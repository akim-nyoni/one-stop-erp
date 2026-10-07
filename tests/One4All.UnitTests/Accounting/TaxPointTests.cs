using FluentAssertions;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers the tax point rules and exemption certificate validity.</summary>
public sealed class TaxPointTests
{
    private static readonly DateOnly Invoice = new(2026, 3, 20);

    [Theory]
    [InlineData(null, null, "2026-03-20")]
    [InlineData("2026-03-10", null, "2026-03-20")]
    [InlineData("2026-02-20", null, "2026-02-20")]
    [InlineData("2026-04-02", null, "2026-03-20")]
    [InlineData("2026-03-10", "2026-03-01", "2026-03-01")]
    [InlineData("2026-03-06", null, "2026-03-20")]
    [InlineData("2026-03-05", null, "2026-03-05")]
    public void The_tax_point_follows_delivery_invoice_and_payment(string? delivery, string? payment, string expected)
        => TaxPoints.Determine(Invoice, delivery is null ? null : DateOnly.Parse(delivery, System.Globalization.CultureInfo.InvariantCulture),
            payment is null ? null : DateOnly.Parse(payment, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Certificates_cover_their_dates_until_revoked()
    {
        TaxExemptionCertificate certificate = new(1, TradingLedger.AccountsReceivable, 7, "EX-1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        certificate.CoversDate(new DateOnly(2026, 6, 1)).Should().BeTrue();
        certificate.CoversDate(new DateOnly(2027, 1, 1)).Should().BeFalse();
        certificate.Revoke(new DateOnly(2026, 7, 1));
        (certificate.CoversDate(new DateOnly(2026, 6, 30)), certificate.CoversDate(new DateOnly(2026, 7, 1))).Should().Be((true, false));
    }
}
