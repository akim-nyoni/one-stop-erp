using System.Globalization;
using System.Text;

namespace One4All.Application.SalesOrders;

/// <summary>One line to pick.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Description">The line description.</param>
/// <param name="ToPick">The quantity still to despatch (ordered − despatched).</param>
/// <param name="OnHand">The quantity on hand in the picking warehouse.</param>
public sealed record PickListLine(string ItemCode, string Description, decimal ToPick, decimal OnHand)
{
    /// <summary>The quantity the warehouse cannot supply from this location (0 when fully pickable).</summary>
    public decimal Short => Math.Max(0m, ToPick - OnHand);
}

/// <summary>The data behind a pick list for one sales order from one warehouse.</summary>
/// <param name="CompanyName">The issuing company.</param>
/// <param name="OrderNumber">The sales order number.</param>
/// <param name="OrderDate">The order date.</param>
/// <param name="CustomerCode">The customer's account reference.</param>
/// <param name="CustomerName">The customer name.</param>
/// <param name="WarehouseCode">The picking warehouse.</param>
/// <param name="Lines">The lines still to pick.</param>
public sealed record PickListDocument(string CompanyName, string OrderNumber, DateOnly OrderDate, string CustomerCode, string CustomerName, string WarehouseCode, IReadOnlyList<PickListLine> Lines)
{
    /// <summary>True when every line can be picked in full from the warehouse.</summary>
    public bool IsComplete => Lines.All(l => l.Short == 0m);
}

/// <summary>
/// Renders a <see cref="PickListDocument"/> to fixed-width plain text for the warehouse: one line per
/// outstanding order line with the quantity to pick, what is on hand, and a SHORT marker where the location
/// cannot supply it. Pure and unit-tested.
/// </summary>
public static class PickListFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 64;

    /// <summary>Renders the pick list.</summary>
    /// <param name="document">The pick list data.</param>
    /// <param name="width">The character width (default 64, minimum 40).</param>
    /// <returns>The formatted pick list.</returns>
    public static string ToPlainText(PickListDocument document, int width = DefaultWidth)
    {
        if (width < 40)
        {
            width = 40;
        }

        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, document.CompanyName, width);
        AppendCentered(sb, "PICK LIST", width);
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"Order: {document.OrderNumber}", document.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), width));
        sb.AppendLine(TwoColumn($"Customer: {document.CustomerName} ({document.CustomerCode})", string.Empty, width));
        sb.AppendLine(TwoColumn($"Warehouse: {document.WarehouseCode}", string.Empty, width));
        sb.AppendLine(rule);
        sb.AppendLine(Columns("Item / description", "Pick", "On hand", "Picked", width));
        foreach (PickListLine line in document.Lines)
        {
            string left = $"{line.ItemCode} {line.Description}";
            string status = line.Short > 0m ? $"SHORT {Qty(line.Short)}" : "[    ]";
            sb.AppendLine(Columns(left, Qty(line.ToPick), Qty(line.OnHand), status, width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"{document.Lines.Count} line(s)", document.IsComplete ? "COMPLETE PICK" : "PART PICK - SHORTAGES", width));
        sb.AppendLine(TwoColumn("Picked by: ____________", "Checked by: ____________", width));
        sb.AppendLine(rule);

        return sb.ToString();
    }

    private static string Qty(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Columns(string left, string pick, string onHand, string status, int width)
    {
        const int QtyWidth = 10;
        const int StatusWidth = 14;
        string tail = pick.PadLeft(QtyWidth) + onHand.PadLeft(QtyWidth) + status.PadLeft(StatusWidth);
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
