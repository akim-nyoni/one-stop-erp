using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class RentalArithmeticTests
{
    [Fact]
    public void A_part_month_is_billed_pro_rata()
    {
        RentalArithmetic.RentFor(300m, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 16), null).Should().Be(154.84m);   // 16 of 31 days
        RentalArithmetic.RentFor(300m, new DateOnly(2026, 5, 1), new DateOnly(2026, 3, 16), new DateOnly(2026, 5, 15)).Should().Be(145.16m); // 15 of 31
        RentalArithmetic.RentFor(300m, new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 16), null).Should().Be(300m);
        RentalArithmetic.RentFor(300m, new DateOnly(2026, 6, 1), new DateOnly(2026, 3, 16), new DateOnly(2026, 5, 15)).Should().Be(0m);
    }

    [Theory]
    [InlineData(400, 300, 300, 100)]
    [InlineData(400, 500, 400, 0)]
    [InlineData(400, -50, 0, 400)]
    public void The_deposit_covers_arrears_first_and_the_rest_is_refunded(int deposit, int owed, int applied, int refund)
    {
        RentalArithmetic.Settle(deposit, owed).Should().Be(new DepositSettlement(applied, refund));
    }

    [Fact]
    public void Void_days_count_both_ends_and_rent_lost_follows_the_annual_rent()
    {
        RentalArithmetic.DaysVoid(new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 31)).Should().Be(16);
        RentalArithmetic.DaysVoid(new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 31)).Should().Be(0);
        RentalArithmetic.RentLost(300m, 16).Should().Be(157.81m);
        RentalArithmetic.RentLost(200m, 151).Should().Be(992.88m);
    }
}
