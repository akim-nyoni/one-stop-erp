using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the AP-numbered payables reports (brief §14 C): two suppliers invoiced, one overpaid into debit; a bank detail
/// change requested; the reports list, rank and flag them; every AP report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayablesReportsTests
{
    private readonly AccountingFixture _fixture;

    public PayablesReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payables_reports_reflect_the_ledger()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Supplier main = new(companyId, "AP-MAIN", "Main Supplies", Scenario.Usd);
        Supplier minor = new(companyId, "AP-MIN", "Minor Parts", Scenario.Usd);
        db.Suppliers.AddRange(main, minor);
        await db.SaveChangesAsync();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        async Task Invoice(Supplier supplier, string number, DateOnly date, decimal amount)
            => (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = amount, ExpenseAccountCode = "6100", Narrative = "Supplies",
            })).IsSuccess.Should().BeTrue();
        await Invoice(main, "SI-AP-1", new DateOnly(2026, 2, 1), 900m);
        await Invoice(minor, "SI-AP-2", new DateOnly(2026, 3, 1), 100m);
        (await ap.PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = minor.Id, DocumentNumber = "PMT-AP-1", PaymentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, Amount = 150m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        db.Set<SupplierBankChange>().Add(new SupplierBankChange(companyId, main.Id, "Main Supplies", "1234567890", "First Bank", 999_971));
        await db.SaveChangesAsync();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, Dictionary<string, object?>? extra = null)
        {
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 3, 31), ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            foreach (KeyValuePair<string, object?> pair in extra ?? [])
            {
                parameters[pair.Key] = pair.Value;
            }

            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("AP53", new() { ["SupplierCode"] = "AP-MIN" })).Should().Contain("SI-AP-2").And.Contain("PMT-AP-1").And.Contain("-50.00");
        (await Render("AP54")).Should().Contain("SI-AP-1").And.Contain("SI-AP-2").And.NotContain("PMT-AP-1");
        (await Render("AP58")).Should().Contain("2026-03-10,PMT-AP-1,Minor Parts,USD,150.00,-,150");
        (await Render("AP64")).Should().Contain("Minor Parts,USD,Debit balance,Active,-50").And.Contain("Main Supplies,USD,,Active,900");
        (await Render("AP65")).Should().Contain("Main Supplies,user 999971,First Bank / Main Supplies / ******7890,Pending");
        (await Render("AP66")).Should().Contain("1,Main Supplies,1,90.0%,90.0%,900").And.Contain("2,Minor Parts,1,10.0%,100.0%,100");

        foreach (string code in PayablesReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
