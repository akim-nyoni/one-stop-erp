using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// End-to-end working-capital metrics: DSO and DPO computed from a real AR invoice and AP invoice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkingCapitalAccountingTests
{
    private readonly AccountingFixture _fixture;

    public WorkingCapitalAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Dso_and_dpo_are_computed_from_receivables_and_payables()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "CUST1", "Acme", Scenario.Usd);
            Supplier supplier = new(companyId, "SUP1", "Widgets", Scenario.Usd);
            db.Customers.Add(customer);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            supplierId = supplier.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "SI-1", DocumentDate = new DateOnly(2026, 1, 15), Currency = Scenario.Usd, NetAmount = 1000m, TaxAmount = 0m, SalesAccountCode = "4000" });

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "PI-1", DocumentDate = new DateOnly(2026, 1, 15), Currency = Scenario.Usd, NetAmount = 600m, TaxAmount = 0m, ExpenseAccountCode = "6800" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IWorkingCapitalService wc = scope.ServiceProvider.GetRequiredService<IWorkingCapitalService>();
            var result = await wc.ComputeAsync(companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)); // 31 days

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            WorkingCapitalMetrics m = result.Value;

            m.ReceivablesBalance.Should().Be(1000m);
            m.CreditSales.Should().Be(1000m);
            m.DaysSalesOutstanding.Should().Be(31.0m); // 1000 / 1000 × 31

            m.PayablesBalance.Should().Be(600m);
            m.CreditPurchases.Should().Be(600m);
            m.DaysPayableOutstanding.Should().Be(31.0m); // 600 / 600 × 31
        }
    }
}
