using System.Globalization;
using System.Text;

namespace One4All.Application.Trading;

/// <summary>The content of a withholding tax certificate given to a supplier.</summary>
public sealed class WithholdingCertificateDocument
{
    /// <summary>The paying company's name.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>The certificate number.</summary>
    public string CertificateNumber { get; init; } = string.Empty;

    /// <summary>The supplier's code.</summary>
    public string SupplierCode { get; init; } = string.Empty;

    /// <summary>The supplier's name.</summary>
    public string SupplierName { get; init; } = string.Empty;

    /// <summary>The payment's document number.</summary>
    public string PaymentReference { get; init; } = string.Empty;

    /// <summary>The payment date.</summary>
    public DateOnly PaymentDate { get; init; }

    /// <summary>The currency of the amounts.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>The gross amount paid.</summary>
    public decimal Gross { get; init; }

    /// <summary>The rate withheld, as a percent.</summary>
    public decimal RatePercent { get; init; }

    /// <summary>The tax withheld.</summary>
    public decimal Withheld { get; init; }
}

/// <summary>Renders a withholding tax certificate to fixed-width plain text. Pure and unit-tested.</summary>
public static class WithholdingCertificateFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 60;

    /// <summary>Renders the certificate.</summary>
    /// <param name="document">The certificate content.</param>
    /// <param name="width">The character width (at least 40).</param>
    /// <returns>The certificate text.</returns>
    public static string ToPlainText(WithholdingCertificateDocument document, int width = DefaultWidth)
    {
        ArgumentNullException.ThrowIfNull(document);
        width = Math.Max(width, 40);
        string rule = new('-', width);
        StringBuilder sb = new();

        Centre(sb, document.CompanyName, width);
        Centre(sb, "WITHHOLDING TAX CERTIFICATE", width);
        sb.AppendLine(rule);
        sb.AppendLine(Pair("Certificate", document.CertificateNumber, width));
        sb.AppendLine(Pair("Supplier", $"{document.SupplierCode} {document.SupplierName}", width));
        sb.AppendLine(Pair("Payment", document.PaymentReference, width));
        sb.AppendLine(Pair("Date", document.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), width));
        sb.AppendLine(rule);
        sb.AppendLine(Pair($"Gross amount ({document.Currency})", Money(document.Gross), width));
        sb.AppendLine(Pair($"Tax withheld at {document.RatePercent.ToString("0.##", CultureInfo.InvariantCulture)}%", Money(document.Withheld), width));
        sb.AppendLine(Pair("Net amount paid", Money(document.Gross - document.Withheld), width));
        sb.AppendLine(rule);
        sb.AppendLine("The tax withheld will be paid over to the revenue authority");
        sb.AppendLine("on the supplier's behalf.");
        return sb.ToString();
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Pair(string left, string right, int width)
    {
        int room = width - right.Length - 1;
        if (left.Length > room)
        {
            left = room > 0 ? left[..room] : string.Empty;
        }

        return left + right.PadLeft(width - left.Length);
    }

    private static void Centre(StringBuilder sb, string text, int width)
        => sb.AppendLine(text.Length >= width ? text[..width] : new string(' ', (width - text.Length) / 2) + text);
}
