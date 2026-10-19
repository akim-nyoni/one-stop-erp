using System.Globalization;
using System.Text;

namespace One4All.Application.Pos;

/// <summary>A printed line on a receipt.</summary>
/// <param name="Description">What was sold.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="LineGross">The gross line total.</param>
/// <param name="UnitPrice">The gross unit price (shown as "qty x price" when the layout asks for it).</param>
public sealed record PosReceiptLine(string Description, decimal Quantity, decimal LineGross, decimal? UnitPrice = null);

/// <summary>
/// How a receipt is laid out (brief §4.1 receipt customisation): the paper width, the title, header and footer lines, what is shown and how many copies
/// print. A layout is kept per company with an optional override per till.
/// </summary>
/// <param name="Width">Characters across the paper (48 on 80mm, 32 on 58mm).</param>
/// <param name="Title">The title under the company name (blank: none).</param>
/// <param name="Header">Lines under the company name (address, phone, tax number, a message); null: none.</param>
/// <param name="Footer">Lines at the foot; null: "Thank you!".</param>
/// <param name="ShowCashier">Whether the cashier is shown.</param>
/// <param name="ShowTerminal">Whether the till is shown.</param>
/// <param name="ShowUnitPrice">Whether each line shows its quantity and unit price on a line of its own.</param>
/// <param name="ShowTaxSummary">Whether the subtotal and tax are shown above the total.</param>
/// <param name="ShowTaxNumber">Whether the company's tax number is shown.</param>
/// <param name="ShowFiscal">Whether the fiscal code and verification are shown.</param>
/// <param name="Copies">How many copies print.</param>
public sealed record ReceiptOptions(int Width, string Title = "SALES RECEIPT", IReadOnlyList<string>? Header = null, IReadOnlyList<string>? Footer = null, bool ShowCashier = true,
    bool ShowTerminal = true, bool ShowUnitPrice = false, bool ShowTaxSummary = true, bool ShowTaxNumber = false, bool ShowFiscal = true, int Copies = 1)
{
    /// <summary>The characters across a paper width: 32 on 58mm paper, 48 on 80mm.</summary>
    /// <param name="paperMm">The paper width in millimetres.</param>
    /// <returns>The characters.</returns>
    public static int WidthFor(int paperMm) => paperMm <= 58 ? 32 : 48;
}

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

    /// <summary>The company's tax number (printed when the layout shows it).</summary>
    public string? CompanyTaxNumber { get; init; }

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
    public static string ToPlainText(PosReceiptModel model, int width = DefaultWidth) => ToPlainText(model, new ReceiptOptions(width));

    /// <summary>Renders the receipt to plain text in a layout.</summary>
    /// <param name="model">The receipt data.</param>
    /// <param name="options">The layout.</param>
    /// <returns>The formatted receipt.</returns>
    public static string ToPlainText(PosReceiptModel model, ReceiptOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        int width = Math.Max(24, options.Width); // keep the two-column rows legible

        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, model.CompanyName, width);
        if (options.ShowTaxNumber && !string.IsNullOrWhiteSpace(model.CompanyTaxNumber))
        {
            AppendCentered(sb, $"Tax no: {model.CompanyTaxNumber}", width);
        }

        foreach (string line in options.Header ?? [])
        {
            AppendCentered(sb, line, width);
        }

        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            AppendCentered(sb, options.Title, width);
        }

        sb.AppendLine(rule);

        sb.AppendLine(TwoColumn($"Receipt: {model.ReceiptNumber}", model.SoldOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), width));
        string cashier = options.ShowCashier && !string.IsNullOrWhiteSpace(model.CashierName) ? $"Cashier: {model.CashierName}" : string.Empty;
        string terminal = options.ShowTerminal ? model.TerminalName : string.Empty;
        if (options.ShowCashier && options.ShowTerminal && (!string.IsNullOrWhiteSpace(model.CashierName) || !string.IsNullOrWhiteSpace(model.TerminalName)))
        {
            sb.AppendLine(TwoColumn($"Cashier: {model.CashierName}", model.TerminalName, width));
        }
        else if (cashier.Length > 0 || terminal.Length > 0)
        {
            sb.AppendLine(TwoColumn(cashier, terminal, width));
        }

        sb.AppendLine(rule);

        foreach (PosReceiptLine line in model.Lines)
        {
            string qty = line.Quantity == Math.Truncate(line.Quantity)
                ? line.Quantity.ToString("0", CultureInfo.InvariantCulture)
                : line.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
            if (options.ShowUnitPrice && line.UnitPrice is decimal unit)
            {
                sb.AppendLine(Fit(line.Description, width));
                sb.AppendLine(TwoColumn($"  {qty} x {Money(unit)}", Money(line.LineGross), width));
            }
            else
            {
                sb.AppendLine(TwoColumn($"{qty} x {line.Description}", Money(line.LineGross), width));
            }
        }

        sb.AppendLine(rule);
        if (options.ShowTaxSummary)
        {
            sb.AppendLine(TwoColumn("Subtotal", Money(model.TotalNet), width));
            sb.AppendLine(TwoColumn("Tax", Money(model.TotalTax), width));
        }

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
        if (options.ShowFiscal && model.FiscalCode is string fiscalCode)
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
        else if (options.ShowFiscal && model.FiscalPending)
        {
            AppendCentered(sb, "FISCAL SIGNATURE PENDING", width);
            sb.AppendLine(rule);
        }

        foreach (string line in options.Footer ?? ["Thank you!"])
        {
            AppendCentered(sb, line, width);
        }

        return sb.ToString();
    }

    /// <summary>Draws the paper's edges round a receipt, for an on-screen preview of how it prints.</summary>
    /// <param name="receipt">The receipt text.</param>
    /// <param name="width">Its width in characters.</param>
    /// <returns>The framed receipt.</returns>
    public static string Framed(string receipt, int width)
    {
        StringBuilder sb = new();
        string edge = "+" + new string('-', width + 2) + "+";
        sb.AppendLine(edge);
        foreach (string line in (receipt ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n'))
        {
            sb.AppendLine("| " + Fit(line, width).PadRight(width) + " |");
        }

        sb.AppendLine(edge);
        return sb.ToString();
    }

    private static string Fit(string text, int width) => text.Length > width ? text[..width] : text;

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
