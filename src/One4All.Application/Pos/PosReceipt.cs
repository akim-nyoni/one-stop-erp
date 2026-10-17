using System.Globalization;
using System.Text;

namespace One4All.Application.Pos;

/// <summary>A printed line on a receipt.</summary>
/// <param name="Description">What was sold.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="LineGross">The gross line total.</param>
public sealed record PosReceiptLine(string Description, decimal Quantity, decimal LineGross);

/// <summary>A tender printed on a receipt.</summary>
/// <param name="Label">The tender label (e.g. "Cash", "Card").</param>
/// <param name="Amount">The amount.</param>
public sealed record PosReceiptTender(string Label, decimal Amount);

/// <summary>
/// The data for a POS sales receipt, independent of how it is rendered. Built from a completed sale; the
/// <see cref="PosReceiptFormatter"/> turns it into thermal-printable text.
/// </summary>
public sealed class PosReceiptModel
{
    /// <summary>The trading company name (receipt heading).</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>The receipt / sale number.</summary>
    public string ReceiptNumber { get; init; } = string.Empty;

    /// <summary>When the sale was made.</summary>
    public DateTime SoldOn { get; init; }

    /// <summary>The cashier's name.</summary>
    public string CashierName { get; init; } = string.Empty;

    /// <summary>The terminal name.</summary>
    public string TerminalName { get; init; } = string.Empty;

    /// <summary>The sold lines.</summary>
    public IReadOnlyList<PosReceiptLine> Lines { get; init; } = [];

    /// <summary>The net subtotal.</summary>
    public decimal TotalNet { get; init; }

    /// <summary>The tax total.</summary>
    public decimal TotalTax { get; init; }

    /// <summary>The gross total.</summary>
    public decimal TotalGross { get; init; }

    /// <summary>The tenders taken (change excluded - shown separately).</summary>
    public IReadOnlyList<PosReceiptTender> Tenders { get; init; } = [];

    /// <summary>The change given.</summary>
    public decimal Change { get; init; }

    /// <summary>The fiscal code the device gave the receipt (none when no device is used).</summary>
    public string? FiscalCode { get; init; }

    /// <summary>The device's verification link or signature (what a QR on the slip carries).</summary>
    public string? FiscalVerification { get; init; }

    /// <summary>Whether the receipt is still waiting for the fiscal device.</summary>
    public bool FiscalPending { get; init; }
}

/// <summary>
/// Renders a <see cref="PosReceiptModel"/> to fixed-width plain text suitable for a thermal printer (or a
/// monospace preview). Pure and dependency-free, so the layout is unit-tested; the POS service builds the
/// model from a stored sale.
/// </summary>
public static class PosReceiptFormatter
{
    /// <summary>The default thermal receipt width in characters.</summary>
    public const int DefaultWidth = 40;

    /// <summary>Renders the receipt to plain text.</summary>
    /// <param name="model">The receipt data.</param>
    /// <param name="width">The character width (default 40).</param>
    /// <returns>The formatted receipt.</returns>
    public static string ToPlainText(PosReceiptModel model, int width = DefaultWidth)
    {
        if (width < 24)
        {
            width = 24; // keep the two-column rows legible
        }

        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, model.CompanyName, width);
        AppendCentered(sb, "SALES RECEIPT", width);
        sb.AppendLine(rule);

        sb.AppendLine(TwoColumn($"Receipt: {model.ReceiptNumber}", model.SoldOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), width));
        if (!string.IsNullOrWhiteSpace(model.CashierName) || !string.IsNullOrWhiteSpace(model.TerminalName))
        {
            sb.AppendLine(TwoColumn($"Cashier: {model.CashierName}", model.TerminalName, width));
        }

        sb.AppendLine(rule);

        foreach (PosReceiptLine line in model.Lines)
        {
            string qty = line.Quantity == Math.Truncate(line.Quantity)
                ? line.Quantity.ToString("0", CultureInfo.InvariantCulture)
                : line.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
            sb.AppendLine(TwoColumn($"{qty} x {line.Description}", Money(line.LineGross), width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn("Subtotal", Money(model.TotalNet), width));
        sb.AppendLine(TwoColumn("Tax", Money(model.TotalTax), width));
        sb.AppendLine(TwoColumn("TOTAL", Money(model.TotalGross), width));
        sb.AppendLine(rule);

        foreach (PosReceiptTender tender in model.Tenders)
        {
            sb.AppendLine(TwoColumn(tender.Label, Money(tender.Amount), width));
        }

        if (model.Change != 0m)
        {
            sb.AppendLine(TwoColumn("Change", Money(model.Change), width));
        }

        sb.AppendLine(rule);
        if (model.FiscalCode is string fiscalCode)
        {
            sb.AppendLine(TwoColumn("Fiscal code", fiscalCode, width));
            if (model.FiscalVerification is string verification)
            {
                sb.AppendLine("Verify:");
                for (int at = 0; at < verification.Length; at += width)
                {
                    sb.AppendLine(verification.Substring(at, Math.Min(width, verification.Length - at)));
                }
            }

            sb.AppendLine(rule);
        }
        else if (model.FiscalPending)
        {
            AppendCentered(sb, "FISCAL SIGNATURE PENDING", width);
            sb.AppendLine(rule);
        }

        AppendCentered(sb, "Thank you!", width);

        return sb.ToString();
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string TwoColumn(string left, string right, int width)
    {
        int max = width - right.Length - 1;
        if (max < 1)
        {
            return right.PadLeft(width);
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

        int pad = (width - text.Length) / 2;
        sb.AppendLine(new string(' ', pad) + text);
    }
}
