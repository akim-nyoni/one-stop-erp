namespace One4All.Application.Payroll;

/// <summary>Net pay broken into notes and coins: how many of each face value, largest first, and what is left that no coin covers.</summary>
/// <param name="Pieces">Each face value used and how many.</param>
/// <param name="Remainder">What is left over.</param>
public sealed record CashPieces(IReadOnlyDictionary<decimal, int> Pieces, decimal Remainder)
{
    /// <summary>The pieces as text: "3 × 100, 1 × 20, remainder 0.40".</summary>
    /// <returns>The text.</returns>
    public string Describe() => string.Join(", ", Pieces.Where(p => p.Value > 0).OrderByDescending(p => p.Key)
        .Select(p => $"{p.Value} × {p.Key.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}")
        .Concat(Remainder > 0m ? [$"remainder {Remainder.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"] : []));
}

/// <summary>
/// Cash pay by notes and coins (brief §4.7 PR12): each employee's net pay split into the fewest notes and coins, largest first, and the totals a pay point
/// needs drawn from the bank - the sum of its employees' pieces, so each envelope can be made up exactly.
/// </summary>
public static class CashPayBreakdown
{
    /// <summary>Splits an amount into notes and coins.</summary>
    /// <param name="amount">The net pay.</param>
    /// <param name="faceValues">The currency's notes and coins.</param>
    /// <returns>The pieces.</returns>
    public static CashPieces Split(decimal amount, IEnumerable<decimal> faceValues)
    {
        ArgumentNullException.ThrowIfNull(faceValues);
        Dictionary<decimal, int> pieces = [];
        decimal left = Math.Max(0m, amount);
        foreach (decimal face in faceValues.Where(f => f > 0m).Distinct().OrderByDescending(f => f))
        {
            int count = (int)Math.Floor(left / face);
            if (count > 0)
            {
                pieces[face] = count;
                left -= count * face;
            }
        }

        return new CashPieces(pieces, left);
    }

    /// <summary>Adds up several employees' pieces: what the pay point draws.</summary>
    /// <param name="splits">Each employee's pieces.</param>
    /// <returns>The totals.</returns>
    public static CashPieces Total(IEnumerable<CashPieces> splits)
    {
        ArgumentNullException.ThrowIfNull(splits);
        Dictionary<decimal, int> pieces = [];
        decimal remainder = 0m;
        foreach (CashPieces split in splits)
        {
            foreach ((decimal face, int count) in split.Pieces)
            {
                pieces[face] = pieces.GetValueOrDefault(face) + count;
            }

            remainder += split.Remainder;
        }

        return new CashPieces(pieces, remainder);
    }
}
