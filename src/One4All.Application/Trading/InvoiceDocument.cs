using System.Globalization;
using System.Text;

namespace One4All.Application.Trading;

/// <summary>A line on a customer invoice / credit note document.</summary>
/// <param name="Description">What was charged.</param>
/// <param name="Amount">The net amount in document currency.</param>
public sealed record InvoiceDocumentLine(string Description, decimal Amount);

/// <summary>The data behind a customer tax invoice or credit note.</summary>
/// <param name="IsCreditNote">True for a credit note, false for an invoice.</param>
/// <param name="CompanyName">The issuing company.</param>
/// <param name="CompanyTaxNumber">The issuer's tax / VAT registration (optional).</param>
/// <param name="CustomerCode">The customer's account reference.</param>
/// <param name="CustomerName">The customer name.</param>
/// <param name="CustomerTaxNumber">The customer's tax registration (optional).</param>
/// <param name="DocumentNumber">The document number.</param>
/// <param name="DocumentDate">The document date.</param>
/// <param name="DueDate">The payment due date (ignored on a credit note).</param>
/// <param name="Currency">The document currency.</param>
/// <param name="Lines">The net lines.</param>
/// <param name="Tax">The tax (VAT) on the document.</param>
/// <param name="Outstanding">The amount still unpaid / unapplied.</param>
/// <param name="Narrative">An optional narrative.</param>
public sealed record InvoiceDocument(bool IsCreditNote, string CompanyName, string? CompanyTaxNumber, string CustomerCode, string CustomerName, string? CustomerTaxNumber,
    string DocumentNumber, DateOnly DocumentDate, DateOnly DueDate, string Currency, IReadOnlyList<InvoiceDocumentLine> Lines, decimal Tax, decimal Outstanding, string? Narrative)
{
    /// <summary>The net total.</summary>
    public decimal Net => Lines.Sum(l => l.Amount);

    /// <summary>The gross total (net + tax).</summary>
    public decimal Gross => Net + Tax;
}

/// <summary>
/// Renders an <see cref="InvoiceDocument"/> to fixed-width plain text: a tax invoice or credit note with
/// issuer and customer details, numbered lines, net / VAT / gross totals, the balance due and payment
/// terms. Pure and unit-tested.
/// </summary>
public static class InvoiceFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 64;

    /// <summary>Renders the document.</summary>
    /// <param name="document">The invoice / credit note data.</param>
    /// <param name="width">The character width (default 64, minimum 40).</param>
    /// <returns>The formatted document.</returns>
    public static string ToPlainText(InvoiceDocument document, int width = DefaultWidth)
    {
        if (width < 40)
        {
            width = 40;
        }

        StringBuilder sb = new();
        string rule = new('-', width);
        string title = document.IsCreditNote ? "CREDIT NOTE" : document.CompanyTaxNumber is null ? "INVOICE" : "TAX INVOICE";

        AppendCentered(sb, document.CompanyName, width);
        if (document.CompanyTaxNumber is not null)
        {
            AppendCentered(sb, $"VAT No. {document.CompanyTaxNumber}", width);
        }

        AppendCentered(sb, title, width);
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"To: {document.CustomerName} ({document.CustomerCode})", string.Empty, width));
        if (document.CustomerTaxNumber is not null)
        {
            sb.AppendLine(TwoColumn($"Customer VAT No. {document.CustomerTaxNumber}", string.Empty, width));
        }

        sb.AppendLine(TwoColumn($"Number: {document.DocumentNumber}", $"Date: {Date(document.DocumentDate)}", width));
        if (!document.IsCreditNote)
        {
            sb.AppendLine(TwoColumn($"Currency: {document.Currency}", $"Due: {Date(document.DueDate)}", width));
        }
        else
        {
            sb.AppendLine(TwoColumn($"Currency: {document.Currency}", string.Empty, width));
        }

        if (!string.IsNullOrWhiteSpace(document.Narrative))
        {
            sb.AppendLine(TwoColumn(document.Narrative.Trim(), string.Empty, width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn("Description", "Amount", width));
        int n = 1;
        foreach (InvoiceDocumentLine line in document.Lines)
        {
            sb.AppendLine(TwoColumn($"{n,2}. {line.Description}", Money(line.Amount), width));
            n++;
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn("Net", Money(document.Net), width));
        sb.AppendLine(TwoColumn("VAT", Money(document.Tax), width));
        sb.AppendLine(TwoColumn($"TOTAL ({document.Currency})", Money(document.Gross), width));
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn(document.IsCreditNote ? "Unapplied credit" : "Balance due", Money(document.Outstanding), width));
        if (!document.IsCreditNote)
        {
            sb.AppendLine(TwoColumn($"Please pay by {Date(document.DueDate)} quoting {document.DocumentNumber}.", string.Empty, width));
        }

        sb.AppendLine(rule);
        return sb.ToString();
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string TwoColumn(string left, string right, int width)
    {
        int max = width - right.Length - 1;
        if (max < 1)
        {
            return right.Length > width ? right[..width] : right.PadLeft(width);
        }

        if (left.Length > max)
        {
            left = left[..max];
        }

        return left + right.PadLeft(width - left.Length);
    }

    private static void AppendCentered(StringBuilder sb, string text, int width)
    {
        if (text.Length >= width)
        {
            sb.AppendLine(text[..width]);
            return;
        }

        sb.AppendLine(new string(' ', (width - text.Length) / 2) + text);
    }
}
