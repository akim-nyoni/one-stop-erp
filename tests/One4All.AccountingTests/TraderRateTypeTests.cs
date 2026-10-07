using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers per-trader rate types: a customer on a contract (fixed) rate converts its euro invoices and receipts at that rate; before the
/// contract rate exists it falls back to the daily rate; a supplier with no rate type converts at the daily rate; a rate keyed on the
/// document still wins.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TraderRateTypeTests
{
    private readonly AccountingFixture _fixture;

    public TraderRateTypeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Traders_convert_at_their_own_rate_type_with_the_daily_rate_as_fallback()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Scenario.Eur && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Fixed))
        {
            db.ExchangeRates.Add(new ExchangeRate(Scenario.Eur, Scenario.Usd, ExchangeRateType.Fixed, new DateOnly(2026, 2, 1), 1.25m));
        }

        Customer contract = new(companyId, "RT-CUS", "Contract Customer", Scenario.Eur) { RateType = ExchangeRateType.Fixed };
        Supplier daily = new(companyId, "RT-SUP", "Daily Supplier", Scenario.Eur);
        db.AddRange(contract, daily);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        async Task<decimal> Invoice(string number, DateOnly date, decimal? rate = null) => (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = contract.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Eur, NetAmount = 100m, SalesAccountCode = "4000", ExchangeRate = rate,
        })).Value.BaseAmount;

        (await Invoice("RT-1", new DateOnly(2026, 3, 1))).Should().Be(125m);
        (await Invoice("RT-0", new DateOnly(2026, 1, 15))).Should().Be(110m, "no contract rate yet: the daily rate");
        (await Invoice("RT-2", new DateOnly(2026, 3, 2), 1.3m)).Should().Be(130m, "a keyed rate wins");
        (await ar.PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = contract.Id, DocumentNumber = "RT-R1", ReceiptDate = new DateOnly(2026, 3, 5), Currency = Scenario.Eur, Amount = 40m, BankAccountCode = "1100",
        })).Value.BaseAmount.Should().Be(50m);

        (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = daily.Id, DocumentNumber = "RT-P1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Eur, NetAmount = 100m, ExpenseAccountCode = "5000",
        })).Value.BaseAmount.Should().Be(110m);
    }
}
