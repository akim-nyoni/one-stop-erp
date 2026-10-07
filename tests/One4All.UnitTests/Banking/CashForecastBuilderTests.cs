using FluentAssertions;
using One4All.Application.Banking;
using Xunit;

namespace One4All.UnitTests.Banking;

/// <summary>Covers the weekly forecast: overdue flows land in the first week, flows past the horizon are left out, the minimum is flagged.</summary>
public sealed class CashForecastBuilderTests
{
    [Fact]
    public void Flows_are_bucketed_by_week_with_running_balances()
    {
        DateOnly from = new(2026, 4, 6);
        CashForecast f = CashForecastBuilder.Build(from, 3, 1_000m, 500m,
        [
            new CashFlowLine(new DateOnly(2026, 3, 1), CashFlowSource.Receivables, "OVERDUE", 300m),
            new CashFlowLine(new DateOnly(2026, 4, 8), CashFlowSource.Payables, "P1", -900m),
            new CashFlowLine(new DateOnly(2026, 4, 14), CashFlowSource.Payroll, "NET", -600m),
            new CashFlowLine(new DateOnly(2026, 4, 20), CashFlowSource.SalesOrders, "SO", 1_000m),
            new CashFlowLine(new DateOnly(2026, 4, 27), CashFlowSource.SalesOrders, "LATER", 5_000m),
        ]);

        f.Weeks.Select(w => (w.WeekStart, w.Inflows, w.Outflows, w.Closing, w.BelowMinimum)).Should().Equal(
            (from, 300m, 900m, 400m, true), (new DateOnly(2026, 4, 13), 0m, 600m, -200m, true), (new DateOnly(2026, 4, 20), 1_000m, 0m, 800m, false));
        f.Weeks[0].BySource.Should().Equal(new Dictionary<CashFlowSource, decimal> { [CashFlowSource.Receivables] = 300m, [CashFlowSource.Payables] = -900m });
        (f.FirstShortfall, f.LowestClosing).Should().Be((from, -200m));
        f.Lines.Should().NotContain(l => l.Reference == "LATER").And.Contain(l => l.Reference == "OVERDUE" && l.Date == from);
    }
}
