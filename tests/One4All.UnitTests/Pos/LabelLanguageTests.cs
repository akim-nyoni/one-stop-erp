using FluentAssertions;
using One4All.Application.Documents;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Shelf labels in ZPL and EPL with real barcodes, and scale labels whose price-embedded EAN-13 the till reads back.</summary>
public sealed class LabelLanguageTests
{
    private static readonly ShelfLabel Milk = new("MILK", "Milk 1L", 1.25m, "USD", "6001234567899");
    private static readonly ShelfLabel Bread = new("BREAD", "Bread \"brown\"", 0.90m, "USD", "BREAD");

    [Fact]
    public void Zpl_uses_ean13_for_an_ean_and_code128_otherwise()
    {
        string zpl = ShelfLabelFormatter.ToZpl([Milk, Bread]);
        zpl.Should().Contain("^BEN,60,Y,N^FD600123456789^FS").And.Contain("^BCN,60,Y,N,N^FDBREAD^FS").And.Contain("USD 1.25");
        zpl.Split("^XA").Length.Should().Be(3, "one label per item");
    }

    [Fact]
    public void Epl_uses_ean13_for_an_ean_and_escapes_quotes()
    {
        string epl = ShelfLabelFormatter.ToEpl([Milk, Bread]);
        epl.Should().Contain("B40,140,0,E30,2,4,60,B,\"600123456789\"").And.Contain("Bread \\\"brown\\\"").And.Contain("B40,140,0,1,2,4,60,B,\"BREAD\"");
    }

    [Fact]
    public void A_scale_label_carries_a_price_barcode_the_till_reads_back()
    {
        ScaleLabel label = ScaleLabel.For("123", "Beef mince", 0.75m, 12.40m, "USD")!;
        label.Price.Should().Be(9.30m);
        Barcodes.IsValidEan13(label.Barcode).Should().BeTrue();
        label.Barcode.Should().StartWith("2200123" + "00930");
        Barcodes.TryParseEmbedded(label.Barcode).Should().Be(new EmbeddedBarcode("123", EmbeddedValueKind.Price, 9.30m));
        label.ToZpl().Should().Contain("0.750 kg").And.Contain("USD 9.30");
        ScaleLabel.For("1234567", "Too long a PLU", 1m, 1m, "USD").Should().BeNull();
    }
}
