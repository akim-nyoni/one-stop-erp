namespace One4All.Application.Accounting;

/// <summary>
/// Working-capital efficiency ratios. Days-outstanding expresses a balance as the number of days of flow it
/// represents — receivables against credit sales (DSO) or payables against credit purchases (DPO). Pure, so
/// the arithmetic is unit-tested; the service supplies the balances and flows from the ledger.
/// </summary>
public static class WorkingCapital
{
    /// <summary>
    /// Days a balance represents at the given flow: <c>balance / flow × days</c>. Returns zero when there is
    /// no flow or no period, so an idle account does not produce an infinite ratio.
    /// </summary>
    /// <param name="balance">The outstanding balance (receivables or payables).</param>
    /// <param name="flow">The flow over the period (credit sales or credit purchases).</param>
    /// <param name="days">The number of days in the period.</param>
    /// <returns>The days outstanding, rounded to one decimal.</returns>
    public static decimal DaysOutstanding(decimal balance, decimal flow, int days)
    {
        if (flow <= 0m || days <= 0)
        {
            return 0m;
        }

        return Math.Round(balance / flow * days, 1, MidpointRounding.AwayFromZero);
    }
}
