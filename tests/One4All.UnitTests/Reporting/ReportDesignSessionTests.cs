using FluentAssertions;
using One4All.Reporting;
using One4All.Reporting.Design;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the banded designer's model: elements added and moved on the snap grid and kept inside their band and the printable width,
/// alignment, equal sizing and distribution, typed property edits with refusals, band heights, themes, duplicate and delete, every change
/// undoable and redoable, and the expression preview against a sample row.
/// </summary>
public sealed class ReportDesignSessionTests
{
    private static ReportDesignSession Session() => new(new ReportDefinition { Code = "T", Name = "Test" }, grid: 4);

    [Fact]
    public void Elements_snap_and_stay_inside_their_band_and_page()
    {
        ReportDesignSession s = Session();
        string field = s.AddElement(BandType.Detail, ElementKind.Field, 13, 3, "Amount");
        (ReportElement? e, ReportBand? band) = s.Find(field);
        (e!.X, e.Y, e.FieldName, band!.Type).Should().Be((12d, 4d, "Amount", BandType.Detail));
        s.PrintableWidth.Should().Be(595 - 72);

        s.Move([field], 1000, 50);
        (e = s.Find(field).Element!).X.Should().Be(s.PrintableWidth - e.Width, "kept on the page");
        e.Y.Should().Be(band.Height - e.Height, "kept in the band");
        s.Resize(field, 37, 30);
        (s.Find(field).Element!.Width, s.Find(field).Element!.Height, s.Find(field).Band!.Height).Should().Be((36d, 32d, 32d), "a taller element grows its band");
    }

    [Fact]
    public void Alignment_sizing_and_distribution_follow_the_first_selected()
    {
        ReportDesignSession s = Session();
        string a = s.AddElement(BandType.Detail, ElementKind.Label, 40, 0, "A");
        string b = s.AddElement(BandType.Detail, ElementKind.Label, 100, 4, "B");
        string c = s.AddElement(BandType.Detail, ElementKind.Label, 300, 0, "C");
        s.Resize(b, 60, 16);

        s.Align([a, b], DesignAlignment.Right);
        s.Find(b).Element!.X.Should().Be(40 + 100 - 60);
        s.Align([a, b], DesignAlignment.Top);
        s.Find(b).Element!.Y.Should().Be(0);
        s.SameSize([a, b], width: true, height: false);
        s.Find(b).Element!.Width.Should().Be(100);

        s.Move([b], 60, 0);
        s.DistributeHorizontally([c, a, b]);
        double[] xs = [s.Find(a).Element!.X, s.Find(b).Element!.X, s.Find(c).Element!.X];
        (xs[1] - (xs[0] + 100)).Should().BeApproximately(xs[2] - (xs[1] + 100), 0.01, "equal gaps");
    }

    [Fact]
    public void Properties_are_typed_and_checked()
    {
        ReportDesignSession s = Session();
        string field = s.AddElement(BandType.Detail, ElementKind.Field, 0, 0, "Amount");
        s.SetProperty(field, "FontSize", "11.5").Should().BeNull();
        s.SetProperty(field, "Bold", "true").Should().BeNull();
        s.SetProperty(field, "HAlign", "right").Should().BeNull();
        s.SetProperty(field, "Format", "N2").Should().BeNull();
        s.SetProperty(field, "DrillTarget", "Journal").Should().BeNull();
        (s.GetProperty(field, "FontSize"), s.GetProperty(field, "Bold"), s.GetProperty(field, "HAlign")).Should().Be(("11.5", "True", "Right"));

        s.SetProperty(field, "FontSize", "big").Should().Contain("not a valid");
        s.SetProperty(field, "Width", "-5").Should().Contain("negative");
        s.SetProperty(field, "FontFamily", "").Should().Contain("needs a value");
        s.SetProperty(field, "ImageData", "x").Should().Contain("cannot be edited");
        s.SetProperty("Nope", "Text", "x").Should().Contain("No element");
        s.SetProperty(field, "Format", " ").Should().BeNull();
        s.GetProperty(field, "Format").Should().BeEmpty();
    }

    [Fact]
    public void Every_change_undoes_and_redoes()
    {
        ReportDesignSession s = Session();
        s.CanUndo.Should().BeFalse();
        string label = s.AddElement(BandType.ReportHeader, ElementKind.Label, 0, 0, "Title");
        s.Move([label], 40, 0);
        s.SetProperty(label, "Text", "Annual Report").Should().BeNull();

        s.Undo().Should().BeTrue();
        s.Find(label).Element!.Text.Should().Be("Title");
        s.Undo().Should().BeTrue();
        s.Find(label).Element!.X.Should().Be(0);
        s.Redo().Should().BeTrue();
        s.Find(label).Element!.X.Should().Be(40);
        s.Delete([label]);
        s.Find(label).Element.Should().BeNull();
        s.CanRedo.Should().BeFalse("a new change clears the redo stack");
        s.Undo();
        s.Find(label).Element.Should().NotBeNull("selections by name survive undo");
    }

    [Fact]
    public void Bands_themes_and_duplicates()
    {
        ReportDesignSession s = new(SystemReports.JournalDaybook());
        s.Definition.Bands.SelectMany(b => b.Elements).Select(e => e.Name).Should().OnlyHaveUniqueItems().And.NotContain(string.Empty);
        s.Definition.Bands.Select(b => b.Type).Should().BeInAscendingOrder();

        string header = s.Definition.BandOfType(BandType.PageHeader)!.Elements[0].Name;
        s.ApplyTheme(ReportTheme.BuiltIn.Single(t => t.Name == "Council"));
        s.Find(header).Element!.Should().Match<ReportElement>(e => e.FontFamily == "Calibri" && e.Background == "#1A3A6E" && e.Foreground == "#FFFFFF");

        s.SetBandHeight(BandType.Detail, 6);
        s.Definition.BandOfType(BandType.Detail)!.Elements.Should().OnlyContain(e => e.Y + e.Height <= s.Definition.BandOfType(BandType.Detail)!.Height);

        IReadOnlyList<string> copies = s.Duplicate([header]);
        s.Find(copies.Single()).Element!.Should().Match<ReportElement>(e => e.Text == s.Find(header).Element!.Text && e.Name != header);
        s.AddBand(BandType.PageHeader).Should().BeSameAs(s.Definition.BandOfType(BandType.PageHeader), "one page header per report");
    }

    [Fact]
    public void Expressions_preview_against_a_sample_row_and_fields_are_offered()
    {
        Dictionary<string, object?> row = new() { ["Amount"] = 120.5m, ["Name"] = "Acme" };
        ReportDesignSession.Preview("Amount * 2", row).Should().Be("241.0");
        ReportDesignSession.Preview("Amount *", row).Should().StartWith("Error:");

        ReportDesignSession s = Session();
        s.AddElement(BandType.Detail, ElementKind.Field, 0, 0, "Reference");
        s.Fields(row).Should().Equal("Amount", "Name", "Reference");
        s.Fields(null).Should().Equal("Reference");
    }

    [Fact]
    public void Copied_elements_paste_into_another_report_renamed_and_stepped()
    {
        ReportDesignSession from = Session();
        string a = from.AddElement(BandType.Detail, ElementKind.Field, 0, 0, "Amount");
        string b = from.AddElement(BandType.Detail, ElementKind.Label, 100, 0, "Total");
        from.SetProperty(b, nameof(ReportElement.Bold), "True");
        string clipboard = from.Copy([a, b]);
        clipboard.Should().Contain("Amount").And.Contain("Total");
        from.Copy([]).Should().BeEmpty();

        ReportDesignSession to = Session();
        IReadOnlyList<string> pasted = to.Paste(BandType.PageHeader, clipboard);
        pasted.Should().HaveCount(2);
        List<ReportElement> elements = [.. pasted.Select(n => to.Find(n).Element!)];
        elements.Select(e => e.Name).Should().OnlyHaveUniqueItems();
        elements[0].Should().Match<ReportElement>(e => e.FieldName == "Amount" && e.X == 8 && e.Y == 4, "stepped off its place by two grid units, then kept inside the 20 pt band");
        elements[1].Should().Match<ReportElement>(e => e.Text == "Total" && e.Bold);
        to.Find(pasted[0]).Band!.Type.Should().Be(BandType.PageHeader);
        to.Paste(BandType.Detail, "not a copy").Should().BeEmpty();
        to.Undo().Should().BeTrue();
        to.Definition.BandOfType(BandType.PageHeader)!.Elements.Should().BeEmpty("pasting is one undoable step");
    }

    [Fact]
    public void Dragging_snaps_to_the_edges_and_centres_of_the_other_elements()
    {
        DesignRect moving = new(0, 0, 50, 10);
        DesignRect[] others = [new(102, 40, 60, 10), new(300, 0, 20, 10)];

        GuideSnap near = DesignGuides.Snap(moving, 100, 29, others);
        (near.Dx, near.Dy).Should().Be((102d, 30d), "the left edge is within 3 pt of the other's left edge, and the top within 3 pt of its bottom");
        near.VerticalGuides.Should().Equal(102);
        near.HorizontalGuides.Should().Equal(40d); // the guide is drawn on the edge reached: the other's top

        GuideSnap far = DesignGuides.Snap(moving, 40, 15, others);
        (far.Dx, far.Dy, far.VerticalGuides.Count, far.HorizontalGuides.Count).Should().Be((40d, 15d, 0, 0), "nothing is within 3 pt");

        GuideSnap centre = DesignGuides.Snap(moving, 106, 0, others);
        centre.Dx.Should().Be(107, "the moving centre (131) lines up with the other's centre (132)");
    }

    [Fact]
    public void Elements_can_be_brought_to_the_front_or_sent_to_the_back()
    {
        ReportDesignSession session = new(new ReportDefinition { Code = "Z", Bands = [new ReportBand { Type = BandType.Detail, Height = 20 }] });
        string a = session.AddElement(BandType.Detail, ElementKind.Label, 0, 0, "A");
        string b = session.AddElement(BandType.Detail, ElementKind.Label, 0, 0, "B");
        string c = session.AddElement(BandType.Detail, ElementKind.Label, 0, 0, "C");
        List<string> Order() => [.. session.Definition.Bands.Single().Elements.Select(e => e.Name)];

        session.Restack([a], toFront: true);
        Order().Should().Equal(b, c, a);
        session.Restack([c, a], toFront: false);
        Order().Should().Equal(c, a, b);
        session.Undo().Should().BeTrue();
        Order().Should().Equal(b, c, a);
    }
}
