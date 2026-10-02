namespace One4All.Application.Banking;

/// <summary>A statement line offered for matching.</summary>
/// <param name="Id">The statement line id.</param>
/// <param name="Date">The statement date.</param>
/// <param name="Amount">The signed amount (+ in, − out).</param>
/// <param name="Reference">The bank's reference, if any.</param>
public sealed record MatchableStatementLine(long Id, DateOnly Date, decimal Amount, string? Reference);

/// <summary>A cash-book (GL) line offered for matching.</summary>
/// <param name="Id">The journal line id.</param>
/// <param name="Date">The posting date.</param>
/// <param name="Amount">The signed amount on the bank account (debit positive).</param>
/// <param name="DocumentNumber">The journal's document number, if any.</param>
/// <param name="Reference">The journal's reference, if any.</param>
public sealed record MatchableGlLine(long Id, DateOnly Date, decimal Amount, string? DocumentNumber, string? Reference);

/// <summary>A proposed match.</summary>
/// <param name="StatementLineId">The statement line.</param>
/// <param name="GlLineId">The cash-book line.</param>
/// <param name="DaysApart">The absolute difference between the two dates.</param>
/// <param name="ReferenceMatched">Whether the bank reference matched the journal's document number or reference.</param>
public sealed record BankMatch(long StatementLineId, long GlLineId, int DaysApart, bool ReferenceMatched);

/// <summary>
/// Matches bank statement lines to cash-book lines (brief §4.7). A candidate must have the same signed
/// amount and fall within the date tolerance; among candidates, one whose document number or reference
/// equals the bank reference wins, then the closest date, then the earliest posting. Each cash-book line
/// is used at most once and statement lines are processed in date order. Pure and unit-tested.
/// </summary>
public static class BankAutoMatcher
{
    /// <summary>The default number of days either side of the statement date a posting may fall.</summary>
    public const int DefaultDateToleranceDays = 7;

    /// <summary>Proposes matches.</summary>
    /// <param name="statementLines">The unreconciled statement lines.</param>
    /// <param name="glLines">The cash-book lines not yet matched.</param>
    /// <param name="dateToleranceDays">Days either side of the statement date a posting may fall (negative = unlimited).</param>
    /// <returns>The matches, in statement-date order.</returns>
    public static IReadOnlyList<BankMatch> Match(IReadOnlyList<MatchableStatementLine> statementLines, IReadOnlyList<MatchableGlLine> glLines, int dateToleranceDays = DefaultDateToleranceDays)
    {
        HashSet<long> used = [];
        List<BankMatch> matches = [];

        foreach (MatchableStatementLine line in statementLines.OrderBy(s => s.Date).ThenBy(s => s.Id))
        {
            string? reference = Normalise(line.Reference);
            BankMatch? best = null;

            foreach (MatchableGlLine gl in glLines)
            {
                if (used.Contains(gl.Id) || gl.Amount != line.Amount)
                {
                    continue;
                }

                int daysApart = Math.Abs(line.Date.DayNumber - gl.Date.DayNumber);
                if (dateToleranceDays >= 0 && daysApart > dateToleranceDays)
                {
                    continue;
                }

                bool referenceMatched = reference is not null
                    && (reference == Normalise(gl.DocumentNumber) || reference == Normalise(gl.Reference));

                BankMatch candidate = new(line.Id, gl.Id, daysApart, referenceMatched);
                if (best is null || Better(candidate, best, gl, glLines))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                used.Add(best.GlLineId);
                matches.Add(best);
            }
        }

        return matches;
    }

    private static bool Better(BankMatch candidate, BankMatch current, MatchableGlLine candidateLine, IReadOnlyList<MatchableGlLine> glLines)
    {
        if (candidate.ReferenceMatched != current.ReferenceMatched)
        {
            return candidate.ReferenceMatched;
        }

        if (candidate.DaysApart != current.DaysApart)
        {
            return candidate.DaysApart < current.DaysApart;
        }

        MatchableGlLine currentLine = glLines.First(g => g.Id == current.GlLineId);
        return candidateLine.Date < currentLine.Date || (candidateLine.Date == currentLine.Date && candidateLine.Id < currentLine.Id);
    }

    private static string? Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Trim().ToUpperInvariant();
    }
}
