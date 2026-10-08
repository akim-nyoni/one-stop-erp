using System.Text;
using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>A watermark driven by expression: worked out per run from the parameters and data, the fixed watermark otherwise, and drawn on the PDF.</summary>
public sealed class WatermarkExpressionTests
{
    private static ReportDefinition Definition() => new()
    {
        Code = "WM1", Name = "Watermarked",
        Page = new PageSetup { Watermark = "INTERNAL", WatermarkExpression = "\"Draft\".Equals(Param(\"Status\")) ? \"DRAFT\" : (\"Y\".Equals(Field(\"Secret\")) ? \"CONFIDENTIAL\" : \"\")" },
    };

    [Theory]
    [InlineData("Draft", "N", "DRAFT")]
    [InlineData("Final", "Y", "CONFIDENTIAL")]
    [InlineData("Final", "N", "INTERNAL")]
    public void The_watermark_follows_the_expression_or_falls_back(string status, string secret, string expected)
    {
        ReportDefinition definition = Definition();
        RenderModel model = new ReportEngine().Build(new ReportInput
        {
            Definition = definition, Parameters = new Dictionary<string, object?> { ["Status"] = status }, Rows = [new Dictionary<string, object?> { ["Secret"] = secret }],
        });
        model.Page.Watermark.Should().Be(expected);
        definition.Page.Watermark.Should().Be("INTERNAL");
    }

    [Fact]
    public void A_watermarked_pdf_renders()
    {
        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = Definition(), Parameters = new Dictionary<string, object?> { ["Status"] = "Draft" } });
        Encoding.ASCII.GetString(new PdfReportRenderer().Render(model), 0, 5).Should().Be("%PDF-");
    }
}
