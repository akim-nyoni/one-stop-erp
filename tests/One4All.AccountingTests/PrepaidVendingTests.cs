using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers prepaid vending: electricity at 0.25 a unit for an account 100 in arrears; a 50 purchase at a 20% arrears rule
/// takes 10 off arrears and buys 160 units; a 500 purchase at 25% clears the remaining 90 and buys 1,640 units; with no
/// arrears 20 buys 80 units; a stand without electricity and a zero amount are refused; each sale has its own token;
/// the bank holds 570, electricity sales 470 and the account is clear.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PrepaidVendingTests
{
    private readonly AccountingFixture _fixture;

    public PrepaidVendingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Prepaid_sales_pay_down_arrears_and_issue_tokens()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4990", "Electricity Sales", AccountType.Income));
        Tariff power = new(companyId, "ELEC-PP", "Prepaid Electricity", ServiceType.Electricity, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4990") { PerUnitRate = 0.25m };
        db.Tariffs.Add(power);
        Customer owner = new(companyId, "PP-OWN", "Prepaid Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();
        Meter meter = new(companyId, $"E-{companyId}");
        db.Set<Meter>().Add(meter);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "601", "Ward 7", owner.Id);
        stand.AddService(ServiceType.Electricity, power.Id, new DateOnly(2026, 1, 1)).MeterId = meter.Id;
        Property bare = new(companyId, "602", "Ward 7", owner.Id);
        db.Properties.AddRange(stand, bare);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = owner.Id, DocumentNumber = "PP-ARR", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 100m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        IPrepaidVendingService prepaid = scope.ServiceProvider.GetRequiredService<IPrepaidVendingService>();
        PrepaidVendRequest Vend(long propertyId, decimal amount, decimal percent, int day) => new()
        {
            CompanyId = companyId, PropertyId = propertyId, ServiceType = ServiceType.Electricity, Amount = amount, Date = new DateOnly(2026, 3, day), BankAccountCode = "1100", ArrearsPercent = percent,
        };

        (await prepaid.VendAsync(Vend(bare.Id, 50m, 20m, 1))).Error.Code.Should().Be("Mun.ServiceNotLinked");
        (await prepaid.VendAsync(Vend(stand.Id, 0m, 20m, 1))).Error.Code.Should().Be("Mun.VendRequest");

        Result<VendResult> sold = await prepaid.VendAsync(Vend(stand.Id, 50m, 20m, 1));
        sold.IsSuccess.Should().BeTrue(sold.IsFailure ? sold.Error.Message : string.Empty);
        VendResult first = sold.Value;
        first.Should().Match<VendResult>(r => r.ToArrears == 10m && r.ToUnits == 40m && r.Units == 160m && r.ArrearsAfter == 90m);
        first.Token.Should().MatchRegex(@"^\d{4}(-\d{4}){4}$");
        first.TestToken.Should().BeTrue("no vendor generator is registered, so the token is a test one");
        VendResult second = (await prepaid.VendAsync(Vend(stand.Id, 500m, 25m, 2))).Value;
        second.Should().Match<VendResult>(r => r.ToArrears == 90m && r.ToUnits == 410m && r.Units == 1640m && r.ArrearsAfter == 0m);
        VendResult third = (await prepaid.VendAsync(Vend(stand.Id, 20m, 25m, 3))).Value;
        third.Should().Match<VendResult>(r => r.ToArrears == 0m && r.Units == 80m);
        new[] { first.Token, second.Token, third.Token }.Should().OnlyHaveUniqueItems();

        (await prepaid.SalesAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).Select(s => (s.StandNumber, s.Amount, s.Units)).Should().Equal(
            ("601", 50m, 160m), ("601", 500m, 1640m), ("601", 20m, 80m));

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "4990").Credit.Should().Be(470m);
        tb.Rows.Single(r => r.AccountCode == "1100").Debit.Should().Be(570m);
        tb.Rows.Where(r => r.AccountCode == "1200").Sum(r => r.Debit - r.Credit).Should().Be(0m);
    }
}
