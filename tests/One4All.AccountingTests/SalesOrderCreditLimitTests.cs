using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers credit control when an order is taken (brief §4.5): the balance, the orders not yet invoiced and the new order against the limit —
/// refused under a blocking policy, taken and logged under a warning policy.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesOrderCreditLimitTests
{
    private readonly AccountingFixture _fixture;

    public SalesOrderCreditLimitTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_order_over_the_credit_limit_is_refused_or_logged_by_policy()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer blocked = new(companyId, "SCL-B", "Strict Ltd", Scenario.Usd) { CreditLimit = 1_000m, CreditLimitPolicy = CreditLimitPolicy.Block };
        Customer warned = new(companyId, "SCL-W", "Lenient Ltd", Scenario.Usd) { CreditLimit = 1_000m, CreditLimitPolicy = CreditLimitPolicy.Warn };
        Item service = new(companyId, "SCL-SVC", "Consulting") { RevenueAccountCode = "4000" };
        db.AddRange(blocked, warned, service);
        await db.SaveChangesAsync();

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        CreateSalesOrderRequest Order(Customer customer, string number, decimal value) => new()
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(service.Id, "Consulting", 1m, value, null, "4000")],
        };

        (await orders.CreateOrderAsync(Order(blocked, "SCL-1", 1_200m))).Error.Code.Should().Be("Sop.CreditLimitExceeded");
        (await orders.CreateOrderAsync(Order(blocked, "SCL-2", 600m))).IsSuccess.Should().BeTrue();
        (await orders.CreateOrderAsync(Order(blocked, "SCL-3", 500m))).Error.Code.Should().Be("Sop.CreditLimitExceeded", "600 is already on order");
        (await orders.CreateOrderAsync(Order(blocked, "SCL-4", 400m))).IsSuccess.Should().BeTrue();

        (await orders.CreateOrderAsync(Order(warned, "SCL-5", 1_500m))).IsSuccess.Should().BeTrue();
        (await db.Set<CreditLimitOverride>().AsNoTracking().Where(o => o.CompanyId == companyId).Select(o => o.DocumentNumber).ToListAsync()).Should().Equal("SCL-5");
    }
}
