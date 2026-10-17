using FluentAssertions;
using One4All.Reporting;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Every shipped report has its own code: two definitions under one code overwrite each other when the catalogue is seeded.</summary>
public sealed class ReportCodeUniquenessTests
{
    [Fact]
    public void Every_shipped_report_code_is_unique()
    {
        SystemReportCatalogue.All().GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(" / ", g.Select(r => r.Name))}").Should().BeEmpty();
    }
}
