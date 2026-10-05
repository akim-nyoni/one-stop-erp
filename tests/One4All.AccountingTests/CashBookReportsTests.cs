using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the CB-numbered cash book reports (brief §14 D): a deposit and a payment through the bank, a statement line not
/// yet on the ledger, a petty cash top-up and spend; the cash book, unreconciled ageing and petty cash report read them;
/// every CB report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CashBookReportsTests
{
    private readonly AccountingFixture _fixture;

    public CashBookReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cash_book_reports_reflect_the_bank()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "1190", "Petty Cash", AccountType.Asset));
        BankAccount bank = new(companyId, "CBMAIN", "Main Bank", Scenario.Usd, "1100");
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Post(string document, DateOnly date, string debit, string credit, decimal amount) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "CB", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = document, Narrative = document,
            Lines = [new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount }],
        })).IsSuccess.Should().BeTrue();
        await Post("CB-DEP", new DateOnly(2026, 3, 2), "1100", "4000", 500m);
        await Post("CB-PAY", new DateOnly(2026, 3, 5), "6000", "1100", 200m);
        await Post("CB-TOPUP", new DateOnly(2026, 3, 6), "1190", "1100", 100m);
        await Post("CB-PETTY", new DateOnly(2026, 3, 7), "6000", "1190", 30m);
        (await scope.ServiceProvider.GetRequiredService<IBankReconciliationService>().ImportStatementLinesAsync(companyId, bank.Id,
            [new StatementLineImport(new DateOnly(2026, 3, 8), "Bank charges", -12m, "CHG")])).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31), ["BankCode"] = "CBMAIN" };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string book = await Render("CB68");
        book.Should().Contain("CBMAIN,,,,Opening balance,,,0.00").And.Contain("Journal CB-DEP,CB-DEP,500.00,,500.00").And.Contain("Journal CB-TOPUP,CB-TOPUP,,100.00,200.00");
        string open = await Render("CB70");
        open.Should().Contain("CBMAIN,Deposit not yet cleared,2026-03-02").And.Contain("CBMAIN,Payment not yet presented,2026-03-05").And.Contain("CBMAIN,\"On statement, not in ledger\",2026-03-08,Bank charges CHG,23");
        (await Render("CB72")).Should().Contain("2026-03-31,USD,200.00,0.00,0.00,200.00");
        (await Render("CB73")).Should().Contain("CB-TOPUP,CB-TOPUP,100.00,,100.00").And.Contain("CB-PETTY,CB-PETTY,,30.00,70.00");

        foreach (string code in CashBookReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
