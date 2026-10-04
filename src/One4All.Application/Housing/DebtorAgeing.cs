namespace One4All.Application.Housing;

/// <summary>
/// Housing debtor ageing (brief §14 H45 / H75: 30/60/90/120+ as at any date): an item not yet due is current; otherwise
/// it falls in 1–30, 31–60, 61–90, 91–120 or over 120 days past its due date. Pure and unit-tested.
/// </summary>
public static class DebtorAgeing
{
    /// <summary>The bands, in order.</summary>
    public static readonly IReadOnlyList<string> Bands = ["Current", "1-30", "31-60", "61-90", "91-120", "120+"];

    /// <summary>The band an item due on a date falls in, as at another.</summary>
    /// <param name="dueDate">When it fell due.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>The band's index in <see cref="Bands"/>.</returns>
    public static int Band(DateOnly dueDate, DateOnly asAt) => (asAt.DayNumber - dueDate.DayNumber) switch
    {
        <= 0 => 0,
        <= 30 => 1,
        <= 60 => 2,
        <= 90 => 3,
        <= 120 => 4,
        _ => 5,
    };

    /// <summary>
    /// Ages an instalment buyer: the sale is booked to the debtor in full when signed, so of what the buyer owes, the principal
    /// not yet billed is current and the rest is arrears. Payments clear the oldest instalments first, so the arrears sit in
    /// the latest raised instalments; any excess beyond them falls in the oldest instalment's band. An overpayment is current.
    /// </summary>
    /// <param name="owing">The buyer's net balance.</param>
    /// <param name="notYetBilled">The principal still to be billed (the balance after the last raised instalment).</param>
    /// <param name="raised">The raised instalments (due date, amount), in any order.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>The amount in each of <see cref="Bands"/>, summing to <paramref name="owing"/>.</returns>
    public static decimal[] AgeInstalments(decimal owing, decimal notYetBilled, IEnumerable<(DateOnly Due, decimal Amount)> raised, DateOnly asAt)
    {
        decimal[] bands = new decimal[Bands.Count];
        decimal arrears = owing - notYetBilled;
        bands[0] = notYetBilled;
        List<(DateOnly Due, decimal Amount)> newestFirst = [.. raised.OrderByDescending(r => r.Due)];
        if (arrears <= 0m || newestFirst.Count == 0)
        {
            bands[0] += arrears;
            return bands;
        }

        foreach ((DateOnly due, decimal amount) in newestFirst)
        {
            decimal take = Math.Min(amount, arrears);
            bands[Band(due, asAt)] += take;
            arrears -= take;
            if (arrears == 0m)
            {
                return bands;
            }
        }

        bands[Band(newestFirst[^1].Due, asAt)] += arrears;
        return bands;
    }

    /// <summary>How many of the raised instalments the arrears cover (the unpaid ones), newest first.</summary>
    /// <param name="arrears">Owing less the principal not yet billed.</param>
    /// <param name="raised">The raised instalment amounts, newest first.</param>
    /// <returns>The count of instalments wholly or partly unpaid.</returns>
    public static int UnpaidInstalments(decimal arrears, IEnumerable<decimal> raised)
    {
        int count = 0;
        foreach (decimal amount in raised)
        {
            if (arrears <= 0m)
            {
                break;
            }

            count++;
            arrears -= amount;
        }

        return count;
    }
}
