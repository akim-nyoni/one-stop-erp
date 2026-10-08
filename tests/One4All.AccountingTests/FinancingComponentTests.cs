using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the significant financing component of a long instalment sale (IFRS 15): sold over two years interest-free while credit costs 12%, the sale is
/// its present value; the rest is deferred and earned as interest instalment by instalment, the remainder when the buyer settles early.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FinancingComponentTests
{
    private readonly AccountingFixture _fixture;

    public FinancingComponentTests(AccountingFixture fixture) => _fixture = fixture;

    [Fact]
    public void A_plan_below_the_market_rate_is_discounted_and_the_discount_unwinds_in_full()
    {
        IReadOnlyList<PlannedInstalment> plan = [.. Enumerable.Range(1, 24).Select(n => new PlannedInstalment(n, new DateOnly(2026, 1, 10).AddMonths(n), 500m, 0m, 500m, 12_000m - (500m * n)))];
        FinancingDiscount discount = FinancingComponent.Measure(plan, 12_000m, 12m, new DateOnly(2026, 1, 10), 24)!;
        discount.PresentValue.Should().BeApproximately(10_621.70m, 0.02m, "24 × 500 at 1% a month");
        discount.Discount.Should().Be(12_000m - discount.PresentValue);
        discount.Unwind[1].Should().Be(decimal.Round(discount.PresentValue * 0.01m, 2, MidpointRounding.AwayFromZero));
        discount.Unwind.Values.Sum().Should().Be(discount.Discount);
        FinancingComponent.Measure(plan, 12_000m, 12m, new DateOnly(2026, 1, 10), 12).Should().BeNull("a year or less needs no discounting");
        FinancingComponent.Measure(plan, 12_000m, null, new DateOnly(2026, 1, 10), 24).Should().BeNull();
    }

    [SkippableFact]
    public async Task A_long_interest_free_sale_defers_its_financing_and_earns_it_over_the_term()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "4966", "Housing interest", AccountType.Income), new LedgerAccount(companyId, "2392", "Deferred financing income", AccountType.Liability));
        Customer buyer = new(companyId, "FC-BUY", "Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        HousingScheme scheme = new(companyId, $"SCH-FC-{companyId}", "Financing Scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.HousingMarketRatePercent = 12m;
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcel = (await housing.AcquireLandAsync(companyId, $"LP-FC-{companyId}", "Parcel", 1000m, 10000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-FC")).Value.LandParcelId;
        long stand = (await housing.SubdivideAsync(companyId, parcel, scheme.Id, [new NewStand("FC-1", 500m)], ApportionMethod.ByArea, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds[0];
        IInstalmentSaleService sales = scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>();
        InstalmentSaleRequest Sale(string? deferred) => new()
        {
            CompanyId = companyId, StandId = stand, BuyerCustomerId = buyer.Id, CashPrice = 13_000m, Deposit = 1_000m, AnnualRatePercent = 0m, Method = InterestMethod.Flat,
            TermMonths = 24, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 10), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4966", BankAccountCode = "1100", DeferredFinancingAccountCode = deferred,
        };

        (await sales.SignAsync(Sale(null))).Error.Code.Should().Be("Hse.DeferredFinancingAccount");
        var signed = await sales.SignAsync(Sale("2392"));
        signed.IsSuccess.Should().BeTrue(signed.IsFailure ? signed.Error.ToString() : null);
        string agreement = signed.Value.AgreementNumber;
        decimal Balance(string account) => db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == account)
            .Sum(l => l.BaseCredit - l.BaseDebit);
        decimal discount = Balance("2392");
        discount.Should().BeApproximately(1_378.30m, 0.02m);
        Balance("4100").Should().Be(13_000m - discount, "the sale is the instalments' present value plus the deposit");

        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 2, 28))).Value.InstalmentsRaised.Should().Be(1);
        decimal firstMonth = Balance("4966");
        firstMonth.Should().BeApproximately(106.22m, 0.02m, "1% of the present value in the first month");
        Balance("2392").Should().Be(discount - firstMonth);

        (await sales.SettleEarlyAsync(companyId, agreement, new DateOnly(2026, 3, 15))).IsSuccess.Should().BeTrue();
        Balance("2392").Should().Be(0m, "settling early earns what is left");
        Balance("4966").Should().Be(discount);
    }
}
