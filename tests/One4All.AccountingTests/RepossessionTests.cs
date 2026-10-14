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
/// Covers repossession under the §4.10.9 ruling: a stand costing 25,000 sold on instalments for 13,000 (1,000 down)
/// with two instalments' interest raised (230.54) defaults. Self-approval is refused; repossession at a net realisable
/// value of 20,000 derecognises the 12,230.54 receivable, returns the stand to inventory at 20,000 (the lower of cost
/// and NRV) and to the available pool, records the repossession on the ownership chain, closes the agreement and takes a
/// 7,769.46 gain to the repossession account; the register reports it; a second repossession is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RepossessionTests
{
    private readonly AccountingFixture _fixture;

    public RepossessionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_defaulted_stand_returns_to_inventory_at_the_lower_of_cost_and_nrv()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "4985", "Repossession Gains and Losses", AccountType.Income));
        Customer buyer = new(companyId, "RP-BUY", "Defaulting Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-RP-{companyId}", "Parcel", 1000m, 100000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-RP")).Value.LandParcelId;
        long standId = (await housing.SubdivideAsync(companyId, parcelId, null, [new NewStand("RP-1", 250m), new NewStand("RP-2", 250m), new NewStand("RP-3", 250m), new NewStand("RP-4", 250m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds[0];

        IInstalmentSaleService sales = scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>();
        string agreement = (await sales.SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = standId, BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 12m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        })).Value.AgreementNumber;
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();

        IRepossessionService repossessions = scope.ServiceProvider.GetRequiredService<IRepossessionService>();
        RepossessionRequest Request(long requester, long approver) => new()
        {
            CompanyId = companyId, AgreementNumber = agreement, Date = new DateOnly(2026, 4, 15), NetRealisableValue = 20000m, Reason = "Six months in default",
            RequestedByUserId = requester, ApprovedByUserId = approver, StandsAvailableAccountCode = "1460", GainLossAccountCode = "4985",
        };

        (await repossessions.RepossessAsync(Request(71, 71))).Error.Code.Should().Be("Hse.SelfApproval");
        RepossessionEntry entry = (await repossessions.RepossessAsync(Request(71, 72))).Value;
        entry.Should().Be(new RepossessionEntry(entry.Reference, agreement, "RP-1", new DateOnly(2026, 4, 15), 12230.54m, 20000m, 7769.46m));
        (await repossessions.RepossessAsync(Request(71, 72))).Error.Code.Should().Be("Hse.AgreementClosed");

        HousingStand stand = await db.HousingStands.AsNoTracking().Include(s => s.Ownership).SingleAsync(s => s.Id == standId);
        stand.Status.Should().Be(StandStatus.Repossessed);
        IStandLifecycleService lifecycle = scope.ServiceProvider.GetRequiredService<IStandLifecycleService>();
        (await lifecycle.MoveAsync(companyId, "RP-1", StandStatus.Available, new DateOnly(2026, 4, 20), 72, "Committee 12/2026", "Cleared for re-allocation")).IsSuccess.Should().BeTrue();
        (await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == standId)).Status.Should().Be(StandStatus.Available);
        stand.CurrentHolderCustomerId.Should().BeNull();
        stand.AllocatedCost.Should().Be(20000m);
        stand.Ownership[^1].ChangeType.Should().Be(OwnershipChangeType.Repossession);
        stand.VerifyOwnershipChain().Should().BeTrue();
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 12, 31))).Value.InstalmentsRaised.Should().Be(0);
        (await repossessions.RegisterAsync(companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).Should().Equal(entry);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        Net("4985").Should().Be(-7769.46m);
        Net("1200").Should().Be(0m);
        Net("1460").Should().Be(95000m); // 100,000 subdivided, 25,000 sold, 20,000 back
    }
}
