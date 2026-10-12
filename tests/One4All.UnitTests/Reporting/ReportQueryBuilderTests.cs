using FluentAssertions;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers the designer's query builder: a company-filtered SELECT over a reporting view, with checked identifiers.</summary>
public sealed class ReportQueryBuilderTests
{
    [Fact]
    public void Builds_a_company_filtered_query_and_refuses_anything_else()
    {
        ReportQueryBuilder.Build("rpt.sales_by_day", ["sale_date", "total"], [new QueryFilter("sale_date", ">=", "FromDate")], ["sale_date DESC"]).Command
            .Should().Be("sql:SELECT sale_date, total FROM rpt.sales_by_day WHERE company_id = @CompanyId AND sale_date >= @FromDate ORDER BY sale_date DESC");
        ReportQueryBuilder.Build("rpt.x", [], [], []).Command.Should().Be("sql:SELECT * FROM rpt.x WHERE company_id = @CompanyId");
        ReportQueryBuilder.Build("sec.user", [], [], []).Refusal.Should().Contain("reporting view");
        ReportQueryBuilder.Build("rpt.x", ["a; drop table t"], [], []).Refusal.Should().Contain("not a column");
        ReportQueryBuilder.Build("rpt.x", [], [new QueryFilter("a", "OR 1=1 --", "p")], []).Refusal.Should().NotBeNull();
        ReportQueryBuilder.Build("rpt.x", [], [], ["a sideways"]).Refusal.Should().NotBeNull();
    }
}
