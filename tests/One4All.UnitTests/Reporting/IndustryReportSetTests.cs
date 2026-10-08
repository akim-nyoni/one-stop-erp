using FluentAssertions;
using One4All.Application.MasterData;
using One4All.Reporting;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Every report an industry features exists in the system report catalogue.</summary>
public sealed class IndustryReportSetTests
{
    [Fact]
    public void Featured_reports_are_in_the_catalogue()
    {
        HashSet<string> codes = [.. SystemReportCatalogue.All().Select(r => r.Code)];
        foreach (Vertical vertical in Enum.GetValues<Vertical>())
        {
            IndustryExtras.For(vertical).FeaturedReports.Should().OnlyContain(code => codes.Contains(code), $"{vertical}'s report set names catalogue reports");
        }
    }
}
