using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;
using ISalesLedgerService = One4All.Application.Abstractions.Trading.ISalesLedgerService;
using ReceiptRequest = One4All.Application.Abstractions.Trading.ReceiptRequest;
using SalesInvoiceRequest = One4All.Application.Abstractions.Trading.SalesInvoiceRequest;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the AR-numbered receivables reports (brief §14 B): two customers invoiced, one paying on account; the reports
/// age, list, rank and measure them; every AR report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReceivablesReportsTests
{
    private readonly AccountingFixture _fixture;

    public ReceivablesReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Receivables_reports_reflect_the_ledger()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer big = new(companyId, "AR-BIG", "Big Buyer", Scenario.Usd);
        Customer small = new(companyId, "AR-SML", "Small Buyer", Scenario.Usd);
        db.Customers.AddRange(big, small);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        async Task Invoice(Customer customer, string number, DateOnly date, decimal amount)
            => (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000", Narrative = "Goods",
            })).IsSuccess.Should().BeTrue();
        await Invoice(big, "INV-AR-1", new DateOnly(2026, 1, 10), 1000m);
        await Invoice(small, "INV-AR-2", new DateOnly(2026, 4, 1), 250m);
        (await ar.PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = big.Id, DocumentNumber = "RC-AR-1", ReceiptDate = new DateOnly(2026, 4, 5), Currency = Scenario.Usd, Amount = 300m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, Dictionary<string, object?>? extra = null)
        {
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 4, 30), ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
            foreach (KeyValuePair<string, object?> pair in extra ?? [])
            {
                parameters[pair.Key] = pair.Value;
            }

            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("AR29")).Should().Contain("Big Buyer,INV-AR-1,Invoice,2026-01-10").And.Contain("Small Buyer,INV-AR-2,Invoice,2026-04-01");
        (await Render("AR31", new() { ["CustomerCode"] = "AR-BIG" })).Should().Contain("2026-01-10,Invoice,INV-AR-1").And.Contain("RC-AR-1").And.Contain("700.00");
        (await Render("AR33")).Should().Contain("INV-AR-1").And.Contain("INV-AR-2").And.NotContain("RC-AR-1");
        string top = await Render("AR35");
        top.Should().Contain("1,Big Buyer,1,80.0%,80.0%,1000").And.Contain("2,Small Buyer,1,20.0%,100.0%,250");
        (await Render("AR39")).Should().Contain("Big Buyer,RC-AR-1,Receipt,2026-04-05,25,300");
        (await Render("AR47")).Should().Contain("USD,2,");
        (await Render("AR48")).Should().Contain("New,Big Buyer,2026-01-10").And.Contain("New,Small Buyer,2026-04-01");

        foreach (string code in ReceivablesReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
