using FluentAssertions;
using One4All.Application.Procurement;
using Xunit;

namespace One4All.UnitTests.Procurement;

/// <summary>Covers matching receipts to a delivery schedule.</summary>
public sealed class DeliveryScheduleTests
{
    private static readonly DateOnly June5 = new(2026, 6, 5), June12 = new(2026, 6, 12), June1 = new(2026, 6, 1);

    [Fact]
    public void Receipts_fill_the_earliest_deliveries_first()
        => DeliverySchedule.Match(200m, 150m, [(June12, 100m), (June5, 100m)], June1).Should().Equal(new DeliverySlot(June5, 100m, 100m), new DeliverySlot(June12, 100m, 50m));

    [Fact]
    public void Unscheduled_line_is_due_on_the_fallback_date()
        => DeliverySchedule.Match(40m, 0m, [], June1).Should().Equal(new DeliverySlot(June1, 40m, 0m));

    [Fact]
    public void Amendments_extend_the_last_delivery_or_trim_the_latest()
    {
        DeliverySchedule.Match(250m, 0m, [(June5, 100m), (June12, 100m)], June1).Last().Quantity.Should().Be(150m);
        DeliverySchedule.Match(80m, 0m, [(June5, 100m), (June12, 100m)], June1).Should().Equal(new DeliverySlot(June5, 80m, 0m));
    }
}
