using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers credit control: the review places over-limit and overdue customers on stop (and leaves the
/// rest alone), a held customer is refused new sales orders and invoices, and release restores trading.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CreditControlTests
{
    private readonly AccountingFixture _fixture;

    public CreditControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Review_holds_over_limit_and_overdue_customers_and_blocks_orders_until_released()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long overLimitId;
        long overdueId;
        long goodId;
        long itemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            Customer overLimit = new(companyId, "CC-LIM", "Over Limit Ltd", Scenario.Usd) { CreditLimit = 1000m };
            Customer overdue = new(companyId, "CC-DUE", "Slow Payer Ltd", Scenario.Usd) { PaymentTermsDays = 30 };
            Customer good = new(companyId, "CC-OK", "Prompt Payer Ltd", Scenario.Usd) { CreditLimit = 5000m, PaymentTermsDays = 30 };
            db.Customers.AddRange(overLimit, overdue, good);
            Item item = new(companyId, "CC-WIDGET", "Widget") { RevenueAccountCode = "4000" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            overLimitId = overLimit.Id;
            overdueId = overdue.Id;
            goodId = good.Id;
            itemId = item.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            // Over-limit: two invoices totalling 900 pass the invoice-time check; the limit is then lowered to 500
            // (as a credit controller would), leaving the balance above the limit for the review to catch.
            (await ar.PostInvoiceAsync(Invoice(companyId, overLimitId, "CC-L1", new DateOnly(2026, 5, 1), 400m))).IsSuccess.Should().BeTrue();
            (await ar.PostInvoiceAsync(Invoice(companyId, overLimitId, "CC-L2", new DateOnly(2026, 5, 2), 500m))).IsSuccess.Should().BeTrue();
            overLimit.CreditLimit = 500m;

            // Overdue: due 2026-03-03, well past tolerance at the review date.
            (await ar.PostInvoiceAsync(Invoice(companyId, overdueId, "CC-D1", new DateOnly(2026, 2, 1), 250m))).IsSuccess.Should().BeTrue();

            // Good: within limit and due 2026-05-31 — not overdue at the review date.
            (await ar.PostInvoiceAsync(Invoice(companyId, goodId, "CC-G1", new DateOnly(2026, 5, 1), 1200m))).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ICreditControlService credit = scope.ServiceProvider.GetRequiredService<ICreditControlService>();
            var review = await credit.ReviewAsync(companyId, new DateOnly(2026, 6, 1), overdueToleranceDays: 14);
            review.IsSuccess.Should().BeTrue(review.IsFailure ? review.Error.ToString() : null);

            review.Value.Reviewed.Should().Be(3);
            review.Value.Held.Should().HaveCount(2);

            CreditHold lim = review.Value.Held.Single(h => h.CustomerId == overLimitId);
            lim.Reason.Should().Be(CreditHoldReason.OverLimit);
            lim.Balance.Should().Be(900m);
            lim.CreditLimit.Should().Be(500m);
            lim.OverdueAmount.Should().Be(0m);

            CreditHold due = review.Value.Held.Single(h => h.CustomerId == overdueId);
            due.Reason.Should().Be(CreditHoldReason.Overdue);
            due.OverdueAmount.Should().Be(250m);
            due.OldestOverdueDays.Should().Be(90); // due 2026-03-03 → 2026-06-01

            review.Value.Held.Should().NotContain(h => h.CustomerId == goodId);

            // Re-running the review skips customers already on hold.
            var again = await credit.ReviewAsync(companyId, new DateOnly(2026, 6, 1), 14);
            again.Value.Reviewed.Should().Be(1);
            again.Value.Held.Should().BeEmpty();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            // A held customer is refused at order entry and at invoicing; a clean one is not.
            var heldOrder = await sop.CreateOrderAsync(Order(companyId, overdueId, "CC-SO1", itemId));
            heldOrder.IsFailure.Should().BeTrue();
            heldOrder.Error.Code.Should().Be("Sop.CustomerOnHold");

            (await ar.PostInvoiceAsync(Invoice(companyId, overdueId, "CC-D2", new DateOnly(2026, 6, 2), 10m))).Error.Code.Should().Be("Ar.CustomerOnHold");
            (await sop.CreateOrderAsync(Order(companyId, goodId, "CC-SO2", itemId))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ICreditControlService credit = scope.ServiceProvider.GetRequiredService<ICreditControlService>();
            (await credit.ReleaseAsync(companyId, overdueId, " ")).IsFailure.Should().BeTrue(); // reason required
            (await credit.ReleaseAsync(companyId, goodId, "not held")).Error.Code.Should().Be("Credit.NotOnHold");

            var release = await credit.ReleaseAsync(companyId, overdueId, "Payment plan agreed");
            release.IsSuccess.Should().BeTrue(release.IsFailure ? release.Error.ToString() : null);

            // Hand hold: refuses a customer already on hold, holds a clean one.
            (await credit.HoldAsync(companyId, overLimitId, "dispute")).Error.Code.Should().Be("Credit.AlreadyOnHold");
            (await credit.HoldAsync(companyId, goodId, "Dispute over delivery")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            (await sop.CreateOrderAsync(Order(companyId, overdueId, "CC-SO3", itemId))).IsSuccess.Should().BeTrue(); // released
            (await sop.CreateOrderAsync(Order(companyId, goodId, "CC-SO4", itemId))).Error.Code.Should().Be("Sop.CustomerOnHold"); // hand-held
        }
    }

    private static SalesInvoiceRequest Invoice(long companyId, long customerId, string number, DateOnly date, decimal net) => new()
    {
        CompanyId = companyId, CustomerId = customerId, DocumentNumber = number, DocumentDate = date,
        Currency = Scenario.Usd, NetAmount = net, TaxAmount = 0m, SalesAccountCode = "4000",
    };

    private static CreateSalesOrderRequest Order(long companyId, long customerId, string number, long itemId) => new()
    {
        CompanyId = companyId, CustomerId = customerId, OrderNumber = number, OrderDate = new DateOnly(2026, 6, 2), Currency = Scenario.Usd,
        Lines = [new SalesOrderLineRequest(itemId, "Widget", 1m, 10m, null, "4000")],
    };
}
