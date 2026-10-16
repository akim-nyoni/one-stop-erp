using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ReceiptCurrencyTests
{
    private const long Cashier = 7001;

    private readonly AccountingFixture _fixture;

    public ReceiptCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Foreign_currency_debtor_payment_allocates_at_the_counter()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId, customerId, stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            One4All.Core.Trading.Customer payer = new(companyId, "FX-PAYER", "Pays in euros", Scenario.Usd);
            CashierStation station = new(companyId, "TILL-FX2", "FX Counter 2", "RX");
            db.Customers.Add(payer);
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            customerId = payer.Id;
            stationId = station.Id;
        }

        // Two USD charges: 66 (older) and 100.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4All.Application.Abstractions.Trading.ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Trading.ISalesLedgerService>();
            (await ar.PostInvoiceAsync(new One4All.Application.Abstractions.Trading.SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "FX-OLD", DocumentDate = new DateOnly(2026, 1, 31), Currency = Scenario.Usd, NetAmount = 66m, SalesAccountCode = "4000" })).IsSuccess.Should().BeTrue();
            (await ar.PostInvoiceAsync(new One4All.Application.Abstractions.Trading.SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "FX-NEW", DocumentDate = new DateOnly(2026, 2, 28), Currency = Scenario.Usd, NetAmount = 100m, SalesAccountCode = "4000" })).IsSuccess.Should().BeTrue();
        }

        // EUR 100 at 1.10 = USD 110 settles the USD 66 charge whole (EUR 60) and USD 44 of the next (EUR 40): nothing left on account, no exchange difference.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 3, 1), [new FloatDeclaration(Scenario.Eur, 0m)])).Value;
            var receipt = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = customerId, Date = new DateOnly(2026, 3, 1), Currency = Scenario.Eur, CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 100m)],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 100m)],
            });
            receipt.IsSuccess.Should().BeTrue(receipt.IsFailure ? receipt.Error.ToString() : null);
            (receipt.Value.AllocatedToDebt, receipt.Value.CreditOnAccount).Should().Be((100m, 0m));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<One4All.Core.Trading.OpenItem> items = await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == customerId).OrderBy(i => i.DocumentNumber).ToListAsync();
            items.Take(2).Select(i => (i.DocumentNumber, i.OutstandingTransaction, i.OutstandingBase)).Should().Equal(("FX-NEW", 56m, 56m), ("FX-OLD", 0m, 0m));
            items.Single(i => i.Currency == Scenario.Eur).OutstandingTransaction.Should().Be(0m, "the euro receipt is fully drawn");
            List<One4All.Core.Trading.Allocation> allocations = await db.Allocations.AsNoTracking().Where(a => a.CompanyId == companyId && a.TraderId == customerId).OrderBy(a => a.Id).ToListAsync();
            allocations.Select(a => (a.TransactionAmount, a.DebitTransactionAmount, a.DebitBase, a.CreditBase, a.RealisedFxBase)).Should().Equal((60m, 66m, 66m, 66m, 0m), (40m, 44m, 44m, 44m, 0m));
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            (await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31))).IsBalanced.Should().BeTrue();
        }
    }

    [SkippableFact]
    public async Task Foreign_currency_sundry_receipt_converts_to_base()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            CashierStation station = new(companyId, "TILL-F", "FX Counter", "RF");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            stationId = station.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            var open = await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 3, 1), [new FloatDeclaration(Scenario.Eur, 0m)]);
            long sessionId = open.Value;

            // Sundry income of EUR 100 at the daily rate 1.10 → USD 110 base.
            var receipt = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous,
                Date = new DateOnly(2026, 3, 1), Currency = Scenario.Eur, CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 100m, "4400", "Tender document (EUR)")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 100m)],
            });
            receipt.IsSuccess.Should().BeTrue(receipt.IsFailure ? receipt.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4400" && r.Credit == 110m); // EUR 100 × 1.10
            tb.Rows.Should().Contain(r => r.AccountCode == "1000" && r.Debit == 110m);
        }
    }
}
