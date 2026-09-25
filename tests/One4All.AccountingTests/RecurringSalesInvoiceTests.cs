using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RecurringSalesInvoiceTests
{
    private readonly AccountingFixture _fixture;

    public RecurringSalesInvoiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Billing_run_generates_and_catches_up_monthly_invoices()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long templateId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "SUB-1", "Subscriber", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            IRecurringSalesInvoiceService svc = scope.ServiceProvider.GetRequiredService<IRecurringSalesInvoiceService>();
            var create = await svc.CreateAsync(companyId, customerId, "SUB", Scenario.Usd, 100m, 0m, "4000", null, 1, new DateOnly(2026, 2, 1), "Monthly subscription");
            create.IsSuccess.Should().BeTrue(create.IsFailure ? create.Error.ToString() : null);
            templateId = create.Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRecurringSalesInvoiceService svc = scope.ServiceProvider.GetRequiredService<IRecurringSalesInvoiceService>();
            // As at 2026-04-15, Feb/Mar/Apr are due → three invoices generated (catch-up).
            var run = await svc.GenerateDueAsync(companyId, new DateOnly(2026, 4, 15));
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.Should().Be(3);

            // Running again the same period generates nothing (next run is now May).
            (await svc.GenerateDueAsync(companyId, new DateOnly(2026, 4, 15))).Value.Should().Be(0);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            RecurringSalesInvoice template = await db.Set<RecurringSalesInvoice>().FirstAsync(t => t.Id == templateId);
            template.NextRunDate.Should().Be(new DateOnly(2026, 5, 1));

            // Three AR invoices posted for the subscriber.
            List<OpenItem> invoices = await db.OpenItems
                .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable
                    && i.TraderId == template.CustomerId && i.ItemType == OpenItemType.Invoice)
                .ToListAsync();
            invoices.Should().HaveCount(3);
            invoices.Sum(i => i.OriginalBase).Should().Be(300m);

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 4, 30));
            debtors.Rows.Should().Contain(r => r.TraderCode == "SUB-1" && r.Total == 300m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRecurringSalesInvoiceService svc = scope.ServiceProvider.GetRequiredService<IRecurringSalesInvoiceService>();
            // Suspend then run: nothing further is billed.
            (await svc.SetActiveAsync(companyId, templateId, false)).IsSuccess.Should().BeTrue();
            (await svc.GenerateDueAsync(companyId, new DateOnly(2026, 8, 1))).Value.Should().Be(0);
        }
    }
}
