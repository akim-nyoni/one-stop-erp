using FluentAssertions;
using One4All.Reporting;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

public class ReceiptingReportsTests
{
    [Fact]
    public void Catalogue_has_all_53_rc_reports_with_distinct_codes()
    {
        List<ReportDefinition> reports = ReceiptingReports.All().ToList();

        reports.Should().HaveCount(53);
        reports.Select(r => r.Code).Should().OnlyHaveUniqueItems();
        reports.Should().OnlyContain(r => r.Code.StartsWith("RC", StringComparison.Ordinal));
        reports.Should().OnlyContain(r => r.Module == "Receipting");
    }

    [Fact]
    public void Every_report_is_well_formed_and_round_trips_through_the_serializer()
    {
        foreach (ReportDefinition report in ReceiptingReports.All())
        {
            report.Name.Should().NotBeNullOrWhiteSpace();
            report.Description.Should().NotBeNullOrWhiteSpace();
            report.DataSources.Should().ContainSingle().Which.Command.Should().Be(report.Code);
            report.Bands.Should().Contain(b => b.Type == BandType.ReportHeader);

            // A definition must serialize and deserialize without loss of code/name.
            string json = ReportDefinitionSerializer.Serialize(report);
            ReportDefinition roundTripped = ReportDefinitionSerializer.Deserialize(json);
            roundTripped.Code.Should().Be(report.Code);
            roundTripped.Name.Should().Be(report.Name);
        }
    }

    [Fact]
    public void Listing_reports_expose_a_detail_band_of_fields()
    {
        // RC3 (Daily Collection Summary) is a listing — it must have a detail band with bound fields.
        ReportDefinition rc3 = ReceiptingReports.All().Single(r => r.Code == "RC3");
        ReportBand detail = rc3.Bands.Single(b => b.Type == BandType.Detail);
        detail.Elements.Should().Contain(e => e.Kind == ElementKind.Field);
        rc3.Bands.Should().Contain(b => b.Type == BandType.ReportFooter); // money column is totalled
    }
}
