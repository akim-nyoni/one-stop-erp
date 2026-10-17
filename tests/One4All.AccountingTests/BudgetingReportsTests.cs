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
        (await Render("B21")).Should().Contain("6000,").And.Contain(",-,1440");
        (await Render("B22")).Should().Contain("OPEX v2,Active,6000,\"1,200.00\",\"1,440.00\",,240");
        (await Render("B38")).Should().Contain(",All,\"1,440.00\",450.00,\"1,350.00\",\"1,800.00\",-360");
        (await Render("B39")).Should().Contain("\"1,560.00\",\"1,800.00\",Fuel prices,240");
        string cashFlow = await Render("B41");
        cashFlow.Should().Contain("1,2026-01,-100.00,-150.00,-50").And.Contain("3,2026-03,-100.00,-350.00,-250");
        (await Render("B42")).Should().Contain("CP1,Plant upgrade,In progress,\"10,000.00\",\"2,500.00\",0.00,25.0%,7500");

        Budget capex = new(companyId, yearId, "CAPEX", BudgetType.Capital, null, 1);
        capex.AddLine("1600", 2, 3_000m);
        capex.AddLine("1600", 6, 7_000m);
        capex.Approve();
        Budget maint1 = new(companyId, yearId, "MAINT", BudgetType.DraftSubmission, null, 1), maint2 = new(companyId, yearId, "MAINT", BudgetType.DraftSubmission, null, 2);
        maint1.AddLine("6200", 1, 600m);
        maint2.AddLine("6200", 1, 200m);
        maint2.AddLine("6200", 2, 400m);
        db.AddRange(capex, maint1, maint2);
        await db.SaveChangesAsync();

        string phasing = await Render("B9");
        phasing.Should().Contain("CAPEX v1,1600,2,2026-02,\"3,000.00\",30.0%,3000").And.Contain("CAPEX v1,1600,6,2026-06,\"10,000.00\",100.0%,7000");
        (await Render("B14")).Should().Contain(",OPEX v2,");
        (await Render("B23")).Should().Contain("MAINT,v1 → v2,6200,600.00,\"P1 -400.00, P2 +400.00\",400");
        (await Render("B30")).Should().Contain("1600,").And.Contain("\"10,000.00\",0.00,0.0%,10000");
        string rolling = await Render("B40");
        rolling.Should().Contain("6000,").And.Contain(",2026-04,Reforecast,150").And.Contain(",2026-06,Reforecast,150").And.NotContain("2026-07");

        // B11 version register, B16 archived retrieval, B31 available by basis and level, B43 revenue performance.
        async Task<string> RenderWith(string code, Dictionary<string, object?> parameters)
        {
            parameters["AsAtDate"] = new DateOnly(2026, 3, 31);
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string versions = await Render("B11");
        versions.Should().Contain("OPEX,1,OriginalApproved,Superseded").And.Contain("OPEX,2,SupplementaryRevised,Active").And.Contain("Yes").And.Contain("not sealed");
        string archive = await RenderWith("B16", new() { ["BudgetLabel"] = "opex" });
        archive.Should().Contain("OPEX v2 2026 - seal verified").And.Contain("TOTAL").And.Contain("1440");
        (await RenderWith("B16", new() { ["BudgetLabel"] = "OPEX", ["Version"] = 1 })).Should().Contain("OPEX v1 2026").And.Contain("1200");
        (await RenderWith("B16", new() { ["BudgetLabel"] = "WORKS" })).Should().NotContain("WORKS v");
        (await RenderWith("B31", new() { ["Level"] = "Account", ["Basis"] = "YTD" })).Should().Contain("6000,").And.Contain("360.00,450.00,0.00,-90.00,125.0%,3");
        (await RenderWith("B31", new() { ["Level"] = "Account", ["Basis"] = "FullYear" })).Should().Contain("\"1,440.00\",450.00,0.00,990.00,31.3%,3");
        (await RenderWith("B31", new() { ["Level"] = "Account", ["Basis"] = "Period" })).Should().Contain("120.00,150.00,0.00,-30.00,125.0%,1");
        (await RenderWith("B31", new() { ["Level"] = "Department", ["Basis"] = "YTD" })).Should().Contain("All departments,YTD,");

        Budget revenue = new(companyId, yearId, "REVENUE", BudgetType.OriginalApproved, null, 1);
        for (int period = 1; period <= 12; period++)
        {
            revenue.AddLine("4000", period, 200m);
        }

        revenue.Approve();
        revenue.Activate();
        One4All.Core.Trading.Customer payer = new(companyId, "BG-PAYER", "Revenue Payer", Scenario.Usd);
        db.AddRange(revenue, payer);
        await db.SaveChangesAsync();
        One4All.Application.Abstractions.Trading.ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Trading.ISalesLedgerService>();
        long invoice = (await ar.PostInvoiceAsync(new One4All.Application.Abstractions.Trading.SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = payer.Id, DocumentNumber = "BG-INV", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 400m, SalesAccountCode = "4000",
        })).Value.OpenItemId;
        (await ar.PostReceiptAsync(new One4All.Application.Abstractions.Trading.ReceiptRequest { CompanyId = companyId, CustomerId = payer.Id, DocumentNumber = "BG-REC", ReceiptDate = new DateOnly(2026, 2, 20), Currency = Scenario.Usd, Amount = 100m, BankAccountCode = "1100" }))
            .IsSuccess.Should().BeTrue();
        long receipt = await db.OpenItems.Where(i => i.CompanyId == companyId && i.DocumentNumber == "BG-REC").Select(i => i.Id).SingleAsync();
        (await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Trading.IAllocationService>()
            .AllocateAsync(companyId, One4All.Core.Trading.TradingLedger.AccountsReceivable, invoice, receipt, 100m, new DateOnly(2026, 2, 20))).IsSuccess.Should().BeTrue();
        (await Render("B43")).Should().Contain("4000,").And.Contain("600.00,400.00,66.7%,100.00,25.0%");

        foreach (string code in BudgetingReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
