namespace One4All.Application.Municipal;

/// <summary>What a property must pay before a rates clearance certificate can be issued.</summary>
/// <param name="Balance">The ratepayer's balance now (negative when in credit).</param>
/// <param name="AverageMonthlyBilling">The average monthly billing over the look-back months.</param>
/// <param name="MonthsInAdvance">The months of charges that must be paid in advance.</param>
/// <param name="AdvanceRequired">Those months' charges.</param>
public sealed record ClearanceQuote(decimal Balance, decimal AverageMonthlyBilling, int MonthsInAdvance, decimal AdvanceRequired)
{
    /// <summary>What is still to pay: the balance plus the advance, less any credit already held.</summary>
    public decimal AmountDue => Math.Max(0m, Balance + AdvanceRequired);

    /// <summary>Whether a certificate can be issued now.</summary>
    public bool CanIssue => AmountDue == 0m;
}

/// <summary>
/// Rates clearance arithmetic for property transfers (brief §4.9): the property's arrears plus a number of months of
/// charges in advance, estimated from what it has been billed on average, must be paid before clearance. Pure and
/// unit-tested.
/// </summary>
public static class RatesClearance
{
    /// <summary>The usual look-back for the monthly average.</summary>
    public const int LookBackMonths = 3;

    /// <summary>Quotes what clearance needs.</summary>
    /// <param name="balance">The ratepayer's balance (negative when in credit).</param>
    /// <param name="billedInLookBack">What was billed over the look-back months.</param>
    /// <param name="lookBackMonths">How many months that covers.</param>
    /// <param name="monthsInAdvance">The months of charges required in advance.</param>
    /// <returns>The quote.</returns>
    public static ClearanceQuote Quote(decimal balance, decimal billedInLookBack, int lookBackMonths, int monthsInAdvance)
    {
        decimal average = lookBackMonths <= 0 ? 0m : decimal.Round(billedInLookBack / lookBackMonths, 2, MidpointRounding.AwayFromZero);
        return new ClearanceQuote(balance, average, monthsInAdvance, decimal.Round(average * Math.Max(0, monthsInAdvance), 2, MidpointRounding.AwayFromZero));
    }
}
