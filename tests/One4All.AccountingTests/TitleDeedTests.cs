using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the title deed pipeline: title needs a holder who has paid in full; it is applied for once; the application
/// moves stage by stage with its particulars (conveyancer, the registry's query, deed number, who signed for it) and
/// cannot skip a stage; a blocked stand stops it; and the pipeline report ages each open application at its stage as at
/// any date, dropping deeds once handed over.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TitleDeedTests
{
    private readonly AccountingFixture _fixture;

    public TitleDeedTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Title_deeds_move_through_the_pipeline_and_are_aged_at_every_stage()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer first = new(companyId, "TD-1", "First Owner", Scenario.Usd);
        Customer second = new(companyId, "TD-2", "Second Owner", Scenario.Usd);
        db.Customers.AddRange(first, second);
        LandParcel parcel = new(companyId, $"LP-TD-{companyId}", "Title parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand paidUp = new(companyId, parcel.Id, null, "TD-1", 300m, 2000m);
        HousingStand unsold = new(companyId, parcel.Id, null, "TD-2", 300m, 2000m);
        HousingStand later = new(companyId, parcel.Id, null, "TD-3", 300m, 2000m);
        db.HousingStands.AddRange(paidUp, unsold, later);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        ITitleDeedService titles = scope.ServiceProvider.GetRequiredService<ITitleDeedService>();
        (await titles.ApplyAsync(companyId, unsold.Id, new DateOnly(2026, 5, 1))).Error.Code.Should().Be("Hse.NoHolder");

        (await housing.SellStandAsync(companyId, paidUp.Id, first.Id, first.Name, 6000m, new DateOnly(2026, 4, 1), "4100", "1460", "5100", $"TD-INV1-{companyId}")).IsSuccess.Should().BeTrue();
        (await titles.ApplyAsync(companyId, paidUp.Id, new DateOnly(2026, 5, 1))).Error.Code.Should().Be("Hse.NotFullyPaid");
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = first.Id, DocumentNumber = $"TD-R1-{companyId}", ReceiptDate = new DateOnly(2026, 4, 25), Currency = Scenario.Usd, Amount = 6000m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        string a = (await titles.ApplyAsync(companyId, paidUp.Id, new DateOnly(2026, 5, 1))).Value;
        (await titles.ApplyAsync(companyId, paidUp.Id, new DateOnly(2026, 5, 2))).Error.Code.Should().Be("Hse.TitleApplied");

        (await housing.SellStandAsync(companyId, later.Id, second.Id, second.Name, 5000m, new DateOnly(2026, 4, 1), "4100", "1460", "5100", $"TD-INV2-{companyId}")).IsSuccess.Should().BeTrue();
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = second.Id, DocumentNumber = $"TD-R2-{companyId}", ReceiptDate = new DateOnly(2026, 4, 25), Currency = Scenario.Usd, Amount = 5000m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        string b = (await titles.ApplyAsync(companyId, later.Id, new DateOnly(2026, 5, 1))).Value;

        // Stage by stage, with the particulars each stage needs.
        Task<One4All.Core.Common.Result> Step(string number, TitleStage stage, int month, int day, string? conveyancer = null, string? note = null, string? deed = null, string? receivedBy = null)
            => titles.AdvanceAsync(companyId, number, new TitleStepInput { Stage = stage, Date = new DateOnly(2026, month, day), Conveyancer = conveyancer, Note = note, DeedNumber = deed, ReceivedBy = receivedBy });
        (await Step(a, TitleStage.Lodged, 5, 5)).Error.Code.Should().Be("Hse.TitleStage");
        (await Step(a, TitleStage.ConveyancerAssigned, 5, 10)).Error.Code.Should().Be("Hse.Conveyancer");
        (await Step(a, TitleStage.ConveyancerAssigned, 5, 10, conveyancer: "Moyo & Partners")).IsSuccess.Should().BeTrue();
        (await Step(a, TitleStage.Lodged, 5, 20)).IsSuccess.Should().BeTrue();
        (await Step(a, TitleStage.QueryRaised, 6, 1, note: "Diagram does not match the general plan")).IsSuccess.Should().BeTrue();
        (await Step(a, TitleStage.Registered, 6, 5, deed: "T 1/2026")).Error.Code.Should().Be("Hse.TitleStage");
        (await Step(a, TitleStage.QueryResolved, 6, 20)).IsSuccess.Should().BeTrue();
        (await Step(a, TitleStage.Registered, 7, 15)).Error.Code.Should().Be("Hse.DeedNumber");
        (await Step(a, TitleStage.Registered, 7, 15, deed: "T 1234/2026")).IsSuccess.Should().BeTrue();
        (await Step(a, TitleStage.InCustody, 7, 25)).IsSuccess.Should().BeTrue();

        // A caveat on the stand stops title processing.
        db.Set<StandBlock>().Add(new StandBlock(companyId, later.Id, BlockReason.FamilyDispute, "Estate dispute", "AFF-9", 1, new DateOnly(2026, 5, 2)));
        await db.SaveChangesAsync();
        (await Step(b, TitleStage.ConveyancerAssigned, 5, 10, conveyancer: "Dube Attorneys")).Error.Code.Should().Be("Hse.StandBlocked");

        // Aged as at June: the first at its query (with its conveyancer), the second still waiting since application.
        TitlePipelineReport june = await titles.PipelineAsync(companyId, new DateOnly(2026, 6, 10));
        june.Stages.Should().Equal(new StageAgeing(TitleStage.Applied, 1, 40, 40), new StageAgeing(TitleStage.QueryRaised, 1, 9, 9));
        june.Applications.Should().Equal(new OpenTitle(b, "TD-3", TitleStage.Applied, 40, 40, null), new OpenTitle(a, "TD-1", TitleStage.QueryRaised, 9, 40, "Moyo & Partners"));

        // As at late August: one deed sitting uncollected in custody for 30 days.
        TitlePipelineReport august = await titles.PipelineAsync(companyId, new DateOnly(2026, 8, 24));
        august.Stages.Should().Equal(new StageAgeing(TitleStage.Applied, 1, 115, 115), new StageAgeing(TitleStage.InCustody, 1, 30, 30));

        (await Step(a, TitleStage.HandedOver, 8, 25)).Error.Code.Should().Be("Hse.Receipt");
        (await Step(a, TitleStage.HandedOver, 8, 25, receivedBy: "First Owner (ID verified)")).IsSuccess.Should().BeTrue();
        (await titles.PipelineAsync(companyId, new DateOnly(2026, 8, 26))).Applications.Select(x => x.ApplicationNumber).Should().Equal(b);
        (await titles.PipelineAsync(companyId, new DateOnly(2026, 8, 24))).Applications.Should().HaveCount(2); // history stays reproducible
    }
}
