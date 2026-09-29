using System.Globalization;
using System.Text;
using One4All.Application.Abstractions.Trading;

namespace One4All.Application.Trading;

/// <summary>
/// Renders the existing <see cref="RemittanceAdvice"/> report data (the invoices a supplier payment settled)
/// to fixed-width plain text for printing/emailing. Pure and unit-tested; the trading reports supply the data.
/// </summary>
public static class RemittanceFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 52;

    /// <summary>Renders the remittance advice to plain text under a company heading.</summary>
    /// <param name="advice">The remittance data (from <c>ITradingReports.RemittanceAdviceAsync</c>).</param>
    /// <param name="companyName">The paying company's name (heading).</param>
    /// <param name="width">The character width (default 52).</param>
    /// <returns>The formatted remittance.</returns>
    public static string ToPlainText(RemittanceAdvice advice, string companyName, int width = DefaultWidth)
    {
        if (width < 32)
        {
            width = 32;
        }

        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, companyName, width);
        AppendCentered(sb, "REMITTANCE ADVICE", width);
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"To: {advice.SupplierName} ({advice.SupplierCode})", string.Empty, width));
        sb.AppendLine(TwoColumn($"Payment: {advice.PaymentNumber}", advice.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), width));
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn("Invoice", "Amount", width));
        foreach (RemittanceLine line in advice.Lines)
        {
            sb.AppendLine(TwoColumn($"{line.InvoiceNumber}  {line.InvoiceDate:yyyy-MM-dd}", Money(line.AllocatedAmount), width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"TOTAL PAID ({advice.Currency})", Money(advice.TotalAllocated), width));
        sb.AppendLine(rule);

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

        sb.AppendLine(new string(' ', (width - text.Length) / 2) + text);
    }
}
