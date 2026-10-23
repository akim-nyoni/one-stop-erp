using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the title safeguards: applying needs every development covenant complied with and a rates clearance (nothing
/// overdue on the stand's municipal account; a bill not yet due does not stop it); a transfer open on the stand stops an
/// application, and an open application stops a transfer being lodged; and the deed is registered only to the holders
/// the title was applied for - a change of holder, or joint holders registered since, is refused at registration.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TitleDeedSafeguardsTests
{
    private readonly AccountingFixture _fixture;

    public TitleDeedSafeguardsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Title_needs_covenants_met_and_rates_clear_and_holds_the_holder_until_registration()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer holder = new(companyId, "TS-H", "Title Holder", Scenario.Usd);
        Customer ratepayer = new(companyId, "TS-R", "Rates Account", Scenario.Usd);
        Customer buyer = new(companyId, "TS-B", "Would-be Buyer", Scenario.Usd);
        Customer spouse = new(companyId, "TS-S", "Joint Holder", Scenario.Usd);
        db.Customers.AddRange(holder, ratepayer, buyer, spouse);
        LandParcel parcel = new(companyId, $"LP-TS-{companyId}", "Title safeguards parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand first = new(companyId, parcel.Id, null, "TS-1", 300m, 2000m);
        HousingStand second = new(companyId, parcel.Id, null, "TS-2", 300m, 2000m);
        db.HousingStands.AddRange(first, second);
        db.Properties.Add(new Property(companyId, "TS-1", "Ward 4", ratepayer.Id));
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        ITitleDeedService titles = scope.ServiceProvider.GetRequiredService<ITitleDeedService>();
        IDevelopmentCovenantService covenants = scope.ServiceProvider.GetRequiredService<IDevelopmentCovenantService>();
        ITransferApplicationService transfers = scope.ServiceProvider.GetRequiredService<ITransferApplicationService>();
        foreach ((HousingStand stand, string suffix) in new[] { (first, "1"), (second, "2") })
        {
            (await housing.SellStandAsync(companyId, stand.Id, holder.Id, holder.Name, 3000m, new DateOnly(2026, 2, 1), "4100", "1460", "5100", $"TS-INV{suffix}-{companyId}")).IsSuccess.Should().BeTrue();
        }

        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = holder.Id, DocumentNumber = $"TS-R1-{companyId}", ReceiptDate = new DateOnly(2026, 2, 20), Currency = Scenario.Usd, Amount = 6000m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();

        // An unmet development covenant stops the application, naming the covenant.
        string covenant = (await covenants.CreateAsync(companyId, first.Id, holder.Id, new DateOnly(2026, 2, 1), 2)).Value;
        DateOnly applyOn = new(2026, 5, 4);
        Error unmet = (await titles.ApplyAsync(companyId, first.Id, applyOn)).Error;
        unmet.Code.Should().Be("Hse.CovenantUnmet");
        unmet.Message.Should().Contain(covenant);
        foreach (CovenantMilestone milestone in new[] { CovenantMilestone.PlanSubmitted, CovenantMilestone.PlanApproved, CovenantMilestone.ConstructionStarted, CovenantMilestone.Completed })
        {
            (await covenants.RecordMilestoneAsync(companyId, covenant, milestone, new DateOnly(2026, 4, 1), null)).IsSuccess.Should().BeTrue();
        }

        // Rates arrears on the stand's municipal account stop it; a bill not yet due does not.
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = ratepayer.Id, DocumentNumber = $"TS-RATES1-{companyId}", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 80m, SalesAccountCode = "4400" })).IsSuccess.Should().BeTrue();
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = ratepayer.Id, DocumentNumber = $"TS-RATES2-{companyId}", DocumentDate = new DateOnly(2026, 5, 1), Currency = Scenario.Usd, NetAmount = 80m, SalesAccountCode = "4400" })).IsSuccess.Should().BeTrue();
        Error arrears = (await titles.ApplyAsync(companyId, first.Id, applyOn)).Error;
        arrears.Code.Should().Be("Hse.RatesArrears");
        arrears.Message.Should().Contain("80.00");
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = ratepayer.Id, DocumentNumber = $"TS-RR-{companyId}", ReceiptDate = new DateOnly(2026, 5, 2), Currency = Scenario.Usd, Amount = 80m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();

        // A transfer open on the stand stops the application; rejected, title can be applied for.
        TransferLodgement Cession(HousingStand stand, DateOnly date) => new() { CompanyId = companyId, StandId = stand.Id, TransferType = TransferType.Cession, ToCustomerId = buyer.Id, Consideration = 4000m, Date = date };
        string transfer = (await transfers.LodgeAsync(Cession(first, new DateOnly(2026, 5, 3)))).Value;
        (await titles.ApplyAsync(companyId, first.Id, applyOn)).Error.Code.Should().Be("Hse.TransferOpen");
        (await transfers.RejectAsync(companyId, transfer, "Withdrawn by the parties", applyOn)).IsSuccess.Should().BeTrue();
        string title = (await titles.ApplyAsync(companyId, first.Id, applyOn)).Value;

        // While title is in progress the stand cannot be transferred, by application or directly.
        (await transfers.LodgeAsync(Cession(first, new DateOnly(2026, 5, 5)))).Error.Code.Should().Be("Hse.TitleApplicationOpen");
        (await housing.TransferOwnershipAsync(companyId, first.Id, buyer.Id, buyer.Name, 4000m, new DateOnly(2026, 5, 5), true, true)).Error.Code.Should().Be("Hse.TitleApplicationOpen");

        // Joint holders registered after the application: the deed is not registered to the holder applied for.
        Task<Result> Step(string number, TitleStage stage, DateOnly date, string? conveyancer = null, string? deed = null)
            => titles.AdvanceAsync(companyId, number, new TitleStepInput { Stage = stage, Date = date, Conveyancer = conveyancer, DeedNumber = deed });
        (await Step(title, TitleStage.ConveyancerAssigned, new DateOnly(2026, 5, 10), conveyancer: "Ncube Legal")).IsSuccess.Should().BeTrue();
        (await Step(title, TitleStage.Lodged, new DateOnly(2026, 5, 20))).IsSuccess.Should().BeTrue();
        IJointOwnershipService joint = scope.ServiceProvider.GetRequiredService<IJointOwnershipService>();
        (await joint.RegisterAsync(companyId, first.Id, CoOwnershipRegime.JointWithSurvivorship, [new HolderShare(holder.Id, 50m), new HolderShare(spouse.Id, 50m)], new DateOnly(2026, 5, 25))).IsSuccess.Should().BeTrue();
        (await Step(title, TitleStage.Registered, new DateOnly(2026, 6, 15), deed: "T 77/2026")).Error.Code.Should().Be("Hse.TitleHolderChanged");

        // The second stand: title applied for, then its holder changes (repossessed) before registration.
        string other = (await titles.ApplyAsync(companyId, second.Id, applyOn)).Value;
        (await Step(other, TitleStage.ConveyancerAssigned, new DateOnly(2026, 5, 10), conveyancer: "Ncube Legal")).IsSuccess.Should().BeTrue();
        (await Step(other, TitleStage.Lodged, new DateOnly(2026, 5, 20))).IsSuccess.Should().BeTrue();
        HousingStand repossessed = await db.HousingStands.Include(s => s.Ownership).SingleAsync(s => s.Id == second.Id);
        repossessed.Repossess(new DateOnly(2026, 6, 1), 2000m);
        await db.SaveChangesAsync();
        Error changed = (await Step(other, TitleStage.Registered, new DateOnly(2026, 6, 15), deed: "T 78/2026")).Error;
        changed.Code.Should().Be("Hse.TitleHolderChanged");
        (await db.Set<TitleDeed>().AsNoTracking().SingleAsync(t => t.CompanyId == companyId && t.ApplicationNumber == other)).Stage.Should().Be(TitleStage.Lodged);
    }
}
