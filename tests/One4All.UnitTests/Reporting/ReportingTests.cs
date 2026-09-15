using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Expressions;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

public class ReportingTests
{
    private static ReportDefinition SampleDefinition()
    {
        ReportDefinition def = new() { Code = "GL-TB", Name = "Trial Balance", Module = "GeneralLedger" };
        def.DataSources.Add(new ReportDataSource { Name = "Main", Kind = DataSourceKind.InMemory, Command = "TrialBalance" });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportHeader,
            Height = 24,
            Elements = [new ReportElement { Kind = ElementKind.Label, Name = "title", Text = "Trial Balance", Bold = true, FontSize = 14, Width = 300 }],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.Detail,
            Height = 16,
            Elements =
            [
                new ReportElement { Kind = ElementKind.Field, Name = "code", FieldName = "AccountCode", X = 0, Width = 80 },
                new ReportElement { Kind = ElementKind.Field, Name = "name", FieldName = "AccountName", X = 90, Width = 200 },
                new ReportElement { Kind = ElementKind.Field, Name = "debit", FieldName = "Debit", X = 300, Width = 80, Format = "N2", HAlign = ReportHAlign.Right },
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportFooter,
            Height = 20,
            Elements =
            [
                new ReportElement { Kind = ElementKind.Label, Name = "totalLabel", Text = "Total", Bold = true, X = 90, Width = 100 },
                new ReportElement { Kind = ElementKind.Aggregate, Name = "totalDebit", Aggregate = AggregateFunction.Sum, AggregateField = "Debit", X = 300, Width = 80, Format = "N2", HAlign = ReportHAlign.Right, Bold = true },
            ],
        });

        return def;
    }

    private static ReportInput SampleInput(ReportDefinition def) => new()
    {
        Definition = def,
        Rows =
        [
            new Dictionary<string, object?> { ["AccountCode"] = "1000", ["AccountName"] = "Cash", ["Debit"] = 55m },
            new Dictionary<string, object?> { ["AccountCode"] = "5000", ["AccountName"] = "Cost of Sales", ["Debit"] = 60m },
        ],
    };

    [Fact]
    public void Definition_round_trips_through_mrpt_json()
    {
        ReportDefinition original = SampleDefinition();
        string json = ReportDefinitionSerializer.Serialize(original);
        ReportDefinition parsed = ReportDefinitionSerializer.Deserialize(json);

        parsed.Code.Should().Be("GL-TB");
        parsed.Bands.Should().HaveCount(3);
        parsed.BandOfType(BandType.Detail)!.Elements.Should().HaveCount(3);
    }

    [Fact]
    public void Html_render_shows_fields_and_aggregate_total()
    {
        ReportDefinition def = SampleDefinition();
        RenderModel model = new ReportEngine().Build(SampleInput(def));
        string html = new HtmlReportRenderer().RenderToString(model);

        html.Should().Contain("Trial Balance");
        html.Should().Contain("Cash");
        html.Should().Contain("55.00");
        html.Should().Contain("115.00"); // aggregate sum of debits
    }

    [Fact]
    public void Csv_export_has_header_and_rows()
    {
        byte[] csv = new CsvReportExporter().Export(SampleInput(SampleDefinition()).Rows);
        string text = System.Text.Encoding.UTF8.GetString(csv);

        text.Should().Contain("AccountCode,AccountName,Debit");
        text.Should().Contain("1000,Cash,55");
    }

    [Fact]
    public void Pdf_render_produces_a_pdf()
    {
        RenderModel model = new ReportEngine().Build(SampleInput(SampleDefinition()));
        byte[] pdf = new PdfReportRenderer().Render(model);

        pdf.Length.Should().BeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Plain_text_render_lays_out_lines()
    {
        RenderModel model = new ReportEngine().Build(SampleInput(SampleDefinition()));
        string text = new PlainTextReportRenderer().RenderToString(model);

        text.Should().Contain("Cash");
        text.Should().Contain("Total");
    }

    [Fact]
    public void Grouped_report_emits_group_headers_and_footers()
    {
        ReportDefinition def = new() { Code = "G", Name = "Grouped" };
        def.Groups.Add(new ReportGroup { Expression = "Dept" });
        def.Bands.Add(new ReportBand { Type = BandType.GroupHeader, GroupLevel = 0, Height = 14, Elements = [new ReportElement { Kind = ElementKind.Field, FieldName = "Dept", Width = 100 }] });
        def.Bands.Add(new ReportBand { Type = BandType.Detail, Height = 12, Elements = [new ReportElement { Kind = ElementKind.Field, FieldName = "Item", Width = 100 }] });
        def.Bands.Add(new ReportBand { Type = BandType.GroupFooter, GroupLevel = 0, Height = 12, Elements = [new ReportElement { Kind = ElementKind.Aggregate, Aggregate = AggregateFunction.Sum, AggregateField = "Amount", Width = 100, Format = "N0" }] });

        ReportInput input = new()
        {
            Definition = def,
            Rows =
            [
                new Dictionary<string, object?> { ["Dept"] = "A", ["Item"] = "x", ["Amount"] = 10m },
                new Dictionary<string, object?> { ["Dept"] = "A", ["Item"] = "y", ["Amount"] = 20m },
                new Dictionary<string, object?> { ["Dept"] = "B", ["Item"] = "z", ["Amount"] = 5m },
            ],
        };

        RenderModel model = new ReportEngine().Build(input);
        int groupHeaders = model.Body.Count(b => b.Type == BandType.GroupHeader);
        int groupFooters = model.Body.Count(b => b.Type == BandType.GroupFooter);

        groupHeaders.Should().Be(2); // dept A and dept B
        groupFooters.Should().Be(2);
        // Dept A footer sums to 30.
        model.Body.First(b => b.Type == BandType.GroupFooter).Cells[0].Text.Should().Be("30");
    }
}
