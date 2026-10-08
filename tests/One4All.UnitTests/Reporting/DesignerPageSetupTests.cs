using FluentAssertions;
using One4All.Reporting.Design;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers changing a report's page setup in the designer: one undoable step, elements kept inside the narrower page.</summary>
public sealed class DesignerPageSetupTests
{
    [Fact]
    public void Page_setup_changes_in_one_undoable_step_and_keeps_elements_on_the_page()
    {
        ReportDefinition def = new()
        {
            Code = "PG", Name = "Page",
            Bands = [new ReportBand { Type = BandType.Detail, Height = 20, Elements = [new ReportElement { Kind = ElementKind.Label, Name = "far", Text = "x", X = 480, Width = 40, Height = 12 }] }],
        };
        ReportDesignSession session = new(def);

        session.SetPage(new PageSetup { PaperSize = PaperSize.Receipt80, MarginLeft = 4, MarginRight = 4, Columns = 1, Tray = "Tractor", Duplex = DuplexMode.LongEdge });
        session.Definition.Page.PaperSize.Should().Be(PaperSize.Receipt80);
        session.Definition.Page.Tray.Should().Be("Tractor");
        ReportElement far = session.Definition.Bands[0].Elements[0];
        (far.X + far.Width).Should().BeLessThanOrEqualTo(session.PrintableWidth, "an element is kept on the narrower roll");

        session.Undo();
        session.Definition.Page.PaperSize.Should().Be(PaperSize.A4);
        session.Definition.Bands[0].Elements[0].X.Should().Be(480);
    }
}
