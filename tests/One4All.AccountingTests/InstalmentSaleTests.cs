using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers an instalment sale under the §4.10.9 ruling: a stand costing 25,000 sold for 13,000 cash price with a 1,000
/// deposit and 12,000 financed at 12% reducing over 12 months. Signature recognises sales of 13,000 and cost of sales of
/// 25,000 at once; the deposit is banked; running to the end of March raises two instalments' interest (230.54) as
/// interest income, and a repeat raises nothing; early settlement at mid-March rebates the remaining 563.69 of interest
/// and leaves 10,098.16 principal plus the raised interest owing; nothing more is raised afterwards.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class InstalmentSaleTests
{
    private readonly AccountingFixture _fixture;

    public InstalmentSaleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_instalment_sale_recognises_the_sale_up_front_and_interest_over_time()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        Customer buyer = new(companyId, "IS-BUY", "Instalment Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-IS-{companyId}", "Parcel", 1000m, 100000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-IS")).Value.LandParcelId;
        long standId = (await housing.SubdivideAsync(companyId, parcelId, null, [new NewStand("IS-1", 250m), new NewStand("IS-2", 250m), new NewStand("IS-3", 250m), new NewStand("IS-4", 250m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds[0];

        IInstalmentSaleService sales = scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>();
        InstalmentSaleRequest Request(decimal deposit) => new()
        {
            CompanyId = companyId, StandId = standId, BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = deposit, AnnualRatePercent = 12m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        };

        (await sales.SignAsync(Request(13000m))).Error.Code.Should().Be("Hse.InstalmentTerms");
        InstalmentSaleResult sale = (await sales.SignAsync(Request(1000m))).Value;
        sale.Schedule.Should().HaveCount(12);
        sale.Schedule[0].Amount.Should().Be(1066.19m);

        InstalmentRunResult march = (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 3, 31))).Value;
        march.Should().Be(new InstalmentRunResult(2, 230.54m));
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 3, 31))).Value.InstalmentsRaised.Should().Be(0);

        (await sales.QuoteSettlementAsync(companyId, sale.AgreementNumber, new DateOnly(2026, 3, 15))).Value.Should().Be(new SettlementQuote(10098.16m, 563.69m));
        (await sales.SettleEarlyAsync(companyId, sale.AgreementNumber, new DateOnly(2026, 3, 15))).Value.InterestRebated.Should().Be(563.69m);
        (await sales.SettleEarlyAsync(companyId, sale.AgreementNumber, new DateOnly(2026, 3, 16))).Error.Code.Should().Be("Hse.AgreementClosed");
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 12, 31))).Value.InstalmentsRaised.Should().Be(0);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "4100").Credit.Should().Be(13000m);
        tb.Rows.Single(r => r.AccountCode == "5100").Debit.Should().Be(25000m);
        tb.Rows.Single(r => r.AccountCode == "4965").Credit.Should().Be(230.54m);
        tb.Rows.Single(r => r.AccountCode == "1200").Debit.Should().Be(12230.54m); // 12,000 principal + interest raised
    }
}
