using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers <see cref="FinanceCharge.InterestAfterCredits"/>: unallocated credits stop interest from their date.</summary>
public sealed class FinanceChargeCreditTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Feb1 = new(2026, 2, 1);
    private static readonly DateOnly Mar2 = new(2026, 3, 2);
    private static readonly DateOnly AsAt = new(2026, 4, 1);

    [Fact]
    public void Without_credits_the_whole_balance_accrues_to_the_run_date()
    {
        FinanceCharge.InterestAfterCredits([new ChargeableItem(Jan1, Jan1, 1_000m)], [], AsAt, 12m)
            .Should().Be(new CreditAdjustedInterest(29.59m, 1_000m)); // 90 days
    }

    [Fact]
    public void A_payment_covering_the_invoice_stops_interest_at_its_date()
    {
        FinanceCharge.InterestAfterCredits([new ChargeableItem(Jan1, Jan1, 1_000m)], [new UnallocatedCredit(Mar2, 1_000m)], AsAt, 12m)
            .Should().Be(new CreditAdjustedInterest(19.73m, 0m)); // 60 days
    }

    [Fact]
    public void A_credit_dated_before_the_charge_period_leaves_nothing_to_charge()
    {
        FinanceCharge.InterestAfterCredits([new ChargeableItem(Jan1, Jan1, 1_000m)], [new UnallocatedCredit(new DateOnly(2025, 12, 20), 1_000m)], AsAt, 12m)
            .Should().Be(new CreditAdjustedInterest(0m, 0m));
    }

    [Fact]
    public void A_part_payment_splits_the_invoice()
    {
        // 400 × 60 days = 7.89, plus 600 × 90 days = 17.75.
        FinanceCharge.InterestAfterCredits([new ChargeableItem(Jan1, Jan1, 1_000m)], [new UnallocatedCredit(Mar2, 400m)], AsAt, 12m)
            .Should().Be(new CreditAdjustedInterest(25.64m, 600m));
    }

    [Fact]
    public void A_credit_spills_from_the_oldest_invoice_to_the_next()
    {
        // A 300 covered (60 days) = 5.92; B 100 covered (29 days) = 0.95; B 400 uncovered (59 days) = 7.76.
        FinanceCharge.InterestAfterCredits(
                [new ChargeableItem(Feb1, Feb1, 500m), new ChargeableItem(Jan1, Jan1, 300m)],
                [new UnallocatedCredit(Mar2, 400m)],
                AsAt,
                12m)
            .Should().Be(new CreditAdjustedInterest(14.63m, 400m));
    }
}
