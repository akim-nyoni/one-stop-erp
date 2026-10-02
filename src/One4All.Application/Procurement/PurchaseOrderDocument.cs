using System.Globalization;
using System.Text;

namespace One4All.Application.Procurement;

/// <summary>A line on a purchase order document.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Description">The line description.</param>
/// <param name="Quantity">The quantity ordered.</param>
/// <param name="UnitPrice">The agreed unit price.</param>
public sealed record PurchaseOrderDocumentLine(string ItemCode, string Description, decimal Quantity, decimal UnitPrice)
{
    /// <summary>Quantity × unit price.</summary>
    public decimal LineTotal => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);
}

/// <summary>The data behind a purchase order sent to a supplier.</summary>
/// <param name="CompanyName">The buying company.</param>
/// <param name="CompanyTaxNumber">The buyer's tax registration (optional).</param>
/// <param name="SupplierCode">The supplier's account reference.</param>
/// <param name="SupplierName">The supplier name.</param>
/// <param name="OrderNumber">The purchase order number.</param>
/// <param name="OrderDate">The order date.</param>
/// <param name="Currency">The order currency.</param>
/// <param name="PaymentTermsDays">The supplier's payment terms.</param>
/// <param name="Status">The order status, as text.</param>
/// <param name="Lines">The order lines.</param>
public sealed record PurchaseOrderDocument(string CompanyName, string? CompanyTaxNumber, string SupplierCode, string SupplierName, string OrderNumber, DateOnly OrderDate, string Currency, int PaymentTermsDays, string Status, IReadOnlyList<PurchaseOrderDocumentLine> Lines)
{
    /// <summary>The order total (net).</summary>
    public decimal Total => Lines.Sum(l => l.LineTotal);
}

/// <summary>
/// Renders a <see cref="PurchaseOrderDocument"/> to fixed-width plain text for sending to the supplier:
/// buyer and supplier, order number and date, numbered lines with quantity, price and line total, the
/// order total and the payment terms. Pure and unit-tested.
/// </summary>
public static class PurchaseOrderFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 72;

    /// <summary>Renders the purchase order.</summary>
    /// <param name="document">The order data.</param>
    /// <param name="width">The character width (default 72, minimum 48).</param>
    /// <returns>The formatted order.</returns>
    public static string ToPlainText(PurchaseOrderDocument document, int width = DefaultWidth)
    {
        if (width < 48)
        {
            width = 48;
        }

        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, document.CompanyName, width);
        if (document.CompanyTaxNumber is not null)
        {
            AppendCentered(sb, $"VAT No. {document.CompanyTaxNumber}", width);
        }

        AppendCentered(sb, "PURCHASE ORDER", width);
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"To: {document.SupplierName} ({document.SupplierCode})", string.Empty, width));
        sb.AppendLine(TwoColumn($"Order: {document.OrderNumber}", $"Date: {Date(document.OrderDate)}", width));
        sb.AppendLine(TwoColumn($"Currency: {document.Currency}", $"Status: {document.Status}", width));
        sb.AppendLine(rule);
        sb.AppendLine(Columns("Item / description", "Qty", "Price", "Total", width));
        int n = 1;
        foreach (PurchaseOrderDocumentLine line in document.Lines)
        {
            sb.AppendLine(Columns($"{n,2}. {line.ItemCode} {line.Description}", Qty(line.Quantity), Money(line.UnitPrice), Money(line.LineTotal), width));
            n++;
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"ORDER TOTAL ({document.Currency})", Money(document.Total), width));
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"Payment terms: {document.PaymentTermsDays} days from invoice. Please quote {document.OrderNumber} on all documents.", string.Empty, width));
        sb.AppendLine(TwoColumn("Authorised by: ____________________", string.Empty, width));
        sb.AppendLine(rule);

        return sb.ToString();
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Qty(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Columns(string left, string qty, string price, string total, int width)
    {
        const int QtyWidth = 10;
        const int MoneyWidth = 13;
        string tail = qty.PadLeft(QtyWidth) + price.PadLeft(MoneyWidth) + total.PadLeft(MoneyWidth);
        int max = width - tail.Length - 1;
        if (max < 1)
        {
            return tail.Length > width ? tail[^width..] : tail.PadLeft(width);
        }

        if (left.Length > max)
        {
            left = left[..max];
        }

        return left + tail.PadLeft(width - left.Length);
    }

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
