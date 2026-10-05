using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace One4All.Application.Banking;

/// <summary>The file-level values a bank file template can use (header and trailer).</summary>
/// <param name="Date">The payment date.</param>
/// <param name="Originator">The paying account or company reference.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Count">The number of payments.</param>
/// <param name="Total">The control total.</param>
/// <param name="Hash">The hash total of the account numbers.</param>
public sealed record BankFileHeader(DateOnly Date, string Originator, string Currency, int Count, decimal Total, long Hash);

/// <summary>One payment in a bank file.</summary>
/// <param name="Sequence">The line number (from 1).</param>
/// <param name="Reference">The payment reference.</param>
/// <param name="Name">The beneficiary.</param>
/// <param name="Bank">The beneficiary's bank.</param>
/// <param name="Branch">The branch code.</param>
/// <param name="Account">The account number.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Amount">The amount.</param>
public sealed record BankFileRow(int Sequence, string Reference, string Name, string Bank, string Branch, string Account, string Currency, decimal Amount);

/// <summary>
/// Renders bank payment files from templates (brief §4.15.7: "define the format as a template, not as code"). A template is text with
/// placeholders <c>{Field[,width][:format]}</c>: a negative width left-aligns, a positive width right-aligns, a width starting with
/// 0 zero-pads, and every padded value is cut to the width (fixed-width bank files). The format <c>cents</c> writes an amount as whole
/// cents; any other format is a .NET format string (dates default to yyyyMMdd, amounts to 0.00). Header and trailer fields: Date,
/// Originator, Currency, Count, Total, Hash. Detail fields: Seq, Date, Reference, Name, Bank, Branch, Account, Currency, Amount.
/// </summary>
public static partial class BankFileFormatter
{
    /// <summary>The header and trailer fields.</summary>
    public static readonly IReadOnlySet<string> FileFields = new HashSet<string>(["Date", "Originator", "Currency", "Count", "Total", "Hash"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The detail fields.</summary>
    public static readonly IReadOnlySet<string> DetailFields = new HashSet<string>(["Seq", "Date", "Reference", "Name", "Bank", "Branch", "Account", "Currency", "Amount"], StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{(?<field>[A-Za-z]+)(,(?<width>-?\d+))?(:(?<format>[^}]+))?\}")]
    private static partial Regex Placeholder();

    /// <summary>The hash total of a set of account numbers: the sum of each one's last eleven digits, modulo 10^12.</summary>
    /// <param name="accounts">The account numbers.</param>
    /// <returns>The hash.</returns>
    public static long HashTotal(IEnumerable<string> accounts)
    {
        long hash = 0;
        foreach (string account in accounts)
        {
            string digits = new([.. account.Where(char.IsAsciiDigit)]);
            if (digits.Length > 0)
            {
                hash = (hash + long.Parse(digits[^Math.Min(digits.Length, 11)..], CultureInfo.InvariantCulture)) % 1_000_000_000_000L;
            }
        }

        return hash;
    }

    /// <summary>The placeholders in a template that are not among the allowed fields.</summary>
    /// <param name="template">The template.</param>
    /// <param name="allowed">The allowed fields.</param>
    /// <returns>The unknown field names.</returns>
    public static IReadOnlyList<string> UnknownFields(string? template, IReadOnlySet<string> allowed)
        => [.. Placeholder().Matches(template ?? string.Empty).Select(m => m.Groups["field"].Value).Where(f => !allowed.Contains(f)).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Renders the file: header (if any), one detail line per row, trailer (if any).</summary>
    /// <param name="header">The header template.</param>
    /// <param name="detail">The detail template.</param>
    /// <param name="trailer">The trailer template.</param>
    /// <param name="file">The file-level values.</param>
    /// <param name="rows">The payments.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>The file text.</returns>
    public static string Render(string? header, string detail, string? trailer, BankFileHeader file, IEnumerable<BankFileRow> rows, string lineEnding = "\r\n")
    {
        StringBuilder text = new();
        object? FileValue(string field) => field.ToUpperInvariant() switch
        {
            "DATE" => file.Date,
            "ORIGINATOR" => file.Originator,
            "CURRENCY" => file.Currency,
            "COUNT" => file.Count,
            "TOTAL" => file.Total,
            "HASH" => file.Hash,
            _ => null,
        };

        if (!string.IsNullOrEmpty(header))
        {
            text.Append(Fill(header, FileValue)).Append(lineEnding);
        }

        foreach (BankFileRow row in rows)
        {
            text.Append(Fill(detail, field => field.ToUpperInvariant() switch
            {
                "SEQ" => row.Sequence,
                "DATE" => file.Date,
                "REFERENCE" => row.Reference,
                "NAME" => row.Name,
                "BANK" => row.Bank,
                "BRANCH" => row.Branch,
                "ACCOUNT" => row.Account,
                "CURRENCY" => row.Currency,
                "AMOUNT" => row.Amount,
                _ => null,
            })).Append(lineEnding);
        }

        if (!string.IsNullOrEmpty(trailer))
        {
            text.Append(Fill(trailer, FileValue)).Append(lineEnding);
        }

        return text.ToString();
    }

    private static string Fill(string template, Func<string, object?> value)
        => Placeholder().Replace(template, match =>
        {
            object? raw = value(match.Groups["field"].Value);
            string format = match.Groups["format"].Success ? match.Groups["format"].Value : string.Empty;
            string text = raw switch
            {
                decimal amount when format.Equals("cents", StringComparison.OrdinalIgnoreCase) => ((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture),
                decimal amount => amount.ToString(format.Length == 0 ? "0.00" : format, CultureInfo.InvariantCulture),
                DateOnly date => date.ToString(format.Length == 0 ? "yyyyMMdd" : format, CultureInfo.InvariantCulture),
                IFormattable number => number.ToString(format.Length == 0 ? null : format, CultureInfo.InvariantCulture),
                null => string.Empty,
                _ => raw.ToString() ?? string.Empty,
            };

            if (!match.Groups["width"].Success)
            {
                return text;
            }

            string widthText = match.Groups["width"].Value;
            int width = Math.Abs(int.Parse(widthText, CultureInfo.InvariantCulture));
            if (text.Length > width)
            {
                return text[..width];
            }

            return widthText.StartsWith('-') ? text.PadRight(width) : text.PadLeft(width, widthText.StartsWith('0') ? '0' : ' ');
        });
}
