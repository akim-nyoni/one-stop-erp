using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the GL-numbered ledger reports (brief §14 A): sales, a reserve movement, a suspense posting and a reversing
/// accrual; the multi-currency trial balance, equity statement, suspense review and accruals schedule read them back;
/// every GL report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LedgerReportsTests
{
    private readonly AccountingFixture _fixture;

    public LedgerReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Ledger_reports_reflect_the_journals()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Post(string document, DateOnly date, string debit, string credit, decimal amount) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = document, Narrative = document,
            Lines = [new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount }],
        })).IsSuccess.Should().BeTrue();
        await Post("GLR-SALE", new DateOnly(2026, 2, 1), "1100", "4000", 1000m);
        await Post("GLR-RES", new DateOnly(2026, 2, 2), "1100", "3300", 200m);
        await Post("GLR-SUS", new DateOnly(2026, 3, 1), "1100", "2600", 50m);
        (await scope.ServiceProvider.GetRequiredService<IAccrualService>().PostAccrualAsync(companyId, new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1), "6000", "2500", 80m, "ACC-1", "Audit fee"))
            .IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 4, 30), ["AsAtDate"] = new DateOnly(2026, 4, 30) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("GL2")).Should().Contain(",USD,\"1,250.00\",1250");
        (await Render("GL5")).Should().Contain("Journal GLR-SALE").And.Contain("Journal GLR-SUS").And.Contain("GL-ACCR ACC-1");
        string equity = await Render("GL8");
        equity.Should().Contain("3300 Revaluation Reserve,0.00,0.00,200.00,200").And.Contain("Retained profit not yet closed to equity,0.00,\"1,000.00\",0.00,1000"); // the accrual reverses inside the period
        (await Render("GL17")).Should().Contain("Suspense: Suspense / Unallocated Control,2600,1,2026-03-01,-50");
        string accruals = await Render("GL23");
        accruals.Should().Contain("Accrual,ACC-1,2026-03-31,Audit fee,,80").And.Contain("Accrual reversal");

        foreach (string code in LedgerReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
