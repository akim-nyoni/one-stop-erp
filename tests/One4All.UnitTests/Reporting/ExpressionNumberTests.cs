using FluentAssertions;
using One4All.Reporting.Expressions;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers number literals in expressions: a literal with a decimal point is decimal, so a payroll formula such as an AIDS levy of
/// "PAYE * 0.03" multiplies the decimal amount, and whole numbers still serve where a function takes a count (Round's places).
/// </summary>
public sealed class ExpressionNumberTests
{
    private static readonly ReportExpressionEngine Engine = new();

    [Fact]
    public void A_literal_with_a_decimal_point_multiplies_a_decimal_amount()
    {
        object? levy = Engine.Evaluate("PAYE * 0.03", new ReportExpressionContext { Fields = new Dictionary<string, object?> { ["PAYE"] = 1_000m } });
        levy.Should().Be(30m);
    }

    [Fact]
    public void Whole_numbers_still_count_places_and_compare()
    {
        Engine.Evaluate("Round(AMOUNT, 2)", new ReportExpressionContext { Fields = new Dictionary<string, object?> { ["AMOUNT"] = 10.555m } }).Should().Be(10.56m);
        Engine.EvaluateBool("AMOUNT > 100", new ReportExpressionContext { Fields = new Dictionary<string, object?> { ["AMOUNT"] = 150m } }).Should().BeTrue();
    }
}
