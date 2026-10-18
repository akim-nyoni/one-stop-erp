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
/// compare with case, punctuation and word order removed, and are "close" within one edit for a short name and two otherwise, when they
/// sound the same (Tendai / Tendayi), or when one gives an initial for a word the other spells out (T Dube / Tendai Dube). A pair is reported when
/// <list type="bullet">
/// <item>one ID is registered twice on the waiting list;</item>
/// <item>one ID carries clearly different names;</item>
/// <item>one phone number carries different IDs;</item>
/// <item>close names share a date of birth under different IDs;</item>
/// <item>close names carry dates of birth with the day and month swapped;</item>
/// <item>close names carry IDs one edit apart (a mistyped or altered ID).</item>
/// </list>
/// Records are bucketed by ID (and every ID with one character left out, so IDs one edit apart meet), date of birth (day and month in
/// either order), name, the name's sound and phone, so the work stays near-linear. Pure and unit-tested.
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
                     .Concat(Buckets(people, p => PhoneKey(p.Phone)))
                     .Concat(Buckets(people, p => SoundKey(p.Name)))
                     .Concat(Buckets(people, p => p.DateOfBirth is DateOnly d ? DayMonthKey(d) : null))
                     .Concat(MultiBuckets(people, p => p.IdNumber is null ? [] : OneLeftOut(NormaliseId(p.IdNumber)))))
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
        bool namesClose = NamesClose(a.Name, b.Name);
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

        if (a.DateOfBirth is DateOnly da && b.DateOfBirth is DateOnly db && da.Year == db.Year && da.Day == db.Month && da.Month == db.Day)
        {
            return "Similar name with the date of birth's day and month swapped";
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

    /// <summary>
    /// Whether two names are close: within one edit when the shorter is under eight letters and two otherwise, sounding the same word by word,
    /// or matching when one gives only an initial for a word the other spells out.
    /// </summary>
    /// <param name="a">One name.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when they may be the same person's name.</returns>
    public static bool NamesClose(string a, string b)
    {
        string ka = NameKey(a), kb = NameKey(b);
        if (ka.Length == 0 || kb.Length == 0)
        {
            return false;
        }

        int letters = Math.Min(ka.Count(char.IsLetter), kb.Count(char.IsLetter));
        if (Distance(ka, kb) <= (letters < 8 ? 1 : 2) || SoundKey(a) == SoundKey(b))
        {
            return true;
        }

        // Initials: every word of the one with fewer letters is either in the other or the initial of a word left in it.
        string[] wa = ka.Split(' '), wb = kb.Split(' ');
        (string[] shorter, string[] longer) = wa.Sum(w => w.Length) <= wb.Sum(w => w.Length) ? (wa, wb) : (wb, wa);
        if (shorter.Length != longer.Length || !shorter.Any(w => w.Length == 1) || shorter.All(w => w.Length == 1))
        {
            return false;
        }

        List<string> left = [.. longer];
        foreach (string word in shorter.OrderByDescending(w => w.Length))
        {
            string? hit = word.Length == 1 ? left.FirstOrDefault(w => w[0] == word[0]) : left.FirstOrDefault(w => w == word);
            if (hit is null)
            {
                return false;
            }

            left.Remove(hit);
        }

        return true;
    }

    /// <summary>A name's sound: each word coded the Soundex way (first letter, then consonant groups, vowels and H, W, Y dropped), sorted.</summary>
    /// <param name="name">The name as captured.</param>
    /// <returns>The sound key ("" when there is no name).</returns>
    public static string SoundKey(string name)
        => string.Join(' ', NameKey(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Sound).Order(StringComparer.Ordinal));

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

    // A word's sound code: PH as F, the first letter kept (a vowel as A), then B F P V=1, C G J K Q S X Z=2, D T=3, L=4, M N=5, R=6, repeats collapsed.
    private static string Sound(string word)
    {
        string w = word.Replace("PH", "F", StringComparison.Ordinal);
        if (w.Length == 0)
        {
            return string.Empty;
        }

        static char Code(char c) => c switch
        {
            'B' or 'F' or 'P' or 'V' => '1',
            'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
            'D' or 'T' => '3',
            'L' => '4',
            'M' or 'N' => '5',
            'R' => '6',
            _ => '0',
        };

        StringBuilder code = new();
        code.Append("AEIOU".Contains(w[0], StringComparison.Ordinal) ? 'A' : w[0]);
        char last = Code(w[0]);
        foreach (char c in w.Skip(1))
        {
            char digit = Code(c);
            if (digit != '0' && digit != last)
            {
                code.Append(digit);
            }

            if (c is not ('H' or 'W'))
            {
                last = digit;
            }
        }

        return code.ToString();
    }

    // A date of birth with its day and month in either order, so a swapped date meets the right one.
    private static string DayMonthKey(DateOnly date)
        => string.Create(CultureInfo.InvariantCulture, $"{date.Year}:{Math.Min(date.Day, date.Month)}:{Math.Max(date.Day, date.Month)}");

    // An ID with each character left out in turn: two IDs one edit apart share one of these.
    private static IEnumerable<string> OneLeftOut(string id) => id.Length < 4 ? [] : Enumerable.Range(0, id.Length).Select(i => "-" + id.Remove(i, 1)).Append("=" + id);

    // Buckets where a record may sit under several keys.
    private static IEnumerable<IEnumerable<int>> MultiBuckets(IReadOnlyList<PersonRecord> people, Func<PersonRecord, IEnumerable<string>> keys)
        => Enumerable.Range(0, people.Count).SelectMany(i => keys(people[i]).Distinct(StringComparer.Ordinal).Select(k => (Index: i, Key: k)))
            .GroupBy(x => x.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Select(x => x.Index).Distinct());

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
