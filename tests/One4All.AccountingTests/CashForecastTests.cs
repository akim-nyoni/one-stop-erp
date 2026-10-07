using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the full cash-flow forecast: the bank balance opens it; an overdue receivable lands in the first week; a supplier in a saved payment
/// run is paid on the run's date and not again on the invoice's due date, while a supplier left out of the run is paid when due; a standing
/// order repeats monthly; an open sales order arrives on delivery plus terms; the week cash falls below the minimum is flagged.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CashForecastTests
{
    private readonly AccountingFixture _fixture;

    public CashForecastTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Every_source_reaches_the_weekly_forecast()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.BankAccounts.Add(new BankAccount(companyId, "FC-BANK", "Main", Scenario.Usd, "1100"));
        Customer customer = new(companyId, "FC-CUS", "Buyer", Scenario.Usd);
        Supplier held = new(companyId, "FC-HELD", "Held Supplier", Scenario.Usd) { OnHold = true };
        Supplier paid = new(companyId, "FC-RUN", "Run Supplier", Scenario.Usd);
        db.AddRange(customer, held, paid);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 4, 1), TransactionCurrency = Scenario.Usd, DocumentType = "Manual", DocumentNumber = "FC-CAP",
            Narrative = "Capital", Lines =
            [
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 1_000m },
                new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 1_000m },
            ],
        })).IsSuccess.Should().BeTrue();

        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "FC-AR1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 300m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        foreach ((Supplier supplier, string number, decimal amount) in new[] { (held, "FC-AP1", 200m), (paid, "FC-AP2", 150m) })
        {
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 20), Currency = Scenario.Usd, NetAmount = amount, ExpenseAccountCode = "5000",
            })).IsSuccess.Should().BeTrue();
        }

        // A saved run paying the run supplier on 8 April; the held supplier is left out of it (paid when due, 19 April).
        (await scope.ServiceProvider.GetRequiredService<IPaymentRunService>().SaveProposalAsync(new PaymentRunRequest
        {
            CompanyId = companyId, PaymentDate = new DateOnly(2026, 4, 8), DueOnOrBefore = new DateOnly(2026, 4, 30), BankAccountCode = "1100",
        }, 3)).IsSuccess.Should().BeTrue();
        db.Add(new StandingOrder(companyId, "FC-RENT", "1100", 50m, RecurrenceFrequency.Monthly, new DateOnly(2026, 4, 10), null, null, "6100", "Rent") { IsActive = true });
        SalesOrder order = new(companyId, customer.Id, "FC-SO1", new DateOnly(2026, 4, 1), Scenario.Usd) { RequestedDeliveryDate = new DateOnly(2026, 4, 15) };
        order.AddLine(0, "Widgets", 2m, 100m, null, "4000");
        db.Add(order);
        await db.SaveChangesAsync();

        CashForecast f = await scope.ServiceProvider.GetRequiredService<ICashForecastService>().ForecastAsync(companyId, new DateOnly(2026, 4, 6), 6, 1_100m);
        f.OpeningBalance.Should().Be(1_000m);
        f.Weeks[0].BySource.Should().Equal(new Dictionary<CashFlowSource, decimal>
        {
            [CashFlowSource.Receivables] = 300m, [CashFlowSource.PaymentRuns] = -150m, [CashFlowSource.StandingOrders] = -50m,
        });
        f.Weeks[1].BySource.Should().Equal(new Dictionary<CashFlowSource, decimal> { [CashFlowSource.Payables] = -200m });
        f.Lines.Should().NotContain(l => l.Reference == "FC-AP2", "the saved run pays it");
        f.Weeks[4].BySource.Should().Equal(new Dictionary<CashFlowSource, decimal> { [CashFlowSource.StandingOrders] = -50m });
        f.Weeks[5].BySource.Should().Equal(new Dictionary<CashFlowSource, decimal> { [CashFlowSource.SalesOrders] = 200m });
        f.Weeks.Select(w => w.Closing).Should().Equal(1_100m, 900m, 900m, 900m, 850m, 1_050m);
        f.FirstShortfall.Should().Be(new DateOnly(2026, 4, 13));
    }
}
