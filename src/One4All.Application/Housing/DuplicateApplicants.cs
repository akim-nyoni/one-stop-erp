using System.Globalization;
using System.Text;

namespace One4All.Application.Housing;

/// <summary>A person found on the waiting list, as a stand holder or on the property register.</summary>
/// <param name="Source">Where the record came from (e.g. "Waiting list").</param>
/// <param name="Reference">Its reference there (waiting-list number, stand, property).</param>
/// <param name="Name">The name as captured.</param>
/// <param name="IdNumber">The national ID number as captured, if any.</param>
/// <param name="DateOfBirth">The date of birth, if captured.</param>
public sealed record PersonRecord(string Source, string Reference, string Name, string? IdNumber, DateOnly? DateOfBirth, string? Phone = null);

/// <summary>Two records that look like the same person, or one person under two identities.</summary>
/// <param name="First">One record.</param>
/// <param name="Second">The other.</param>
/// <param name="Reason">Why they were matched.</param>
public sealed record DuplicateMatch(PersonRecord First, PersonRecord Second, string Reason);

/// <summary>
/// Duplicate and suspected fraudulent applicants (brief §14 H23): fuzzy matching on name, ID number and date of birth across
/// the waiting list, stand holders and the property register. ID numbers compare with punctuation and case removed; names
/// compare with case, punctuation and word order removed, and are "close" within two edits. A pair is reported when
/// <list type="bullet">
/// <item>one ID is registered twice on the waiting list;</item>
/// <item>one ID carries clearly different names;</item>
/// <item>close names share a date of birth under different IDs;</item>
/// <item>close names carry IDs one edit apart (a mistyped or altered ID).</item>
/// </list>
/// Records are bucketed by ID, date of birth and name, so the work stays near-linear. Pure and unit-tested.
/// </summary>
public static class DuplicateApplicants
{
    private const string WaitingList = "Waiting list";

    /// <summary>The source name for waiting-list records.</summary>
    public static string WaitingListSource => WaitingList;

    /// <summary>Finds the suspicious pairs.</summary>
    /// <param name="people">The records.</param>
    /// <returns>Each pair once, with the first reason that applies.</returns>
    public static IReadOnlyList<DuplicateMatch> Find(IReadOnlyList<PersonRecord> people)
    {
        Dictionary<(int, int), DuplicateMatch> found = [];
        void Consider(int i, int j)
        {
            if (i == j || found.ContainsKey((Math.Min(i, j), Math.Max(i, j))))
            {
                return;
            }

            (int a, int b) = (Math.Min(i, j), Math.Max(i, j));
            string? reason = Reason(people[a], people[b]);
            if (reason is not null)
            {
                found[(a, b)] = new DuplicateMatch(people[a], people[b], reason);
            }
        }

        foreach (IEnumerable<int> bucket in Buckets(people, p => p.IdNumber is null ? null : NormaliseId(p.IdNumber))
                     .Concat(Buckets(people, p => p.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                     .Concat(Buckets(people, p => NameKey(p.Name)))
                     .Concat(Buckets(people, p => PhoneKey(p.Phone))))
        {
            int[] members = [.. bucket];
            for (int x = 0; x < members.Length; x++)
            {
                for (int y = x + 1; y < members.Length; y++)
                {
                    Consider(members[x], members[y]);
                }
            }
        }

        return [.. found.OrderBy(f => f.Key.Item1).ThenBy(f => f.Key.Item2).Select(f => f.Value)];
    }

    /// <summary>Why two records look suspicious, or null.</summary>
    /// <param name="a">One record.</param>
    /// <param name="b">The other.</param>
    /// <returns>The reason, or null when they do not match.</returns>
    public static string? Reason(PersonRecord a, PersonRecord b)
    {
        if (a.Source == b.Source && a.Reference == b.Reference)
        {
            return null;
        }

        string? idA = a.IdNumber is null ? null : NormaliseId(a.IdNumber);
        string? idB = b.IdNumber is null ? null : NormaliseId(b.IdNumber);
        bool namesClose = Distance(NameKey(a.Name), NameKey(b.Name)) <= 2;
        if (idA is { Length: > 0 } && idA == idB)
        {
            return a.Source == WaitingList && b.Source == WaitingList ? "Same ID registered twice on the waiting list"
                : namesClose ? null : "Same ID number under different names";
        }

        if (PhoneKey(a.Phone) is { } phone && phone == PhoneKey(b.Phone) && idA != idB)
        {
            return "Same phone number under different ID numbers";
        }

        if (!namesClose)
        {
            return null;
        }

        if (a.DateOfBirth is not null && a.DateOfBirth == b.DateOfBirth)
        {
            return "Similar name and same date of birth under different ID numbers";
        }

        return idA is { Length: > 0 } && idB is { Length: > 0 } && Distance(idA, idB) == 1 ? "Similar name with ID numbers one character apart" : null;
    }

    /// <summary>An ID number with everything but letters and digits removed, upper-cased.</summary>
    /// <param name="id">The ID as captured.</param>
    /// <returns>The comparable form.</returns>
    public static string NormaliseId(string id) => new([.. id.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>A name upper-cased, punctuation removed and its words sorted, so "Moyo, John" matches "JOHN MOYO".</summary>
    /// <param name="name">The name as captured.</param>
    /// <returns>The comparable form.</returns>
    public static string NameKey(string name)
    {
        StringBuilder cleaned = new(name.Length);
        foreach (char c in name.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetter(c))
            {
                cleaned.Append(char.ToUpperInvariant(c));
            }
            else if (char.IsWhiteSpace(c) || c is ',' or '-' or '.')
            {
                cleaned.Append(' ');
            }
        }

        return string.Join(' ', cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
    }

    /// <summary>The Levenshtein edit distance between two strings.</summary>
    /// <param name="a">One string.</param>
    /// <param name="b">The other.</param>
    /// <returns>The fewest single-character insertions, deletions or substitutions turning one into the other.</returns>
    public static int Distance(string a, string b)
    {
        int[] previous = [.. Enumerable.Range(0, b.Length + 1)];
        int[] current = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static IEnumerable<IEnumerable<int>> Buckets(IReadOnlyList<PersonRecord> people, Func<PersonRecord, string?> key)
        => Enumerable.Range(0, people.Count).Select(i => (Index: i, Key: key(people[i]))).Where(x => !string.IsNullOrEmpty(x.Key))
            .GroupBy(x => x.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Select(x => x.Index));

    // A phone number's last nine digits (so +263 77 123 4567 and 077 123 4567 match), or null when too short to mean anything.
    private static string? PhoneKey(string? phone)
    {
        string digits = new([.. (phone ?? string.Empty).Where(char.IsDigit)]);
        return digits.Length < 7 ? null : digits.Length > 9 ? digits[^9..] : digits;
    }
}
