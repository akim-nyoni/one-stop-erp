using System.Security.Cryptography;
using System.Text;

namespace One4All.Application.Security;

/// <summary>
/// Cashier cards (brief §4.1: login by PIN / card swipe / fingerprint): what a swipe or scan reads is reduced to the card number - magnetic-stripe
/// sentinels, separators and spaces dropped - and only its hash is stored. Pure and unit-tested.
/// </summary>
public static class CashierCards
{
    /// <summary>The card number a swipe or scan read, without stripe sentinels or spacing.</summary>
    /// <param name="read">What the reader typed.</param>
    /// <returns>The number, or empty when nothing usable was read.</returns>
    public static string Normalise(string? read)
    {
        string text = (read ?? string.Empty).Trim();
        if (text.StartsWith('%') || text.StartsWith(';'))
        {
            // Track 1 (%B1234^NAME^…?) or track 2 (;1234=…?): the number runs to the first field separator.
            text = text.TrimStart('%', ';', 'B', 'b');
            int end = text.IndexOfAny(['^', '=', '?']);
            text = end >= 0 ? text[..end] : text;
        }

        return new string([.. text.Where(char.IsLetterOrDigit)]).ToUpperInvariant();
    }

    /// <summary>The stored hash of a card (empty when the read had no card number).</summary>
    /// <param name="read">What the reader typed.</param>
    /// <returns>The hash, or empty.</returns>
    public static string Hash(string? read)
    {
        string number = Normalise(read);
        return number.Length < 4 ? string.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("one4all-cashier-card:" + number)));
    }
}
