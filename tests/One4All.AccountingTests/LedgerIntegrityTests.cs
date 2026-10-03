using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers check data: after ordinary activity across AR, AP, stock, deferred release and reversal the company is
/// clean; then each kind of disagreement is planted directly in the database — a cached period balance, a stock
/// quantity, an open item's outstanding, a deleted (deferred) journal leaving a number gap, and a posted line
/// edited with its guard trigger disabled — and each is reported; rebuilding the period balances clears that class.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LedgerIntegrityTests
{
    private readonly AccountingFixture _fixture;

    public LedgerIntegrityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Check_data_is_clean_after_normal_activity_and_finds_each_planted_fault()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long invoiceId;
        long itemId;
        long gapJournalId;
        string gapJournalNumber;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "CHK-C", "Buyer", Scenario.Usd);
            Supplier supplier = new(companyId, "CHK-S", "Supplier", Scenario.Usd);
            Item item = new(companyId, "CHK-ITEM", "Widget") { CostOfSalesAccountCode = "5000" };
            Warehouse wh = new(companyId, "CHKWH", "Main");
            db.AddRange(customer, supplier, item, wh);
            await db.SaveChangesAsync();
            itemId = item.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

            invoiceId = (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "CHK-I1", DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000" })).Value.OpenItemId;
            long receipt = (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "CHK-R1", ReceiptDate = new DateOnly(2026, 3, 20), Currency = Scenario.Usd, Amount = 400m, BankAccountCode = "1100" })).Value.OpenItemId;
            (await alloc.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceId, receipt, 400m, new DateOnly(2026, 3, 20))).IsSuccess.Should().BeTrue();
            long purchase = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "CHK-P1", DocumentDate = new DateOnly(2026, 3, 12), Currency = Scenario.Usd, NetAmount = 500m, ExpenseAccountCode = "6800" })).Value.OpenItemId;
            long payment = (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "CHK-PAY", PaymentDate = new DateOnly(2026, 3, 25), Currency = Scenario.Usd, Amount = 500m, BankAccountCode = "1100" })).Value.OpenItemId;
            (await alloc.AllocateAsync(companyId, TradingLedger.AccountsPayable, payment, purchase, 500m, new DateOnly(2026, 3, 25))).IsSuccess.Should().BeTrue();

            // Stock at awkward costs so the moving average rounds.
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 1), Quantity = 3m, UnitCost = 1.00m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 2), Quantity = 7m, UnitCost = 1.07m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            (await inv.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 5), Quantity = 3m })).IsSuccess.Should().BeTrue();
            (await inv.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 6), Quantity = 1m })).IsSuccess.Should().BeTrue();

            // A deferred journal released, and a posted journal reversed.
            long deferred = (await engine.PostAsync(Journal(companyId, "CHK-D", 75m, deferred: true))).Value.JournalId;
            (await engine.ReleaseDeferredAsync(companyId, deferred)).IsSuccess.Should().BeTrue();
            long wrong = (await engine.PostAsync(Journal(companyId, "CHK-W", 60m, deferred: false))).Value.JournalId;
            (await engine.ReverseAsync(companyId, wrong, new DateOnly(2026, 3, 28), "Posted in error")).IsSuccess.Should().BeTrue();

            // A deferred journal that will be deleted (leaving a numbering gap), followed by a later posting.
            var gap = (await engine.PostAsync(Journal(companyId, "CHK-GAP", 10m, deferred: true))).Value;
            (gapJournalId, gapJournalNumber) = (gap.JournalId, gap.JournalNumber);
            (await engine.PostAsync(Journal(companyId, "CHK-LAST", 5m, deferred: false))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            LedgerIntegrityReport clean = (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value;
            clean.Issues.Should().BeEmpty(string.Join(Environment.NewLine, clean.Issues.Select(i => $"{i.Check} {i.Subject}: {i.Detail}")));
            clean.JournalsChecked.Should().BeGreaterThan(8);
            clean.StockBalancesChecked.Should().Be(1);
            clean.OpenItemsChecked.Should().Be(4);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().RebuildPeriodBalancesAsync(companyId)).Value.NothingToDo.Should().BeTrue();
        }

        // Plant one fault of each kind.
        long tamperedLineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE gl.period_balance SET base_debit = base_debit + 99 WHERE company_id = {companyId} AND account_code = '6800'");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inv.stock_balance SET quantity_on_hand = quantity_on_hand + 5 WHERE company_id = {companyId} AND item_id = {itemId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE trd.open_item SET outstanding_base = 1000 WHERE id = {invoiceId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM gl.journal WHERE id = {gapJournalId}");

            tamperedLineId = await db.Set<Core.Accounting.JournalLine>()
                .Where(l => db.Journals.Any(j => j.Id == l.JournalEntryId && j.CompanyId == companyId && j.DocumentNumber == "CHK-LAST") && l.AccountCode == "1000")
                .Select(l => l.Id).SingleAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE gl.journal_line DISABLE TRIGGER trg_journal_line_posted_guard");
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE gl.journal_line SET base_debit = 7, transaction_debit = 7 WHERE id = {tamperedLineId}");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE gl.journal_line ENABLE TRIGGER trg_journal_line_posted_guard");
            }
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ILedgerIntegrityService integrity = scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>();
            LedgerIntegrityReport report = (await integrity.CheckAsync(companyId)).Value;

            report.Issues.Should().ContainSingle(i => i.Check == IntegrityCheck.UnbalancedJournal)
                .Which.Detail.Should().Contain("differ by 2.00");
            report.Issues.Should().ContainSingle(i => i.Check == IntegrityCheck.JournalNumberGap)
                .Which.Subject.Should().Be(gapJournalNumber);
            report.Issues.Where(i => i.Check == IntegrityCheck.PeriodBalance).Select(i => i.Subject)
                .Should().HaveCount(2).And.Contain(s => s.StartsWith("6800 ")).And.Contain(s => s.StartsWith("1000 "));
            report.Issues.Should().ContainSingle(i => i.Check == IntegrityCheck.StockQuantity)
                .Which.Should().BeEquivalentTo(new { Subject = "CHK-ITEM @ CHKWH", Expected = 6m, Actual = 11m });
            report.Issues.Should().ContainSingle(i => i.Check == IntegrityCheck.OpenItemOutstanding)
                .Which.Should().BeEquivalentTo(new { Expected = 600m, Actual = 1000m });
            report.ErrorCount.Should().Be(5);
            report.WarningCount.Should().Be(1);
            report.Issues[0].Severity.Should().Be(IntegritySeverity.Error);

            // Rebuilding restates the cached balances from the lines: the period-balance findings go, the rest stay.
            PeriodBalanceRebuildResult rebuilt = (await integrity.RebuildPeriodBalancesAsync(companyId)).Value;
            rebuilt.Corrected.Should().Be(2);
            rebuilt.Created.Should().Be(0);
            LedgerIntegrityReport after = (await integrity.CheckAsync(companyId)).Value;
            after.Issues.Should().NotContain(i => i.Check == IntegrityCheck.PeriodBalance);
            after.ErrorCount.Should().Be(3);
        }
    }

    private static JournalRequest Journal(long companyId, string number, decimal amount, bool deferred) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 27), TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = number, Narrative = number, Deferred = deferred,
        Lines =
        [
            new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
