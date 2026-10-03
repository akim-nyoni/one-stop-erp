using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers the withholding tax certificate text.</summary>
public sealed class WithholdingCertificateFormatterTests
{
    [Fact]
    public void The_certificate_shows_gross_withheld_and_net()
    {
        string text = WithholdingCertificateFormatter.ToPlainText(new WithholdingCertificateDocument
        {
            CompanyName = "One4All Trading",
            CertificateNumber = "WHT-PAY-1",
            SupplierCode = "S1",
            SupplierName = "Builder Ltd",
            PaymentReference = "PAY-1",
            PaymentDate = new DateOnly(2026, 3, 20),
            Currency = "USD",
            Gross = 1_000m,
            RatePercent = 30m,
            Withheld = 300m,
        });

        string[] lines = text.Split('\n', StringSplitOptions.TrimEntries);
        lines.Should().Contain("WITHHOLDING TAX CERTIFICATE");
        lines.Single(l => l.StartsWith("Certificate", StringComparison.Ordinal)).Should().EndWith("WHT-PAY-1");
        lines.Single(l => l.StartsWith("Gross amount (USD)", StringComparison.Ordinal)).Should().EndWith("1,000.00");
        lines.Single(l => l.StartsWith("Tax withheld at 30%", StringComparison.Ordinal)).Should().EndWith("300.00");
        lines.Single(l => l.StartsWith("Net amount paid", StringComparison.Ordinal)).Should().EndWith("700.00");
    }
}
