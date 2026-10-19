using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// A stand costing 25,000 sold on instalments for 13,000 (1,000 down), two instalments' interest raised, is surrendered by its buyer: approved by a
/// second user, the 12,230.54 receivable is written off, the stand returns to inventory at its 25,000 cost (below the 30,000 it would fetch), the
/// 1,000 deposit is credited back to the buyer, the agreement closes and the surrender joins the ownership chain; the stand cannot be surrendered by a
/// plain status move.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandSurrenderTests
{
    private readonly AccountingFixture _fixture;

    public StandSurrenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_surrendered_stand_comes_back_through_an_approved_document()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "4986", "Surrender Gains and Losses", AccountType.Income));
        Customer buyer = new(companyId, "SU-BUY", "Surrendering Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-SU-{companyId}", "Parcel", 1000m, 100000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-SU")).Value.LandParcelId;
        long standId = (await housing.SubdivideAsync(companyId, parcelId, null, [new NewStand("SU-1", 250m), new NewStand("SU-2", 250m), new NewStand("SU-3", 250m), new NewStand("SU-4", 250m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds[0];
        IInstalmentSaleService sales = scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>();
        (await sales.SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = standId, BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 12m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();

        IStandLifecycleService lifecycle = scope.ServiceProvider.GetRequiredService<IStandLifecycleService>();
        (await lifecycle.MoveAsync(companyId, "SU-1", StandStatus.Surrendered, new DateOnly(2026, 4, 1), 72, "Letter", "Gives it back")).Error.Code.Should().Be("Hse.StandMoveByDocument");

        IRepossessionService service = scope.ServiceProvider.GetRequiredService<IRepossessionService>();
        StandSurrenderRequest Request(long requester, long approver) => new()
        {
            CompanyId = companyId, StandNumber = "su-1", Date = new DateOnly(2026, 4, 15), Reason = "Relocating", RequestedByUserId = requester, ApprovedByUserId = approver,
            NetRealisableValue = 30000m, StandsAvailableAccountCode = "1460", GainLossAccountCode = "4986", Refund = 1000m, RefundAccountCode = "4986",
        };
        (await service.SurrenderAsync(Request(71, 71))).Error.Code.Should().Be("Hse.SelfApproval");
        StandSurrenderEntry entry = (await service.SurrenderAsync(Request(71, 72))).Value;
        entry.Should().Be(new StandSurrenderEntry(entry.Reference, "SU-1", "SU-BUY", new DateOnly(2026, 4, 15), 12230.54m, 25000m, 1000m));
        (await service.SurrenderAsync(Request(71, 72))).Error.Code.Should().Be("Hse.StandNotHeld");

        HousingStand stand = await db.HousingStands.AsNoTracking().Include(s => s.Ownership).SingleAsync(s => s.Id == standId);
        (stand.Status, stand.CurrentHolderCustomerId, stand.AllocatedCost).Should().Be((StandStatus.Surrendered, (long?)null, 25000m));
        stand.Ownership[^1].ChangeType.Should().Be(OwnershipChangeType.Surrender);
        stand.VerifyOwnershipChain().Should().BeTrue();
        (await db.Set<InstalmentAgreement>().AsNoTracking().SingleAsync(a => a.StandId == standId)).Status.Should().Be(InstalmentAgreementStatus.Surrendered);
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 12, 31))).Value.InstalmentsRaised.Should().Be(0);
        (await service.SurrenderRegisterAsync(companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).Should().Equal(entry);
        (await lifecycle.MoveAsync(companyId, "SU-1", StandStatus.Available, new DateOnly(2026, 4, 20), 72, "Committee 13/2026", "Back in the pool")).IsSuccess.Should().BeTrue();

        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 12, 31));
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        Net("1460").Should().Be(100000m, "the sold stand is back at its cost");
        Net("4986").Should().Be(-25000m + 12230.54m + 1000m);
        Net("1200").Should().Be(-1000m, "the deposit refund stands to the buyer's credit");
    }
}
