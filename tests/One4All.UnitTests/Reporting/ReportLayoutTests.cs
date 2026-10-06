using FluentAssertions;
using One4All.Reporting;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the viewer's layout: pages filled to the printable height with the page header and footer on each and "[Page] of [Pages]"
/// numbered, landscape and receipt-roll paper, drill-through targets inferred from field names (or set explicitly) carrying each row's key,
/// search across pages, and the layout surviving its JSON trip to the desktop.
/// </summary>
public sealed class ReportLayoutTests
{
    private static RenderModel Model(int bodyBands, double bandHeight = 20)
    {
        RenderModel model = new()
        {
            ReportName = "Test", Page = new PageSetup { PaperSize = PaperSize.A4, MarginTop = 36, MarginBottom = 36 },
            PageHeader = new RenderBand { Type = BandType.PageHeader, Height = 30, Cells = [new RenderCell { Text = "Header" }] },
            PageFooter = new RenderBand { Type = BandType.PageFooter, Height = 20, Cells = [new RenderCell { Text = "Page [Page] of [Pages]" }] },
        };
        for (int i = 1; i <= bodyBands; i++)
        {
            model.Body.Add(new RenderBand { Type = BandType.Detail, Height = bandHeight, Cells = [new RenderCell { Text = $"Row {i}" }] });
        }

        return model;
    }

    [Fact]
    public void Bands_flow_onto_pages_with_headers_footers_and_numbers()
    {
        // A4 is 842pt tall: 842 - 72 margins - 30 header - 20 footer = 720pt for the body, so 36 rows of 20pt a page.
        IReadOnlyList<LayoutPage> pages = ReportLayout.Paginate(Model(80));
        pages.Should().HaveCount(3);
        pages.Select(p => p.Bands.Count(b => b.Band.Type == BandType.Detail)).Should().Equal(36, 36, 8);
        pages[0].Bands[0].Should().Match<PlacedBand>(b => b.Band.Type == BandType.PageHeader && b.Top == 0);
        pages[0].Bands[1].Top.Should().Be(30);
        pages[1].Bands.Last().Should().Match<PlacedBand>(b => b.Band.Cells[0].Text == "Page 2 of 3" && b.Top == 750);
        ReportLayout.Paginate(Model(0)).Should().ContainSingle();
    }

    [Fact]
    public void Paper_turns_for_landscape_and_rolls_grow_with_the_receipt()
    {
        ReportLayout.PaperSize(new PageSetup { PaperSize = PaperSize.A4, Orientation = ReportOrientation.Landscape }).Should().Be((842d, 595d));
        ReportLayout.PaperSize(new PageSetup { PaperSize = PaperSize.Receipt80, MarginTop = 10, MarginBottom = 10 }, contentHeight: 900).Should().Be((227d, 920d));
        RenderModel receipt = Model(100);
        receipt.Page.PaperSize = PaperSize.Receipt58;
        ReportLayout.Paginate(receipt).Should().ContainSingle("a receipt roll is one long page");
    }

    [Fact]
    public void Values_drill_through_to_their_source()
    {
        DrillThrough.Infer("JournalNumber").Should().Be("Journal");
        DrillThrough.Infer("account_code").Should().Be("Account");
        DrillThrough.Infer("CustomerCode").Should().Be("Customer");
        DrillThrough.Infer("Narrative").Should().BeNull();

        ReportDefinition daybook = SystemReports.JournalDaybook();
        RenderModel model = new ReportEngine().Build(new ReportInput
        {
            Definition = daybook,
            Rows =
            [
                new Dictionary<string, object?> { ["Date"] = new DateOnly(2026, 10, 1), ["JournalNumber"] = "JNL-000041", ["SourceModule"] = "GL", ["Narrative"] = "Rent", ["Amount"] = 500m },
                new Dictionary<string, object?> { ["Date"] = new DateOnly(2026, 10, 2), ["JournalNumber"] = "JNL-000042", ["SourceModule"] = "AR", ["Narrative"] = "Invoice", ["Amount"] = 80m },
            ],
        });
        model.Body.SelectMany(b => b.Cells).Where(c => c.DrillTarget is not null).Select(c => (c.DrillTarget, c.DrillKey))
            .Should().Equal(("Journal", "JNL-000041"), ("Journal", "JNL-000042"));

        ReportElement explicitDrill = new() { Kind = ElementKind.Field, FieldName = "Name", DrillTarget = "Customer", DrillField = "Code" };
        DrillThrough.For(explicitDrill, new Dictionary<string, object?> { ["Name"] = "Acme", ["Code"] = "C001" }).Should().Be(("Customer", "C001"));
        DrillThrough.For(explicitDrill, null).Should().Be((null, null));
    }

    [Fact]
    public void Search_finds_the_page_and_the_layout_survives_json()
    {
        RenderModel model = Model(80);
        IReadOnlyList<LayoutPage> pages = ReportLayout.Paginate(model);
        ReportLayout.Search(pages, "row 40").Should().ContainSingle().Which.Page.Should().Be(2);
        ReportLayout.Search(pages, "nothing like it").Should().BeEmpty();
        ReportLayout.Search(pages, " ").Should().BeEmpty();

        model.Body[0].Cells[0] = new RenderCell { Text = "JNL-1", DrillTarget = "Journal", DrillKey = "JNL-1", Bold = true };
        RenderModel back = ReportLayout.Deserialize(ReportLayout.Serialize(model));
        back.Body.Should().HaveCount(80);
        back.Body[0].Cells[0].Should().Match<RenderCell>(c => c.DrillTarget == "Journal" && c.DrillKey == "JNL-1" && c.Bold);
        back.PageFooter!.Cells[0].Text.Should().Be("Page [Page] of [Pages]");
    }
}
