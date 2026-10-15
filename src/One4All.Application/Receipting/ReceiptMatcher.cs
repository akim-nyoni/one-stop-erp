using System.Text.RegularExpressions;
using One4All.Core.Receipting;

namespace One4All.Application.Receipting;

/// <summary>A matching rule as the matcher reads it.</summary>
/// <param name="Field">What it reads.</param>
/// <param name="Kind">How it compares.</param>
/// <param name="Pattern">The pattern.</param>
/// <param name="CustomerId">The account.</param>
/// <param name="Priority">Its order.</param>
public readonly record struct MatchRule(ReceiptMatchField Field, ReceiptMatchKind Kind, string Pattern, long CustomerId, int Priority);

/// <summary>How an imported payment was matched.</summary>
public enum MatchedBy
{
    /// <summary>Not matched: it goes to suspense.</summary>
    None = 0,

    /// <summary>A council rule.</summary>
    Rule = 1,

    /// <summary>The account code, written as a word of the reference.</summary>
    AccountCode = 2,

    /// <summary>A stand number in the reference (its ratepayer).</summary>
    StandNumber = 3,

    /// <summary>The account code somewhere in the reference.</summary>
    CodeInReference = 4,
}

/// <summary>The match: the account and how it was found.</summary>
/// <param name="CustomerId">The account, or null.</param>
/// <param name="By">How.</param>
public readonly record struct ReceiptMatch(long? CustomerId, MatchedBy By);

/// <summary>
/// Matches an imported payment to one account (brief §RCP-8). In order: the council's rules (lowest priority first; the first that fits decides); the account
/// code as a whole word of the reference, ignoring spaces, dashes and slashes inside it ("AC 00-12" is AC0012); a stand number likewise, which matches
/// its ratepayer; then an account code anywhere in the reference. Every step but the rules matches only when it identifies exactly one account —
/// anything ambiguous goes to suspense rather than to a guess.
/// </summary>
public static class ReceiptMatcher
{
    /// <summary>Matches a line.</summary>
    /// <param name="reference">The payment reference.</param>
    /// <param name="payerName">The payer's name.</param>
    /// <param name="rules">The council's rules.</param>
    /// <param name="accounts">Each account's code.</param>
    /// <param name="stands">Each stand number's ratepayer.</param>
    /// <returns>The match.</returns>
    public static ReceiptMatch Match(string reference, string payerName, IEnumerable<MatchRule> rules, IReadOnlyDictionary<string, long> accounts,
        IReadOnlyDictionary<string, long> stands)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(stands);
        reference ??= string.Empty;
        payerName ??= string.Empty;
        foreach (MatchRule rule in rules.OrderBy(r => r.Priority))
        {
            if (Fits(rule, rule.Field == ReceiptMatchField.Reference ? reference : payerName))
            {
                return new ReceiptMatch(rule.CustomerId, MatchedBy.Rule);
            }
        }

        // The reference's words, and runs of neighbouring words joined ("AC 0012" → AC0012), closed up.
        HashSet<string> words = Words(reference);
        if (Single(accounts.Where(a => words.Contains(Squash(a.Key))).Select(a => a.Value)) is long byCode)
        {
            return new ReceiptMatch(byCode, MatchedBy.AccountCode);
        }

        if (Single(stands.Where(s => words.Contains(Squash(s.Key))).Select(s => s.Value)) is long byStand)
        {
            return new ReceiptMatch(byStand, MatchedBy.StandNumber);
        }

        string squashed = Squash(reference);
        return Single(accounts.Where(a => Squash(a.Key).Length > 0 && squashed.Contains(Squash(a.Key), StringComparison.Ordinal)).Select(a => a.Value)) is long contained
            ? new ReceiptMatch(contained, MatchedBy.CodeInReference)
            : new ReceiptMatch(null, MatchedBy.None);
    }

    /// <summary>Whether a rule's pattern is usable.</summary>
    /// <param name="kind">How it compares.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>Null when it is; otherwise why not.</returns>
    public static string? Problem(ReceiptMatchKind kind, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || (kind != ReceiptMatchKind.Pattern && Squash(pattern).Length == 0))
        {
            return "A rule needs a pattern.";
        }

        if (kind == ReceiptMatchKind.Pattern)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException ex)
            {
                return $"The pattern is not a valid regular expression: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>Upper case without spaces, dashes, slashes, dots or underscores.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The squashed text.</returns>
    public static string Squash(string text) => string.Concat((text ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static bool Fits(MatchRule rule, string text)
    {
        if (rule.Kind == ReceiptMatchKind.Pattern)
        {
            try
            {
                return Regex.IsMatch(text, rule.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        string have = Squash(text), want = Squash(rule.Pattern);
        return want.Length > 0 && rule.Kind switch
        {
            ReceiptMatchKind.StartsWith => have.StartsWith(want, StringComparison.Ordinal),
            ReceiptMatchKind.Equals => have == want,
            _ => have.Contains(want, StringComparison.Ordinal),
        };
    }

    private static HashSet<string> Words(string reference)
    {
        string[] parts = [.. reference.Split([' ', ',', ';', ':', '#', '(', ')', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(Squash).Where(p => p.Length > 0)];
        HashSet<string> words = [];
        for (int start = 0; start < parts.Length; start++)
        {
            string joined = string.Empty;
            for (int end = start; end < Math.Min(parts.Length, start + 3); end++)
            {
                joined += parts[end];
                words.Add(joined);
            }
        }

        return words;
    }

    private static long? Single(IEnumerable<long> ids)
    {
        List<long> distinct = [.. ids.Distinct().Take(2)];
        return distinct.Count == 1 ? distinct[0] : null;
    }
}
