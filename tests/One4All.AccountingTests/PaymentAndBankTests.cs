using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Banking;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PaymentAndBankTests
{
    private readonly AccountingFixture _fixture;

    public PaymentAndBankTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Sales_credit_note_reduces_the_debtor()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "CN-CUST", "Returns Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "INV-CN", DocumentDate = new DateOnly(2026, 3, 1),
                Currency = Scenario.Usd, NetAmount = 100m, TaxAmount = 15m, SalesAccountCode = "4000", TaxAccountCode = "2200",
            });
            var cn = await ar.PostCreditNoteAsync(new SalesCreditNoteRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "CN-1", DocumentDate = new DateOnly(2026, 3, 5),
                Currency = Scenario.Usd, NetAmount = 20m, TaxAmount = 0m, SalesAccountCode = "4000",
            });
            cn.IsSuccess.Should().BeTrue(cn.IsFailure ? cn.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport aged = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31));
            aged.Total.Should().Be(95m); // 115 invoice − 20 credit note
        }
    }

    [SkippableFact]
    public async Task Payment_run_pays_due_invoices_and_produces_eft_file()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "PAYSUP", "Payable Supplies", Scenario.Usd) { BankAccountNumber = "1234567", BankName = "First Bank" };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "P1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 100m, ExpenseAccountCode = "5000" });
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "P2", DocumentDate = new DateOnly(2026, 2, 15), Currency = Scenario.Usd, NetAmount = 200m, ExpenseAccountCode = "5000" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPaymentRunService run = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();
            var result = await run.RunAsync(new PaymentRunRequest
            {
                CompanyId = companyId, PaymentDate = new DateOnly(2026, 4, 1), DueOnOrBefore = new DateOnly(2026, 4, 1), BankAccountCode = "1100",
            });
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            result.Value.Total.Should().Be(300m);
            result.Value.Lines.Should().ContainSingle(l => l.InvoicesPaid == 2);
            result.Value.EftFile.Length.Should().BeGreaterThan(0);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport creditors = await reports.AgedCreditorsAsync(companyId, new DateOnly(2026, 4, 30));
            creditors.Total.Should().Be(0m); // both invoices settled
        }
    }

    [SkippableFact]
    public async Task Bank_reconciliation_auto_matches_and_reconciles()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long bankAccountId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount bank = new(companyId, "MAIN", "Main Bank", Scenario.Usd, "1100") { BankName = "First Bank", AccountNumber = "1234567" };
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync();
            bankAccountId = bank.Id;
        }

        // Post a cash-book movement to GL 1100: +500 in.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await engine.PostAsync(new JournalRequest { CompanyId = companyId, SourceModule = "CB", PostingDate = new DateOnly(2026, 3, 2), TransactionCurrency = Scenario.Usd, Lines = [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 500m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 500m }] });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            await recon.ImportStatementLinesAsync(companyId, bankAccountId,
            [
                new StatementLineImport(new DateOnly(2026, 3, 2), "Customer deposit", 500m, "DEP1"),
            ]);

            (await recon.AutoMatchAsync(companyId, bankAccountId)).Value.Should().Be(1);

            var report = await recon.GetReconciliationAsync(companyId, bankAccountId, new DateOnly(2026, 3, 31));
            report.IsSuccess.Should().BeTrue(report.IsFailure ? report.Error.ToString() : null);
            report.Value.StatementBalance.Should().Be(500m);
            report.Value.GlBalance.Should().Be(500m);
            report.Value.Difference.Should().Be(0m); // 500 statement = 500 GL, fully matched
        }
    }
}
