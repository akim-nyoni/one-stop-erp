using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Banking;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;
using ISalesLedgerService = One4All.Application.Abstractions.Trading.ISalesLedgerService;
using SalesInvoiceRequest = One4All.Application.Abstractions.Trading.SalesInvoiceRequest;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the DB-numbered dashboards (brief §14 Q): sales in two months, an expense and a customer invoice on the day; the
/// executive dashboard, daily flash, KPI scorecard and trend read them; every DB report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DashboardReportsTests
{
    private readonly AccountingFixture _fixture;

    public DashboardReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Dashboards_reflect_the_ledgers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.BankAccounts.Add(new BankAccount(companyId, "DBMAIN", "Main Bank", Scenario.Usd, "1100"));
        Customer customer = new(companyId, "DB-CUS", "Dashboard Customer", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Post(string document, DateOnly date, string debit, string credit, decimal amount) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = document, Narrative = document,
            Lines = [new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount }],
        })).IsSuccess.Should().BeTrue();
        await Post("DB-FEB", new DateOnly(2026, 2, 10), "1100", "4000", 800m);
        await Post("DB-MAR", new DateOnly(2026, 3, 10), "1100", "4000", 1000m);
        await Post("DB-EXP", new DateOnly(2026, 3, 12), "6000", "1100", 300m);
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "DB-INV", DocumentDate = new DateOnly(2026, 3, 20), Currency = Scenario.Usd, NetAmount = 200m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 3, 20), ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string executive = await Render("DB251");
        executive.Should().Contain("Revenue 2026 to date,\"2,000.00\"").And.Contain("Net profit to date,\"1,700.00\"").And.Contain("Cash & bank,\"1,500.00\"").And.Contain("Debtors,200.00");
        (await Render("DB252")).Should().Contain("\"Sales (invoices + POS, net)\",200.00,0.00,200.00");
        (await Render("DB253")).Should().Contain("Revenue,\"1,200.00\",800.00,▲ better");
        (await Render("DB255")).Should().Contain("2026-02,800.00,0.00,0.00,—,800").And.Contain("2026-03,\"1,200.00\",300.00,0.00,—,900");

        foreach (string code in DashboardReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
