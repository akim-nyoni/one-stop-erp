using System.Globalization;
using System.Text;
using One4All.Core.Vouchers;

namespace One4All.Application.Vouchers;

/// <summary>A line of a printed voucher.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="AccountName">Its name.</param>
/// <param name="Narrative">The line description.</param>
/// <param name="Debit">The debit.</param>
/// <param name="Credit">The credit.</param>
public sealed record VoucherDocumentLine(string AccountCode, string AccountName, string? Narrative, decimal Debit, decimal Credit);

/// <summary>Everything a printed accounting voucher shows.</summary>
/// <param name="CompanyName">The company.</param>
/// <param name="Type">The kind.</param>
/// <param name="Number">The voucher number.</param>
/// <param name="Date">The voucher date.</param>
/// <param name="Party">Who is paid or pays.</param>
/// <param name="Narrative">The description.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Lines">The lines.</param>
/// <param name="PreparedBy">Who prepared it.</param>
/// <param name="ApprovedBy">Who approved it, once approved.</param>
/// <param name="JournalNumber">The posting, once approved.</param>
public sealed record AccountingVoucherDocument(string CompanyName, AccountingVoucherType Type, string Number, DateOnly Date, string Party, string? Narrative, string Currency,
    IReadOnlyList<VoucherDocumentLine> Lines, string PreparedBy, string? ApprovedBy, string? JournalNumber)
{
    /// <summary>Gets who checked it, if anyone has.</summary>
    public string? CheckedBy { get; init; }

    /// <summary>Gets how many supporting documents are attached.</summary>
    public int Attachments { get; init; }
}

/// <summary>
/// Prints an accounting voucher in fixed-width text: the voucher title for its type, number and date, the party, each
/// line with its account, the total, the posting reference, and signature blocks for preparer, approver and recipient
/// (brief §4.8.3). Pure and unit-tested.
/// </summary>
public static class AccountingVoucherFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 72;

    /// <summary>The printed title for a voucher type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The title.</returns>
    public static string Title(AccountingVoucherType type) => type switch
    {
        AccountingVoucherType.Payment => "PAYMENT VOUCHER",
        AccountingVoucherType.Receipt => "RECEIPT VOUCHER",
        AccountingVoucherType.Journal => "JOURNAL VOUCHER",
        _ => "PETTY CASH VOUCHER",
    };

    /// <summary>Renders the voucher.</summary>
    /// <param name="document">The voucher.</param>
    /// <param name="width">The width (at least 56).</param>
    /// <returns>The printed voucher.</returns>
    public static string ToPlainText(AccountingVoucherDocument document, int width = DefaultWidth)
    {
        ArgumentNullException.ThrowIfNull(document);
        width = Math.Max(width, 56);
        string rule = new('-', width);
        StringBuilder sb = new();
        Centre(sb, document.CompanyName, width);
        Centre(sb, Title(document.Type), width);
        sb.AppendLine(rule);
        sb.AppendLine(Pair($"No: {document.Number}", $"Date: {document.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}", width));
        sb.AppendLine(document.Type == AccountingVoucherType.Receipt ? $"Received from: {document.Party}" : document.Type == AccountingVoucherType.Journal ? $"Re: {document.Party}" : $"Pay: {document.Party}");
        if (!string.IsNullOrWhiteSpace(document.Narrative))
        {
            sb.AppendLine($"Being: {document.Narrative}");
        }

        sb.AppendLine(rule);
        sb.AppendLine(Row("Account", "Debit", "Credit", width));
        foreach (VoucherDocumentLine line in document.Lines)
        {
            string label = $"{line.AccountCode} {line.AccountName}" + (string.IsNullOrWhiteSpace(line.Narrative) ? string.Empty : $" - {line.Narrative}");
            sb.AppendLine(Row(label, Money(line.Debit), Money(line.Credit), width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(Row($"TOTAL ({document.Currency})", Money(document.Lines.Sum(l => l.Debit)), Money(document.Lines.Sum(l => l.Credit)), width));
        sb.AppendLine(rule);
        sb.AppendLine(document.JournalNumber is null ? "Status: awaiting approval - not posted" : $"Posted: journal {document.JournalNumber}");
        sb.AppendLine();
        if (document.Attachments > 0)
        {
            sb.AppendLine(document.Attachments == 1 ? "Supporting documents: 1 attached" : $"Supporting documents: {document.Attachments} attached");
        }

        sb.AppendLine(Pair($"Prepared by: {document.PreparedBy}", "Signature: ____________", width));
        sb.AppendLine(Pair($"Checked by: {document.CheckedBy ?? "____________"}", "Signature: ____________", width));
        sb.AppendLine(Pair($"Approved by: {document.ApprovedBy ?? "____________"}", "Signature: ____________", width));
        if (document.Type is AccountingVoucherType.Payment or AccountingVoucherType.PettyCash)
        {
            sb.AppendLine(Pair("Received by: ____________", "Signature: ____________", width));
        }

        return sb.ToString();
    }

    private static string Money(decimal amount) => amount == 0m ? string.Empty : amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string Row(string label, string debit, string credit, int width)
    {
        int amountWidth = 14;
        int labelWidth = width - (2 * amountWidth);
        string trimmed = label.Length > labelWidth ? label[..(labelWidth - 1)] : label;
        return trimmed.PadRight(labelWidth) + debit.PadLeft(amountWidth) + credit.PadLeft(amountWidth);
    }

    private static string Pair(string left, string right, int width)
    {
        int gap = Math.Max(1, width - left.Length - right.Length);
        return left + new string(' ', gap) + right;
    }

    private static void Centre(StringBuilder sb, string text, int width)
        => sb.AppendLine(text.Length >= width ? text : new string(' ', (width - text.Length) / 2) + text);
}
