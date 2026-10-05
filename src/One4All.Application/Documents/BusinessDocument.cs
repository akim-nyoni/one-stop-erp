using System.Globalization;
using System.Text;

namespace One4All.Application.Documents;

/// <summary>A line on a business document.</summary>
/// <param name="Code">The item or reference code.</param>
/// <param name="Description">The description.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price, when the document shows prices.</param>
/// <param name="Amount">The line amount, when the document shows prices.</param>
public sealed record BusinessDocumentLine(string Code, string Description, decimal Quantity, decimal? UnitPrice = null, decimal? Amount = null);

/// <summary>
/// The data behind a standard business document (quotation, order confirmation, delivery / packing note, requisition, goods received
/// note, debit note, bill, fee invoice, payment certificate): a title, the two parties, reference fields, lines, totals and footer.
/// </summary>
public sealed class BusinessDocument
{
    /// <summary>Gets the document title (QUOTATION, DELIVERY NOTE…).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the issuing company.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>Gets the issuer's tax number.</summary>
    public string? CompanyTaxNumber { get; init; }

    /// <summary>Gets the label for the other party (To, Supplier, Deliver to…).</summary>
    public string PartyLabel { get; init; } = "To";

    /// <summary>Gets the other party.</summary>
    public string PartyName { get; init; } = string.Empty;

    /// <summary>Gets the reference fields (number, dates, terms…).</summary>
    public IReadOnlyList<(string Label, string Value)> Fields { get; init; } = [];

    /// <summary>Gets a value indicating whether lines show price and amount.</summary>
    public bool ShowPrices { get; init; } = true;

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<BusinessDocumentLine> Lines { get; init; } = [];

    /// <summary>Gets the totals block (label, amount), printed in order.</summary>
    public IReadOnlyList<(string Label, decimal Amount)> Totals { get; init; } = [];

    /// <summary>Gets the footer notes.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Gets the signature lines (Received by, Authorised by…).</summary>
    public IReadOnlyList<string> Signatures { get; init; } = [];
}

/// <summary>Renders any <see cref="BusinessDocument"/> to fixed-width plain text. Pure and unit-tested.</summary>
public static class BusinessDocumentFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 72;

    /// <summary>Renders the document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="width">The width (minimum 48).</param>
    /// <returns>The text.</returns>
    public static string ToPlainText(BusinessDocument document, int width = DefaultWidth)
    {
        width = Math.Max(48, width);
        StringBuilder sb = new();
        string rule = new('-', width);
        Centered(sb, document.CompanyName, width);
        if (!string.IsNullOrWhiteSpace(document.CompanyTaxNumber))
        {
            Centered(sb, $"Tax No. {document.CompanyTaxNumber}", width);
        }

        Centered(sb, document.Title.ToUpperInvariant(), width);
        sb.AppendLine(rule);
        sb.AppendLine(Fit($"{document.PartyLabel}: {document.PartyName}", width));
        for (int i = 0; i < document.Fields.Count; i += 2)
        {
            string left = $"{document.Fields[i].Label}: {document.Fields[i].Value}";
            string right = i + 1 < document.Fields.Count ? $"{document.Fields[i + 1].Label}: {document.Fields[i + 1].Value}" : string.Empty;
            sb.AppendLine(TwoColumn(left, right, width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(document.ShowPrices ? Columns("Item / description", "Qty", "Price", "Amount", width) : Columns("Item / description", "Qty", null, null, width));
        int n = 1;
        foreach (BusinessDocumentLine line in document.Lines)
        {
            string label = $"{n,2}. {(line.Code.Length == 0 ? string.Empty : line.Code + " ")}{line.Description}";
            sb.AppendLine(document.ShowPrices
                ? Columns(label, Qty(line.Quantity), line.UnitPrice is decimal p ? Money(p) : string.Empty, line.Amount is decimal a ? Money(a) : string.Empty, width)
                : Columns(label, Qty(line.Quantity), null, null, width));
            n++;
        }

        sb.AppendLine(rule);
        foreach ((string label, decimal amount) in document.Totals)
        {
            sb.AppendLine(TwoColumn(label, Money(amount), width));
        }

        if (document.Totals.Count > 0)
        {
            sb.AppendLine(rule);
        }

        foreach (string note in document.Notes)
        {
            sb.AppendLine(Fit(note, width));
        }

        foreach (string signature in document.Signatures)
        {
            sb.AppendLine();
            sb.AppendLine(Fit($"{signature}: ______________________  Date: __________", width));
        }

        return sb.ToString();
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Qty(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Fit(string text, int width) => text.Length > width ? text[..width] : text;

    private static string Columns(string left, string qty, string? price, string? amount, int width)
    {
        string tail = qty.PadLeft(10) + (price is null ? string.Empty : price.PadLeft(13)) + (amount is null ? string.Empty : amount.PadLeft(14));
        int max = width - tail.Length - 1;
        if (left.Length > max)
        {
            left = left[..Math.Max(0, max)];
        }

        return left + tail.PadLeft(width - left.Length);
    }

    private static string TwoColumn(string left, string right, int width)
    {
        int max = width - right.Length - 1;
        if (left.Length > max)
        {
            left = left[..Math.Max(0, max)];
        }

        return left + right.PadLeft(width - left.Length);
    }

    private static void Centered(StringBuilder sb, string text, int width)
        => sb.AppendLine(text.Length >= width ? text[..width] : new string(' ', (width - text.Length) / 2) + text);
}

/// <summary>A shelf / bin label.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Description">The description.</param>
/// <param name="Price">The selling price.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Barcode">The barcode value (the item code when none is held).</param>
public sealed record ShelfLabel(string ItemCode, string Description, decimal Price, string Currency, string Barcode);

/// <summary>Lays shelf labels out in boxes, several to a row, with a Code 39 style barcode text line (*VALUE*). Pure and unit-tested.</summary>
public static class ShelfLabelFormatter
{
    /// <summary>Renders the labels.</summary>
    /// <param name="labels">The labels.</param>
    /// <param name="perRow">Labels across the sheet (1–4).</param>
    /// <param name="labelWidth">Characters per label (minimum 20).</param>
    /// <returns>The sheet.</returns>
    public static string ToPlainText(IReadOnlyList<ShelfLabel> labels, int perRow = 2, int labelWidth = 32)
    {
        perRow = Math.Clamp(perRow, 1, 4);
        labelWidth = Math.Max(20, labelWidth);
        int inner = labelWidth - 4;
        StringBuilder sb = new();
        foreach (ShelfLabel[] row in labels.Chunk(perRow))
        {
            string border = string.Join(" ", row.Select(_ => "+" + new string('-', labelWidth - 2) + "+"));
            sb.AppendLine(border);
            foreach (Func<ShelfLabel, string> part in new Func<ShelfLabel, string>[]
                     {
                         l => l.Description,
                         l => l.ItemCode,
                         l => $"{l.Currency} {l.Price.ToString("N2", CultureInfo.InvariantCulture)}",
                         l => $"*{l.Barcode.ToUpperInvariant()}*",
                     })
            {
                sb.AppendLine(string.Join(" ", row.Select(l =>
                {
                    string text = part(l);
                    text = text.Length > inner ? text[..inner] : text;
                    return "| " + text.PadRight(inner) + " |";
                })));
            }

            sb.AppendLine(border);
        }

        return sb.ToString();
    }
}
