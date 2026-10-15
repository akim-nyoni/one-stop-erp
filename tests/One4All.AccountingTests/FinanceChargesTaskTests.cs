using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// The scheduled finance-charge task charges interest on the company's saved policy, does nothing without one, and a re-run the same day charges nothing more.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FinanceChargesTaskTests
{
    private readonly AccountingFixture _fixture;

    public FinanceChargesTaskTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_monthly_task_charges_interest_on_the_saved_policy()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "FCT-1", "Late Payer", Scenario.Usd) { PaymentTermsDays = 0 };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "FCT-I1", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000",
            });
        }

        DateTimeOffset firstOfApril = new(2026, 4, 1, 6, 0, 0, TimeSpan.Zero);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IScheduledTask task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Key == "ar.finance-charges");
            (await task.RunAsync(companyId, firstOfApril)).Value.Should().Contain("No finance-charge policy");

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            db.Add(new FinanceChargePolicy(companyId, 12m, "4800") { GraceDays = 0, ExemptIndigent = false });
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IScheduledTask task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Key == "ar.finance-charges");
            (await task.RunAsync(companyId, firstOfApril)).Value.Should().StartWith("1 customer(s) charged 29.59 interest at 12%"); // 1,000 × 12% × 90/365
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IScheduledTask task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Key == "ar.finance-charges");
            (await task.RunAsync(companyId, firstOfApril)).Value.Should().StartWith("0 customer(s) charged");

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.OpenItems.CountAsync(o => o.CompanyId == companyId && o.TraderId == customerId)).Should().Be(2); // the invoice and one finance charge
        }
    }
}
