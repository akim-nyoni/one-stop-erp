using FluentAssertions;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers PDF417 barcodes (brief §13.1): the symbol drawn for a value reads back as that value, and renders as a PNG in reports.</summary>
public sealed class Pdf417Tests
{
    [Theory]
    [InlineData("INV-2026/0001|USD 1,250.50|2026-10-08")]
    [InlineData("Fiscal 7f3a9c - Zimbabwe Revenue Authority verification code")]
    public void The_symbol_reads_back_as_its_value(string value)
    {
        bool[][] modules = Barcodes.Pdf417Modules(value);
        modules.Length.Should().BeGreaterThan(3);
        modules.Should().OnlyContain(row => row.Length == modules[0].Length);

        // Draw it three pixels a module and read it with ZXing's PDF417 reader.
        const int scale = 3;
        int width = modules[0].Length * scale, height = modules.Length * scale;
        byte[] luminance = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                luminance[(y * width) + x] = modules[y / scale][x / scale] ? (byte)0 : (byte)255;
            }
        }

        ZXing.BinaryBitmap bitmap = new(new ZXing.Common.HybridBinarizer(new ZXing.RGBLuminanceSource(luminance, width, height, ZXing.RGBLuminanceSource.BitmapFormat.Gray8)));
        ZXing.Result? read = new ZXing.PDF417.PDF417Reader().decode(bitmap, new Dictionary<ZXing.DecodeHintType, object> { [ZXing.DecodeHintType.PURE_BARCODE] = true });
        read.Should().NotBeNull();
        read!.Text.Should().Be(value);
    }

    [Fact]
    public void Reports_draw_pdf417_as_a_picture()
    {
        byte[] png = Barcodes.Png("PDF417", "RC35-000123")!;
        png.Take(4).Should().Equal([0x89, 0x50, 0x4E, 0x47]);
        Barcodes.Symbologies.Should().Contain("PDF417");
    }
}
