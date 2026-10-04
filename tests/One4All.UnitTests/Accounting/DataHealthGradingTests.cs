using FluentAssertions;
using One4All.Application.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

public class DataHealthGradingTests
{
    [Theory]
    [InlineData(HealthIndicatorKind.UnbalancedBatches, HealthStatus.Error)]
    [InlineData(HealthIndicatorKind.ControlVariances, HealthStatus.Error)]
    [InlineData(HealthIndicatorKind.NegativeStock, HealthStatus.Error)]
    [InlineData(HealthIndicatorKind.UnpostedBatches, HealthStatus.Warning)]
    [InlineData(HealthIndicatorKind.OrphanRecords, HealthStatus.Warning)]
    [InlineData(HealthIndicatorKind.LedgerIntegrity, HealthStatus.Warning)]
    public void Findings_are_graded_by_kind(HealthIndicatorKind kind, HealthStatus expected)
    {
        DataHealthGrading.Grade(kind, 3).Should().Be(expected);
        DataHealthGrading.Grade(kind, 0).Should().Be(HealthStatus.Ok);
    }

    [Fact]
    public void Integrity_errors_outrank_warnings()
    {
        DataHealthGrading.Grade(HealthIndicatorKind.LedgerIntegrity, 1, hasErrors: true).Should().Be(HealthStatus.Error);
    }

    [Fact]
    public void The_dashboard_is_as_bad_as_its_worst_indicator()
    {
        DataHealthGrading.Overall([HealthStatus.Ok, HealthStatus.Warning, HealthStatus.Ok]).Should().Be(HealthStatus.Warning);
        DataHealthGrading.Overall([HealthStatus.Warning, HealthStatus.Error]).Should().Be(HealthStatus.Error);
        DataHealthGrading.Overall([]).Should().Be(HealthStatus.Ok);
    }
}
