using FluentAssertions;
using One4All.Application.Reporting;
using Xunit;

namespace One4All.UnitTests.Reporting;

public class ReportParameterTokensTests
{
    private static readonly DateOnly Run = new(2026, 3, 15);

    [Theory]
    [InlineData("@Today", 2026, 3, 15)]
    [InlineData("@yesterday", 2026, 3, 14)]
    [InlineData("@MonthStart", 2026, 3, 1)]
    [InlineData("@MonthEnd", 2026, 3, 31)]
    [InlineData("@PrevMonthStart", 2026, 2, 1)]
    [InlineData("@PrevMonthEnd", 2026, 2, 28)]
    [InlineData("@YearStart", 2026, 1, 1)]
    [InlineData("@PrevYearEnd", 2025, 12, 31)]
    public void Tokens_resolve_against_the_run_date(string token, int year, int month, int day)
    {
        ReportParameterTokens.Resolve(new Dictionary<string, string> { ["AsAt"] = token }, Run)["asat"].Should().Be(new DateOnly(year, month, day));
    }

    [Fact]
    public void Literal_values_keep_their_kind()
    {
        IReadOnlyDictionary<string, object?> values = ReportParameterTokens.Resolve(
            new Dictionary<string, string> { ["From"] = "2026-01-31", ["Top"] = "10", ["Branch"] = "HQ" }, Run);
        (values["From"], values["Top"], values["Branch"]).Should().Be((new DateOnly(2026, 1, 31), 10L, "HQ"));
    }

    [Fact]
    public void Unknown_tokens_are_invalid()
    {
        ReportParameterTokens.IsValid("@LastTuesday").Should().BeFalse();
        ReportParameterTokens.IsValid("@PrevMonthEnd").Should().BeTrue();
        ReportParameterTokens.IsValid("plain").Should().BeTrue();
        FluentActions.Invoking(() => ReportParameterTokens.Resolve(new Dictionary<string, string> { ["X"] = "@Nope" }, Run)).Should().Throw<ArgumentException>();
    }
}
