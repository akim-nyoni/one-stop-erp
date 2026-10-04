using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class DebtorAgeingTests
{
    private static readonly DateOnly AsAt = new(2026, 6, 30);

    [Theory]
    [InlineData(2026, 7, 15, "Current")]
    [InlineData(2026, 6, 30, "Current")]
    [InlineData(2026, 6, 29, "1-30")]
    [InlineData(2026, 5, 31, "1-30")]
    [InlineData(2026, 5, 30, "31-60")]
    [InlineData(2026, 4, 1, "61-90")]
    [InlineData(2026, 3, 2, "91-120")]
    [InlineData(2026, 3, 1, "120+")]
    public void Items_age_from_their_due_date(int year, int month, int day, string band)
    {
        DebtorAgeing.Bands[DebtorAgeing.Band(new DateOnly(year, month, day), AsAt)].Should().Be(band);
    }

    [Fact]
    public void Instalment_arrears_sit_in_the_latest_raised_instalments()
    {
        (DateOnly, decimal)[] raised = [(new DateOnly(2026, 4, 1), 100m), (new DateOnly(2026, 5, 1), 100m), (new DateOnly(2026, 6, 1), 100m)];

        // 1,000 not billed; 150 in arrears: June's 100 (1–30) and half of May's (31–60).
        DebtorAgeing.AgeInstalments(1150m, 1000m, raised, AsAt).Should().Equal(1000m, 100m, 50m, 0m, 0m, 0m);
        DebtorAgeing.UnpaidInstalments(150m, [100m, 100m, 100m]).Should().Be(2);

        // More than every raised instalment: the excess goes with the oldest.
        DebtorAgeing.AgeInstalments(1400m, 1000m, raised, AsAt).Should().Equal(1000m, 100m, 100m, 200m, 0m, 0m);

        // Paid ahead: the credit reduces current.
        DebtorAgeing.AgeInstalments(950m, 1000m, raised, AsAt).Should().Equal(950m, 0m, 0m, 0m, 0m, 0m);
        DebtorAgeing.UnpaidInstalments(-50m, [100m]).Should().Be(0);
    }
}
