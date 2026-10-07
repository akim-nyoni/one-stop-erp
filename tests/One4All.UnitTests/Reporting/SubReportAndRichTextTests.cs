using System.Text;
using FluentAssertions;
using One4All.Reporting.Design;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers rich text (markup into runs, placeholders filled but never read as markup, HTML and PDF output) and sub-reports (parameters
/// linked from the parent row, the child laid into the element and the band grown, a missing child noted, nesting capped, links edited
/// in the designer and kept through serialisation).
/// </summary>
public sealed class SubReportAndRichTextTests
{
    private static ReportDefinition Definition(string code, double height, params ReportElement[] detail) => new()
    {
        Code = code, Name = code, Bands = [new ReportBand { Type = BandType.Detail, Height = height, Elements = [.. detail] }],
    };

    [Fact]
    public void Markup_becomes_runs()
    {
        IReadOnlyList<RichRun> runs = RichText.Parse("Dear {Name},\\n**Total:** *{Amount}* __due__ {Missing} {not a field}", f => f switch { "Name" => "**Ann**", "Amount" => 12.5m, _ => null }, "0.00");
        runs.Should().Equal(
            new RichRun("Dear "), new RichRun("**Ann**"), new RichRun(","), new RichRun(string.Empty, LineBreak: true), new RichRun("Total:", Bold: true), new RichRun(" "),
            new RichRun("12.50", Italic: true), new RichRun(" "), new RichRun("due", Underline: true), new RichRun(" "), new RichRun(string.Empty), new RichRun(" {not a field}"));
        RichText.Plain(runs).Should().Be("Dear **Ann**,\nTotal: 12.50 due  {not a field}");
        RichText.Parse(null, _ => null).Should().BeEmpty();
        RichText.Parse("2 * 3 = 6", _ => null).Select(r => (r.Text, r.Italic)).Should().Equal(("2 ", false), (" 3 = 6", true));
    }

    [Fact]
    public void Rich_text_renders_with_emphasis()
    {
        ReportElement note = new() { Kind = ElementKind.RichText, Name = "note", Text = "Hello **{Who}** & <you>", Width = 200, Height = 30 };
        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = Definition("R", 30, note), Rows = [new Dictionary<string, object?> { ["Who"] = "Ann" }] });
        RenderCell cell = model.Body.Single().Cells.Single();
        cell.Text.Should().Be("Hello Ann & <you>");
        cell.Runs.Should().Contain(new RichRun("Ann", Bold: true));
        string html = Encoding.UTF8.GetString(new HtmlReportRenderer().Render(model));
        html.Should().Contain("Hello <strong>Ann</strong> &amp; &lt;you&gt;");
        Encoding.ASCII.GetString(new PdfReportRenderer().Render(model), 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void A_sub_report_runs_per_row_with_linked_parameters_and_grows_the_band()
    {
        ReportDefinition child = Definition("LINES", 12, new ReportElement { Kind = ElementKind.Field, Name = "item", FieldName = "Item", X = 0, Y = 0, Width = 80, Height = 12 });
        Dictionary<string, List<string>> items = new() { ["A"] = ["a1", "a2", "a3"], ["B"] = ["b1"] };
        List<(string Code, IReadOnlyDictionary<string, object?> Parameters)> calls = [];
        ReportInput? Resolve(string code, IReadOnlyDictionary<string, object?> parameters)
        {
            calls.Add((code, parameters));
            return code == "LINES" ? new ReportInput
            {
                Definition = child, Parameters = parameters,
                Rows = [.. items[(string)parameters["Order"]!].Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["Item"] = i })],
            } : null;
        }

        ReportElement sub = new() { Kind = ElementKind.SubReport, Name = "lines", SubReportCode = "LINES", X = 20, Y = 16, Width = 200, Height = 20, SubReportLinks = [new SubReportLink { ChildParameter = "Order", ParentField = "Number" }] };
        ReportElement missing = new() { Kind = ElementKind.SubReport, Name = "other", SubReportCode = "NOPE", X = 300, Y = 0, Width = 100, Height = 12 };
        ReportDefinition parent = Definition("ORDERS", 20, new ReportElement { Kind = ElementKind.Field, Name = "no", FieldName = "Number", Width = 60, Height = 14 }, sub, missing);
        RenderModel model = new ReportEngine().Build(new ReportInput
        {
            Definition = parent, Parameters = new Dictionary<string, object?> { ["Year"] = 2026 }, SubReports = Resolve,
            Rows = [new Dictionary<string, object?> { ["Number"] = "A" }, new Dictionary<string, object?> { ["Number"] = "B" }],
        });

        model.Body.Should().HaveCount(2);
        model.Body[0].Cells.Where(c => c.Text.StartsWith('a')).Select(c => (c.Text, c.X, c.Y)).Should().Equal(("a1", 20d, 16d), ("a2", 20d, 28d), ("a3", 20d, 40d));
        model.Body[0].Height.Should().Be(52);
        model.Body[1].Height.Should().Be(28);
        model.Body[1].Cells.Should().Contain(c => c.Text == "(sub-report NOPE not available)" && c.Italic);
        calls.Where(c => c.Code == "LINES").Select(c => (c.Parameters["Order"], c.Parameters["Year"])).Should().Equal(("A", 2026), ("B", 2026));
        SubReports.Key("lines", new Dictionary<string, object?> { ["order"] = "A", ["Year"] = 2026 }).Should().Be(SubReports.Key("LINES", calls[0].Parameters));

        // Without a resolver a sub-report is only noted.
        new ReportEngine().Build(new ReportInput { Definition = parent, Rows = [new Dictionary<string, object?> { ["Number"] = "A" }] })
            .Body[0].Cells.Count(c => c.Text.Contains("not available", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public void Nesting_stops_at_the_limit()
    {
        ReportElement self = new() { Kind = ElementKind.SubReport, Name = "self", SubReportCode = "SELF", Y = 10, Height = 10 };
        ReportDefinition recursive = Definition("SELF", 10, new ReportElement { Kind = ElementKind.Label, Name = "l", Text = "level", Height = 10 }, self);
        int resolved = 0;
        ReportInput Resolve(string code, IReadOnlyDictionary<string, object?> parameters)
        {
            resolved++;
            return new ReportInput { Definition = recursive, Rows = [new Dictionary<string, object?>()] };
        }

        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = recursive, Rows = [new Dictionary<string, object?>()], SubReports = Resolve });
        resolved.Should().Be(SubReports.MaxDepth);
        model.Body[0].Cells.Count(c => c.Text == "level").Should().Be(SubReports.MaxDepth + 1);
        model.Body[0].Cells.Should().ContainSingle(c => c.Text.Contains("not available", StringComparison.Ordinal));
    }

    [Fact]
    public void Links_are_edited_in_the_designer_and_survive_serialisation()
    {
        ReportDesignSession session = new(Definition("P", 40));
        string name = session.AddElement(BandType.Detail, ElementKind.Label, 0, 0, null);
        session.Find(name).Element!.Kind = ElementKind.SubReport;
        session.SetProperty(name, nameof(ReportElement.SubReportCode), "AR46").Should().BeNull();
        session.SetProperty(name, nameof(ReportElement.SubReportLinks), "RepCode=Rep; FromDate").Should().BeNull();
        session.GetProperty(name, nameof(ReportElement.SubReportLinks)).Should().Be("RepCode=Rep; FromDate=FromDate");
        session.SetProperty(name, nameof(ReportElement.SubReportLinks), "a=b=c").Should().Contain("not Parameter=Field");
        session.SetProperty(name, nameof(ReportElement.SubReportLinks), "x y").Should().NotBeNull();
        session.GetProperty(name, nameof(ReportElement.SubReportLinks)).Should().Be("RepCode=Rep; FromDate=FromDate");

        ReportDefinition copy = ReportDefinitionSerializer.Deserialize(ReportDefinitionSerializer.Serialize(session.Definition));
        ReportElement element = copy.BandOfType(BandType.Detail)!.Elements.Single();
        (element.Kind, element.SubReportCode).Should().Be((ElementKind.SubReport, "AR46"));
        element.SubReportLinks.Select(l => (l.ChildParameter, l.ParentField)).Should().Equal(("RepCode", "Rep"), ("FromDate", "FromDate"));
    }
}
