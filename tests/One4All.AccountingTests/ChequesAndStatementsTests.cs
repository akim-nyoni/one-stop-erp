using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers cheques, wallets and supplier statements (brief §14 C 61, D 75–76): a stale, a presented, a voided and a cancelled cheque;
/// a mobile wallet whose unmatched items explain the gap; a supplier statement that differs from our ledger.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ChequesAndStatementsTests
{
    private readonly AccountingFixture _fixture;

    public ChequesAndStatementsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cheques_wallets_and_supplier_statements_are_reconciled()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "1105", "Mobile money", AccountType.Asset));
        BankAccount current = new(companyId, "CHQ", "Current account", Scenario.Usd, "1100");
        BankAccount wallet = new(companyId, "MOMO", "Mobile Wallet", Scenario.Usd, "1105") { IsMobileWallet = true };
        Supplier supplier = new(companyId, "CS-S", "Supplier S", Scenario.Usd);
        db.AddRange(current, wallet, supplier);
        await db.SaveChangesAsync();

        IChequeService cheques = scope.ServiceProvider.GetRequiredService<IChequeService>();
        (await cheques.IssueAsync(companyId, current.Id, "001", new DateOnly(2025, 8, 1), "Supplier A", 100m, "PAY-1")).IsSuccess.Should().BeTrue();
        long second = (await cheques.IssueAsync(companyId, current.Id, "002", new DateOnly(2026, 3, 5), "Supplier B", 200m, null)).Value;
        (await cheques.IssueAsync(companyId, current.Id, "002", new DateOnly(2026, 3, 5), "Again", 1m, null)).Error.Code.Should().Be("Bank.ChequeExists");
        (await cheques.PresentAsync(companyId, second, new DateOnly(2026, 3, 4))).Error.Code.Should().Be("Bank.ChequeDate");
        (await cheques.PresentAsync(companyId, second, new DateOnly(2026, 3, 10))).IsSuccess.Should().BeTrue();
        (await cheques.PresentAsync(companyId, second, new DateOnly(2026, 3, 11))).Error.Code.Should().Be("Bank.ChequeNotIssued");
        (await cheques.VoidLeafAsync(companyId, current.Id, "003", new DateOnly(2026, 3, 6), "Spoilt")).IsSuccess.Should().BeTrue();
        long fourth = (await cheques.IssueAsync(companyId, current.Id, "004", new DateOnly(2026, 3, 20), "Supplier C", 50m, null)).Value;
        (await cheques.CancelAsync(companyId, fourth, new DateOnly(2026, 3, 25), "Lost in post")).IsSuccess.Should().BeTrue();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        foreach ((DateOnly date, decimal amount) in new[] { (new DateOnly(2026, 3, 1), 100m), (new DateOnly(2026, 3, 5), 50m) })
        {
            (await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = "MOMO-IN", Narrative = "Wallet sales",
                Lines = [new JournalLineRequest { AccountCode = "1105", Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount }],
            })).IsSuccess.Should().BeTrue();
        }

        db.BankStatementLines.AddRange(new BankStatementLine(companyId, wallet.Id, new DateOnly(2026, 3, 2), "Cash in", 100m), new BankStatementLine(companyId, wallet.Id, new DateOnly(2026, 3, 6), "Cash in", 30m));
        await db.SaveChangesAsync();

        IPurchaseLedgerService payables = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        foreach ((string number, decimal amount) in new[] { ("INV-1", 300m), ("INV-2", 200m) })
        {
            (await payables.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 3), Currency = Scenario.Usd, NetAmount = amount, ExpenseAccountCode = "6000",
            })).IsSuccess.Should().BeTrue();
        }

        StatementLineInput[] theirs = [new("INV-1", new DateOnly(2026, 3, 3), 300m), new("INV-3", new DateOnly(2026, 3, 28), 120m)];
        (await cheques.CaptureSupplierStatementAsync(companyId, supplier.Id, new DateOnly(2026, 3, 31), 400m, theirs)).Error.Code.Should().Be("Ap.StatementTotal");
        (await cheques.CaptureSupplierStatementAsync(companyId, supplier.Id, new DateOnly(2026, 3, 31), 420m, theirs)).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2025, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("CB75")).Should().Contain("CHQ,001,2025-08-01,Supplier A,PAY-1,Stale,2025-08-01,242,,100").And.Contain("CHQ,002,2026-03-05,Supplier B,-,Presented,2026-03-10,-,,200")
            .And.Contain("CHQ,003,2026-03-06,(void),-,Voided,2026-03-06,-,Spoilt,0").And.Contain("CHQ,004,2026-03-20,Supplier C,-,Cancelled,2026-03-25,-,Lost in post,50");
        (await Render("CB76")).Should().Contain("MOMO Mobile Wallet,150.00,130.00,150.00,130.00,0");
        string recon = await Render("AP61");
        recon.Should().Contain("Supplier S,Balances at 2026-03-31,-,420.00,500.00,-80").And.Contain("Supplier S,On their statement only,INV-3,120.00,-,0")
            .And.Contain("Supplier S,In our ledger only,INV-2,-,200.00,0").And.NotContain("Amount differs");
    }
}
