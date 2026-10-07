using System.Text;
using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers pictures in reports: Code 128 (set B, with its checksum) and QR codes drawn as PNG, and the engine placing a static picture, a
/// picture field, the company logo and a barcode of a field — shown as images in HTML and PDF while exports keep the barcode's value.
/// </summary>
public sealed class BarcodeAndImageTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Code128_carries_the_start_data_checksum_and_stop()
    {
        Barcodes.Code128Pattern("A").Should().Be("211214" + "111323" + "131123" + "2331112");
        Barcodes.Code128Pattern("é").Should().BeNull();
        for (char c = ' '; c <= '~'; c++)
        {
            string pattern = Barcodes.Code128Pattern(c.ToString())!;
            pattern.Sum(w => w - '0').Should().Be(11 * 3 + 13, $"'{c}' must be start, symbol, checksum and stop");
        }

        Barcodes.Code128Pattern("INV-2026/0001")!.Sum(w => w - '0').Should().Be((11 * 15) + 13);
    }

    [Fact]
    public void Barcodes_are_pngs()
    {
        byte[] code128 = Barcodes.Png("Code128", "SALE-42")!;
        code128.Take(8).Should().Equal(PngSignature);
        int width = (code128[16] << 24) | (code128[17] << 16) | (code128[18] << 8) | code128[19];
        width.Should().Be(2 * (20 + (11 * 9) + 13), "7 characters plus start and checksum, the stop and two quiet zones");
        Barcodes.Png("QR", "https://verify.example/fiscal?c=SIM-0001-000001")!.Take(8).Should().Equal(PngSignature);
        Barcodes.Png("QR", null).Should().BeNull();
        Barcodes.DataUri([1, 2, 3]).Should().Be("data:image/png;base64,AQID");
    }

    [Fact]
    public void Images_logos_and_barcodes_render()
    {
        byte[] logo = Barcodes.Png("QR", "logo")!;
        ReportDefinition definition = new()
        {
            Code = "IMG", Name = "Images",
            Bands =
            [
                new ReportBand
                {
                    Type = BandType.Detail, Height = 60,
                    Elements =
                    [
                        new ReportElement { Kind = ElementKind.Image, Name = "static", ImageData = Convert.ToBase64String(logo), Width = 50, Height = 50 },
                        new ReportElement { Kind = ElementKind.Image, Name = "logo", Expression = "CompanyLogo", X = 60, Width = 50, Height = 50 },
                        new ReportElement { Kind = ElementKind.Barcode, Name = "code", FieldName = "Number", BarcodeSymbology = "Code128", X = 120, Width = 150, Height = 40 },
                        new ReportElement { Kind = ElementKind.Image, Name = "missing", FieldName = "Photo", X = 280, Width = 50, Height = 50 },
                    ],
                },
            ],
        };
        ReportInput input = new() { Definition = definition, CompanyLogo = logo, Rows = [new Dictionary<string, object?> { ["Number"] = "INV-7", ["Photo"] = null }] };
        RenderModel model = new ReportEngine().Build(input);
        List<RenderCell> cells = model.Body.Single().Cells;
        cells[0].ImageData.Should().StartWith("data:image/png;base64,");
        cells[1].ImageData.Should().Be(Barcodes.DataUri(logo));
        (cells[2].Text, cells[2].ImageData).Should().Be(("INV-7", Barcodes.DataUri(Barcodes.Png("Code128", "INV-7")!)));
        cells[3].ImageData.Should().BeNull();
        new ReportEngine().Build(new ReportInput { Definition = definition, Rows = input.Rows }).Body.Single().Cells[1].ImageData.Should().BeNull();

        string html = Encoding.UTF8.GetString(new HtmlReportRenderer().Render(model));
        html.Should().Contain("<img src=\"data:image/png;base64,").And.Contain("alt=\"INV-7\"");
        byte[] pdf = new PdfReportRenderer().Render(model);
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(new PdfReportRenderer().Render(new ReportEngine().Build(new ReportInput { Definition = definition, CompanyLogo = logo })).Length);
    }
}
