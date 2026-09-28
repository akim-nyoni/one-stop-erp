using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// End-to-end finance charges: interest is raised on an overdue customer balance (posting a finance-charge
/// invoice that ages like any other), while a customer still within grace is not charged, and the books
/// balance.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FinanceChargeAccountingTests
{
    private readonly AccountingFixture _fixture;

    public FinanceChargeAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Overdue_balances_are_charged_interest_and_grace_is_respected()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long overdueCustomerId;
        long recentCustomerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            Customer overdueCustomer = new(companyId, "C-OVER", "Overdue Co", Scenario.Usd) { PaymentTermsDays = 0 };
            Customer recentCustomer = new(companyId, "C-RECENT", "Recent Co", Scenario.Usd) { PaymentTermsDays = 0 };
            db.Customers.Add(overdueCustomer);
            db.Customers.Add(recentCustomer);
            await db.SaveChangesAsync();
            overdueCustomerId = overdueCustomer.Id;
            recentCustomerId = recentCustomer.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            // Overdue: due 2026-01-01, 90 days before the run.
            await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = overdueCustomerId, DocumentNumber = "INV-OVER",
                DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd,
                NetAmount = 1000m, TaxAmount = 0m, SalesAccountCode = "4000",
            });

            // Recent: due 2026-03-25, only 7 days before the run (inside a 10-day grace).
            await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = recentCustomerId, DocumentNumber = "INV-RECENT",
                DocumentDate = new DateOnly(2026, 3, 25), Currency = Scenario.Usd,
                NetAmount = 500m, TaxAmount = 0m, SalesAccountCode = "4000",
            });
        }

        FinanceChargeRunResult run;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFinanceChargeService charges = scope.ServiceProvider.GetRequiredService<IFinanceChargeService>();
            var result = await charges.RaiseChargesAsync(new FinanceChargeRunRequest
            {
                CompanyId = companyId,
                AsAtDate = new DateOnly(2026, 4, 1),
                AnnualRatePercent = 12m,
                GraceDays = 10,
                MinimumCharge = 0m,
                InterestIncomeAccountCode = "4800",
            });

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            run = result.Value;
        }

        // Only the overdue customer is charged: 1000 @ 12% for 90 days = 29.59.
        run.Charges.Should().ContainSingle();
        CustomerFinanceCharge charge = run.Charges.Single();
        charge.CustomerId.Should().Be(overdueCustomerId);
        charge.Interest.Should().Be(29.59m);
        charge.MaxDaysOverdue.Should().Be(90);
        charge.OverdueAmount.Should().Be(1000m);
        run.TotalInterest.Should().Be(29.59m);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            // The finance charge posted a new AR open item for the overdue customer.
            OpenItem fc = await db.Set<OpenItem>().SingleAsync(o => o.CompanyId == companyId && o.DocumentNumber == $"FC-{overdueCustomerId}-20260401");
            fc.OutstandingBase.Should().Be(29.59m);
            fc.Sign.Should().Be(LedgerSign.Debit);

            // The recent customer has no finance charge.
            (await db.Set<OpenItem>().AnyAsync(o => o.CompanyId == companyId && o.TraderId == recentCustomerId && o.DocumentNumber.StartsWith("FC-"))).Should().BeFalse();

            // The books still balance.
            decimal debit = await db.Journals.Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).SumAsync(l => l.BaseDebit);
            decimal credit = await db.Journals.Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).SumAsync(l => l.BaseCredit);
            debit.Should().Be(credit);
        }
    }
}
