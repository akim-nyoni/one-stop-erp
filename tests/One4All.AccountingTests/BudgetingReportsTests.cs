using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the B-coded budgeting reports (brief §14 A-B): an operating budget revised from 100 to 120 a month, spending above
/// it, and spending on an account with no budget; the version comparison, register, utilisation, exceptions and
/// unappropriated-vote reports read them back; every B report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetingReportsTests
{
    private readonly AccountingFixture _fixture;

    public BudgetingReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Budgeting_reports_reflect_budgets_and_actuals()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId && y.Code == "2026").Select(y => y.Id).SingleAsync();

        Budget original = new(companyId, yearId, "OPEX", BudgetType.OriginalApproved, null, 1);
        Budget revised = new(companyId, yearId, "OPEX", BudgetType.SupplementaryRevised, null, 2);
        for (int period = 1; period <= 12; period++)
        {
            original.AddLine("6000", period, 100m);
            revised.AddLine("6000", period, 120m);
        }

        original.Approve();
        original.Supersede();
        revised.Approve();
        revised.Activate();
        db.Budgets.AddRange(original, revised);
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Spend(string document, DateOnly date, string account, decimal amount) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = document, Narrative = document,
            Lines = [new JournalLineRequest { AccountCode = account, Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = amount }],
        })).IsSuccess.Should().BeTrue();
        await Spend("BG-1", new DateOnly(2026, 1, 15), "6000", 150m);
        await Spend("BG-2", new DateOnly(2026, 2, 15), "6000", 150m);
        await Spend("BG-3", new DateOnly(2026, 3, 15), "6000", 150m);
        await Spend("BG-4", new DateOnly(2026, 3, 20), "6100", 200m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("B12")).Should().Contain("OPEX,v1 → v2,6000,").And.Contain("\"1,200.00\",\"1,440.00\",+20.0%,240");
        (await Render("B17")).Should().Contain("OPEX,2026,OriginalApproved,1,Superseded").And.Contain("OPEX,2026,SupplementaryRevised,2,Active,Yes");
        (await Render("B24")).Should().Contain("6000,").And.Contain(",All,\"1,440.00\",450.00,0.00,990");
        (await Render("B28")).Should().Contain("6100,Salaries & Wages,1,200");
        (await Render("B33")).Should().Contain("6000,").And.Contain("360.00,125.0%,Red,450");
        (await Render("B34")).Should().Contain("360.00,125.0%,Red,90");

        Budget ask = new(companyId, yearId, "WORKS", BudgetType.DraftSubmission, "Works", 1), trimmed = new(companyId, yearId, "WORKS", BudgetType.DraftSubmission, "Works", 2);
        Budget roundOne = new(companyId, yearId, "RF", BudgetType.Reforecast, null, 1), roundTwo = new(companyId, yearId, "RF", BudgetType.Reforecast, null, 2) { Reason = "Fuel prices" };
        Budget cash = new(companyId, yearId, "CASH", BudgetType.Cash, null, 1);
        for (int period = 1; period <= 12; period++)
        {
            ask.AddLine("6000", period, 150m);
            trimmed.AddLine("6000", period, 125m);
            roundOne.AddLine("6000", period, 130m);
            roundTwo.AddLine("6000", period, 150m);
            cash.AddLine("1100", period, -100m);
        }

        db.Accounts.Add(new LedgerAccount(companyId, "1695", "Capital WIP", AccountType.Asset));
        One4All.Core.FixedAssets.CapitalProject plant = new(companyId, "cp1", "Plant upgrade", "1695", 10_000m, new DateOnly(2026, 1, 5));
        plant.AddCost(new One4All.Core.FixedAssets.CapitalProjectCost(new DateOnly(2026, 2, 1), "Deposit", 2_500m, "J-CP1"));
        db.AddRange(ask, trimmed, roundOne, roundTwo, cash, plant);
        await db.SaveChangesAsync();

        (await Render("B1")).Should().Contain("Works,In progress,2,");
        (await Render("B13")).Should().Contain("Works,6000,").And.Contain("\"1,800.00\",\"1,500.00\",0.00,1800");
        (await Render("B19")).Should().Contain(",All,360.00,360.00,360.00,360.00,1440");
        (await Render("B21")).Should().Contain("6000,").And.Contain(",—,1440");
        (await Render("B22")).Should().Contain("OPEX v2,Active,6000,\"1,200.00\",\"1,440.00\",,240");
        (await Render("B38")).Should().Contain(",All,\"1,440.00\",450.00,\"1,350.00\",\"1,800.00\",-360");
        (await Render("B39")).Should().Contain("\"1,560.00\",\"1,800.00\",Fuel prices,240");
        string cashFlow = await Render("B41");
        cashFlow.Should().Contain("1,2026-01,-100.00,-150.00,-50").And.Contain("3,2026-03,-100.00,-350.00,-250");
        (await Render("B42")).Should().Contain("CP1,Plant upgrade,In progress,\"10,000.00\",\"2,500.00\",0.00,25.0%,7500");

        foreach (string code in BudgetingReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
