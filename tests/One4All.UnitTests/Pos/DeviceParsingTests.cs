using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers the hardware-side parsing: EAN-13 check digits, embedded weight and price barcodes, ESC/POS receipts and scale replies.</summary>
public sealed class DeviceParsingTests
{
    [Theory]
    [InlineData("6001234567899", true)]
    [InlineData("6001234567898", false)]
    [InlineData("600123456789", false)]
    [InlineData("60012345678AB", false)]
    public void Ean13_check_digits_are_verified(string code, bool valid) => Barcodes.IsValidEan13(code).Should().Be(valid);

    [Fact]
    public void Embedded_barcodes_carry_weight_or_price()
    {
        Barcodes.TryParseEmbedded("2100012012508").Should().Be(new EmbeddedBarcode("12", EmbeddedValueKind.Weight, 1.250m));
        Barcodes.TryParseEmbedded("2200045034994").Should().Be(new EmbeddedBarcode("45", EmbeddedValueKind.Price, 34.99m));
        Barcodes.TryParseEmbedded("6001234567899").Should().BeNull("an ordinary product barcode is looked up as it is");
        Barcodes.TryParseEmbedded("2100012012509").Should().BeNull("a bad check digit is not trusted");
        Barcodes.TryParseEmbedded("2300012005009").Should().BeNull("no template for prefix 23 by default");
        Barcodes.TryParseEmbedded("2300012005009", [new EmbeddedBarcodeRule("23", 5, 5, EmbeddedValueKind.Weight, 3)])!.Value.Should().Be(0.500m);
    }

    [Fact]
    public void Receipts_print_cut_and_open_the_drawer_when_cash_was_taken()
    {
        byte[] withDrawer = EscPosBuilder.Receipt("SHOP\nTotal 9.99", openDrawer: true);
        withDrawer.Take(2).Should().Equal(0x1B, (byte)'@');
        System.Text.Encoding.Latin1.GetString(withDrawer).Should().Contain("SHOP\nTotal 9.99\n");
        withDrawer.TakeLast(5).Should().Equal(0x1B, (byte)'p', 0, 25, 250);
        byte[] noDrawer = EscPosBuilder.Receipt("SHOP", openDrawer: false);
        noDrawer.TakeLast(4).Should().Equal(0x1D, (byte)'V', 66, 3);
        new EscPosBuilder().Bold(true).Align(1).DoubleSize(true).ToArray().Should().Equal(0x1B, (byte)'@', 0x1B, (byte)'E', 1, 0x1B, (byte)'a', 1, 0x1D, (byte)'!', 0x11);
    }

    [Theory]
    [InlineData("ST,GS,  1.234kg", 1.234, true)]
    [InlineData("US,GS,  0.800kg", 0.800, false)]
    [InlineData("  250 g", 0.250, true)]
    [InlineData("2.00 lb", 0.907, true)]
    [InlineData("0.5", 0.5, true)]
    public void Scale_replies_become_kilograms(string reply, double kilograms, bool stable)
        => ScaleResponse.TryParse(reply).Should().Be(new ScaleReading((decimal)kilograms, stable));

    [Fact]
    public void A_reply_without_a_weight_is_no_reading() => ScaleResponse.TryParse("ERR").Should().BeNull();
}
