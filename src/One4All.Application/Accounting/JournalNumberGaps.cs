using System.Globalization;

namespace One4All.Application.Accounting;

/// <summary>A run of journal numbers missing from a year's sequence.</summary>
/// <param name="Year">The numbering year.</param>
/// <param name="From">The first missing number.</param>
/// <param name="To">The last missing number (equal to <paramref name="From"/> for a single gap).</param>
public readonly record struct NumberGap(int Year, long From, long To)
{
    /// <summary>How many numbers are missing.</summary>
    public long Count => To - From + 1;
}

/// <summary>
/// Finds gaps in yearly journal numbering (<c>JNL-yyyy-nnnnnn</c>). Each year's numbers should run 1, 2, 3 …
/// with none missing; a gap means a numbered journal was removed (e.g. a deferred journal deleted) and is
/// worth an auditor's attention. Pure, so it is unit-tested; the integrity check supplies the numbers.
/// </summary>
public static class JournalNumberGaps
{
    /// <summary>The prefix the posting engine gives journal numbers.</summary>
    public const string JournalPrefix = "JNL-";

    /// <summary>Finds the missing runs in each year's numbering. Numbers not in the yearly format are ignored.</summary>
    /// <param name="numbers">The journal numbers issued.</param>
    /// <param name="prefix">The number prefix.</param>
    /// <returns>The gaps, by year then number.</returns>
    public static IReadOnlyList<NumberGap> Find(IEnumerable<string> numbers, string prefix = JournalPrefix)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        Dictionary<int, SortedSet<long>> byYear = [];
        foreach (string number in numbers)
        {
            if (TryParse(number, prefix, out int year, out long value))
            {
                if (!byYear.TryGetValue(year, out SortedSet<long>? set))
                {
                    byYear[year] = set = [];
                }

                set.Add(value);
            }
        }

        List<NumberGap> gaps = [];
        foreach (int year in byYear.Keys.Order())
        {
            long expected = 1;
            foreach (long value in byYear[year])
            {
                if (value > expected)
                {
                    gaps.Add(new NumberGap(year, expected, value - 1));
                }

                expected = value + 1;
            }
        }

        return gaps;
    }

    /// <summary>Formats a gap as a readable range, e.g. <c>JNL-2026-000004 to JNL-2026-000006</c>.</summary>
    /// <param name="gap">The gap.</param>
    /// <param name="prefix">The number prefix.</param>
    /// <param name="padding">The digit padding.</param>
    /// <returns>The description.</returns>
    public static string Describe(NumberGap gap, string prefix = JournalPrefix, int padding = 6)
    {
        string first = Format(gap.Year, gap.From, prefix, padding);
        return gap.Count == 1 ? first : $"{first} to {Format(gap.Year, gap.To, prefix, padding)}";
    }

    private static string Format(int year, long value, string prefix, int padding)
        => $"{prefix}{year.ToString(CultureInfo.InvariantCulture)}-{value.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0')}";

    private static bool TryParse(string number, string prefix, out int year, out long value)
    {
        year = 0;
        value = 0;
        if (string.IsNullOrEmpty(number) || !number.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> rest = number.AsSpan(prefix.Length);
        int dash = rest.IndexOf('-');
        return dash == 4
            && int.TryParse(rest[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && long.TryParse(rest[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }
}
