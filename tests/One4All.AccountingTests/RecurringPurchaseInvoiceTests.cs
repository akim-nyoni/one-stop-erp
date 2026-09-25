using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RecurringPurchaseInvoiceTests
{
    private readonly AccountingFixture _fixture;

    public RecurringPurchaseInvoiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Billing_run_generates_and_catches_up_monthly_supplier_invoices()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long templateId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "RENT-SUP", "Landlord", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;

            IRecurringPurchaseInvoiceService svc = scope.ServiceProvider.GetRequiredService<IRecurringPurchaseInvoiceService>();
            var create = await svc.CreateAsync(companyId, supplierId, "RENT", Scenario.Usd, 200m, 0m, "5000", null, 1, new DateOnly(2026, 2, 1), "Monthly rent");
            create.IsSuccess.Should().BeTrue(create.IsFailure ? create.Error.ToString() : null);
            templateId = create.Value;

            // Guard rails.
            (await svc.CreateAsync(companyId, supplierId, "BAD", Scenario.Usd, 0m, 0m, "5000", null, 1, new DateOnly(2026, 2, 1), null)).IsFailure.Should().BeTrue();
            (await svc.CreateAsync(companyId, supplierId, "BAD2", Scenario.Usd, 10m, 0m, "5000", null, 0, new DateOnly(2026, 2, 1), null)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRecurringPurchaseInvoiceService svc = scope.ServiceProvider.GetRequiredService<IRecurringPurchaseInvoiceService>();
            var run = await svc.GenerateDueAsync(companyId, new DateOnly(2026, 4, 15));
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.Should().Be(3); // Feb/Mar/Apr
            (await svc.GenerateDueAsync(companyId, new DateOnly(2026, 4, 15))).Value.Should().Be(0);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            RecurringPurchaseInvoice template = await db.Set<RecurringPurchaseInvoice>().FirstAsync(t => t.Id == templateId);
            template.NextRunDate.Should().Be(new DateOnly(2026, 5, 1));

            List<OpenItem> invoices = await db.OpenItems
                .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable
                    && i.TraderId == template.SupplierId && i.ItemType == OpenItemType.Invoice)
                .ToListAsync();
            invoices.Should().HaveCount(3);
            invoices.Sum(i => i.OriginalBase).Should().Be(600m);

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport creditors = await reports.AgedCreditorsAsync(companyId, new DateOnly(2026, 4, 30));
            creditors.Rows.Should().Contain(r => r.TraderCode == "RENT-SUP" && r.Total == 600m);
        }
    }
}
