using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers min/max replenishment and the moving-average forecast: at or below the minimum an item is ordered up to its maximum, and with a
/// forecast window the reorder point rises to the average daily issues (inside the window, reversed issues left out) times the lead time.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReplenishmentForecastTests
{
    private readonly AccountingFixture _fixture;

    public ReplenishmentForecastTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Min_max_orders_up_to_the_maximum_and_the_forecast_raises_the_reorder_point()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item forecast = new(companyId, "RF-FAST", "Fast mover") { CostOfSalesAccountCode = "5000" };
        Item minMax = new(companyId, "RF-MINMAX", "Min/max item") { CostOfSalesAccountCode = "5000" };
        Warehouse wh = new(companyId, "RFWH", "Main");
        db.Items.AddRange(forecast, minMax);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        IReplenishmentService replenish = scope.ServiceProvider.GetRequiredService<IReplenishmentService>();
        (await replenish.SetPolicyAsync(companyId, minMax.Id, new ReplenishmentPolicy(10m, 0m, 10m, 0, 0))).Error.Code.Should().Be("Replenish.MaxBelowMin");
        (await replenish.SetPolicyAsync(companyId, forecast.Id, new ReplenishmentPolicy(5m, 0m, 200m, 0, 30))).Error.Code.Should().Be("Replenish.LeadTime");
        (await replenish.SetPolicyAsync(companyId, minMax.Id, new ReplenishmentPolicy(10m, 25m, 50m, 0, 0))).IsSuccess.Should().BeTrue();
        (await replenish.SetPolicyAsync(companyId, forecast.Id, new ReplenishmentPolicy(5m, 0m, 200m, 10, 30))).IsSuccess.Should().BeTrue();

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = forecast.Id, WarehouseId = wh.Id, Date = today.AddDays(-45), Quantity = 135m, UnitCost = 1m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = minMax.Id, WarehouseId = wh.Id, Date = today.AddDays(-45), Quantity = 4m, UnitCost = 1m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();

        async Task<long> Issue(int daysAgo, decimal quantity)
        {
            var issued = await inv.IssueAsync(new IssueStockRequest
            {
                CompanyId = companyId, ItemId = forecast.Id, WarehouseId = wh.Id, Date = today.AddDays(-daysAgo), Quantity = quantity, ChargeAccountCode = "6100",
            });
            issued.IsSuccess.Should().BeTrue(issued.IsFailure ? issued.Error.Message : string.Empty);
            return issued.Value.MovementId;
        }

        await Issue(40, 20m);                       // before the 30-day window: not counted
        await Issue(10, 90m);                       // 90 in the window → 3 a day
        long mistake = await Issue(5, 15m);         // reversed: not usage
        (await inv.ReverseIssueAsync(companyId, mistake, today.AddDays(-5))).IsSuccess.Should().BeTrue();

        IReadOnlyList<ReplenishmentSuggestion> suggestions = await replenish.SuggestAsync(companyId);

        // 25 on hand; reorder point max(5, 3 × 10 days) = 30 → order up to 200.
        ReplenishmentSuggestion fast = suggestions.Single(s => s.ItemId == forecast.Id);
        (fast.OnHand, fast.AverageDailyUsage, fast.ReorderLevel, fast.SuggestedQuantity, fast.MaximumStock).Should().Be((25m, 3m, 30m, 175m, 200m));

        // 4 on hand, minimum 10 → up to the maximum 50 (the maximum wins over the fixed reorder quantity).
        suggestions.Single(s => s.ItemId == minMax.Id).SuggestedQuantity.Should().Be(46m);

        // In season (a factor of 2 for every month here) the forecast usage doubles: reorder point 6 × 10 = 60.
        (await replenish.SetPolicyAsync(companyId, forecast.Id, new ReplenishmentPolicy(5m, 0m, 200m, 10, 30) { SeasonalFactors = "1,2" })).Error.Code.Should().Be("Replenish.Seasonal");
        (await replenish.SetPolicyAsync(companyId, forecast.Id, new ReplenishmentPolicy(5m, 0m, 200m, 10, 30) { SeasonalFactors = "2,2,2,2,2,2,2,2,2,2,2,2" })).IsSuccess.Should().BeTrue();
        ReplenishmentSuggestion seasonal = (await replenish.SuggestAsync(companyId)).Single(s => s.ItemId == forecast.Id);
        (seasonal.AverageDailyUsage, seasonal.ReorderLevel).Should().Be((6m, 60m));
    }
}
