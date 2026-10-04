using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the land register (brief §14 H6–H8): a caveat on a parcel blocks the sale of its stands until lifted; an open
/// boundary dispute over a stand blocks it too, and disputes only move forward; part of a parcel is sold at a gain and the
/// rest exchanged at a loss, each posted with its cost pro rata to area; subdivided or disputed land cannot be disposed
/// of; and the three register reports read it all back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LandRegisterTests
{
    private readonly AccountingFixture _fixture;

    public LandRegisterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Encumbrances_disputes_and_disposals_are_registered_enforced_and_posted()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "4985", "Gain / Loss on Land Disposal", AccountType.Income));
        Customer buyer = new(companyId, "LR-BUY", "Stand Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        string farm = $"LR-A-{companyId}", township = $"LR-B-{companyId}", received = $"LR-X-{companyId}";
        (await housing.AcquireLandAsync(companyId, farm, "Farm remainder", 3000m, 10000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-LRA")).IsSuccess.Should().BeTrue();
        long townshipId = (await housing.AcquireLandAsync(companyId, township, "Township", 2000m, 4000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-LRB")).Value.LandParcelId;
        IReadOnlyList<long> stands = (await housing.SubdivideAsync(companyId, townshipId, null, [new NewStand("LR-1", 1000m), new NewStand("LR-2", 1000m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds;
        Task<Result<StandSaleResult>> Sell(long standId, string invoice) => housing.SellStandAsync(companyId, standId, buyer.Id, buyer.Name, 5000m, new DateOnly(2026, 3, 1), "4100", "1460", "5100", invoice);

        // A servitude and a caveat; the caveat blocks the township's stands until lifted.
        ILandRegisterService land = scope.ServiceProvider.GetRequiredService<ILandRegisterService>();
        EncumbranceRegistration Encumbrance(string parcel, EncumbranceType type, string reference) => new()
        {
            CompanyId = companyId, ParcelNumber = parcel, Type = type, Reference = reference, InFavourOf = "Power Authority", Description = "Line corridor", Date = new DateOnly(2026, 1, 10),
        };
        (await land.RegisterEncumbranceAsync(Encumbrance(farm, EncumbranceType.Servitude, "SV-1"))).IsSuccess.Should().BeTrue();
        long caveat = (await land.RegisterEncumbranceAsync(Encumbrance(township, EncumbranceType.Caveat, "CAV-1") with { InFavourOf = "High Court" })).Value;
        (await land.RegisterEncumbranceAsync(Encumbrance(township, EncumbranceType.Caveat, " "))).Error.Code.Should().Be("Hse.EncumbranceDetails");
        (await Sell(stands[0], "INV-LR-1")).Error.Code.Should().Be("Housing.StandBlocked");
        (await land.LiftEncumbranceAsync(companyId, caveat, new DateOnly(2026, 1, 5), "CAN-1")).Error.Code.Should().Be("Hse.LiftDate");
        (await land.LiftEncumbranceAsync(companyId, caveat, new DateOnly(2026, 2, 1), "CAN-1")).IsSuccess.Should().BeTrue();
        (await land.LiftEncumbranceAsync(companyId, caveat, new DateOnly(2026, 2, 2), "CAN-1")).Error.Code.Should().Be("Hse.EncumbranceLifted");
        (await Sell(stands[0], "INV-LR-1")).IsSuccess.Should().BeTrue();

        // A boundary dispute over the second stand blocks it; stages only move forward.
        LandDisputeLodgement Dispute(string parcel, string? stand, LandDisputeType type) => new()
        {
            CompanyId = companyId, ParcelNumber = parcel, StandNumber = stand, Type = type, Claimant = "Neighbour", Particulars = "Wall 2m over the line", Date = new DateOnly(2026, 2, 1),
        };
        (await land.OpenDisputeAsync(Dispute(township, "NOPE", LandDisputeType.Boundary))).Error.Code.Should().Be("Hse.StandNotFound");
        string boundary = (await land.OpenDisputeAsync(Dispute(township, "LR-2", LandDisputeType.Boundary))).Value;
        (await Sell(stands[1], "INV-LR-2")).Error.Code.Should().Be("Housing.StandBlocked");
        (await land.AdvanceDisputeAsync(companyId, boundary, LandDisputeStage.Mediation, new DateOnly(2026, 3, 1), "Referred to mediator")).IsSuccess.Should().BeTrue();
        (await land.AdvanceDisputeAsync(companyId, boundary, LandDisputeStage.Negotiation, new DateOnly(2026, 3, 2), "Back to talks")).Error.Code.Should().Be("Hse.DisputeStage");
        (await land.AdvanceDisputeAsync(companyId, boundary, LandDisputeStage.Resolved, new DateOnly(2026, 2, 15), "Settled")).Error.Code.Should().Be("Hse.DisputeDate");

        // Sell a third of the farm at a gain, exchange the rest at a loss.
        LandDisposalRequest Disposal(string parcel, LandDisposalType type, decimal area, decimal consideration) => new()
        {
            CompanyId = companyId, ParcelNumber = parcel, Type = type, Counterparty = "Mining Co", ResolutionReference = "RES-2026-14", Date = new DateOnly(2026, 4, 1),
            AreaSquareMetres = area, Consideration = consideration, LandAccountCode = "1450", ProceedsAccountCode = "1100", GainLossAccountCode = "4985",
        };
        (await land.DisposeAsync(Disposal(township, LandDisposalType.Sale, 0m, 5000m))).Error.Code.Should().Be("Hse.ParcelNotUndivided");
        (await land.DisposeAsync(Disposal(farm, LandDisposalType.Donation, 0m, 100m))).Error.Code.Should().Be("Hse.DisposalConsideration");
        LandDisposalResult sale = (await land.DisposeAsync(Disposal(farm, LandDisposalType.Sale, 1000m, 5000m))).Value;
        sale.CostDerecognised.Should().Be(3333.33m);
        sale.GainOrLoss.Should().Be(1666.67m);
        (await land.DisposeAsync(Disposal(farm, LandDisposalType.Exchange, 0m, 6000m))).Error.Code.Should().Be("Hse.ExchangeParcel");
        LandDisposalResult swap = (await land.DisposeAsync(Disposal(farm, LandDisposalType.Exchange, 0m, 6000m) with { ExchangeParcelNumber = received, ExchangeParcelArea = 2500m })).Value;
        swap.CostDerecognised.Should().Be(6666.67m);
        swap.GainOrLoss.Should().Be(-666.67m);
        LandParcel farmParcel = await db.LandParcels.AsNoTracking().SingleAsync(p => p.CompanyId == companyId && p.ParcelNumber == farm);
        farmParcel.Status.Should().Be(LandParcelStatus.Disposed);
        farmParcel.AcquisitionCost.Should().Be(0m);
        (await db.LandParcels.AsNoTracking().SingleAsync(p => p.Id == swap.ExchangeParcelId)).AcquisitionCost.Should().Be(6000m);

        // An open ownership dispute stops a disposal.
        (await land.OpenDisputeAsync(Dispute(received, null, LandDisputeType.Ownership))).IsSuccess.Should().BeTrue();
        (await land.DisposeAsync(Disposal(received, LandDisposalType.Sale, 0m, 7000m))).Error.Code.Should().Be("Hse.ParcelDisputed");

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        TrialBalanceRowFor(tb, "4985").Should().Be(-1000m); // 1,666.67 gain less 666.67 loss, as a net credit

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, DateOnly asAt)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = asAt }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("H6", new DateOnly(2026, 4, 30))).Should().Contain($"{sale.DisposalNumber},2026-04-01,{farm},Sale,Mining Co,RES-2026-14").And.Contain("1666.67")
            .And.Contain(received).And.Contain("-666.67");
        (await Render("H7", new DateOnly(2026, 4, 30))).Should().Contain("SV-1").And.Contain("In force").And.Contain("CAV-1").And.Contain("Lifted 2026-02-01").And.Contain("High Court");
        (await Render("H8", new DateOnly(2026, 2, 20))).Should().Contain($"{boundary},{township},LR-2,Boundary,Neighbour,2026-02-01,Reported,2026-02-01,19");
        (await Render("H8", new DateOnly(2026, 4, 30))).Should().Contain($"{boundary},{township},LR-2,Boundary,Neighbour,2026-02-01,Mediation,2026-03-01,88");
    }

    private static decimal TrialBalanceRowFor(ConsolidatedTrialBalance tb, string account)
    {
        var row = tb.Rows.Single(r => r.AccountCode == account);
        return row.Debit - row.Credit;
    }
}
