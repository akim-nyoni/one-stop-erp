using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers the head of a long report shown while the rest is laid out.</summary>
public sealed class ReportHeadTests
{
    [Fact]
    public void A_long_report_shows_its_first_bands_without_the_totals()
    {
        RenderModel model = new()
        {
            Page = new PageSetup(), ReportHeader = new RenderBand(), PageHeader = new RenderBand(), ReportFooter = new RenderBand(),
            Body = [.. Enumerable.Range(0, 1000).Select(_ => new RenderBand())],
        };

        RenderModel head = model.Head(300);
        (head.IsHead, head.Body.Count, head.ReportFooter, head.ReportHeader, head.PageHeader).Should().Be((true, 300, (RenderBand?)null, model.ReportHeader, model.PageHeader));
        head.Body[0].Should().BeSameAs(model.Body[0]);
        model.Head(1000).Should().BeSameAs(model, "a short report is shown whole");
        model.Body.Should().HaveCount(1000, "the report itself is untouched");
    }
}
