using FluentAssertions;
using One4All.Reporting.Expressions;
using Xunit;

namespace One4All.UnitTests.Reporting;

public class ExpressionEngineTests
{
    private readonly ReportExpressionEngine _engine = new();

    private static ReportExpressionContext Ctx(Dictionary<string, object?>? fields = null, Dictionary<string, object?>? parameters = null) => new()
    {
        Fields = fields ?? [],
        Parameters = parameters ?? [],
        UserName = "tester",
        CompanyName = "One4All",
    };

    [Theory]
    [InlineData("Upper(\"abc\")", "ABC")]
    [InlineData("Left(\"abcdef\", 3)", "abc")]
    [InlineData("Right(\"abcdef\", 2)", "ef")]
    [InlineData("Round(1.2345m, 2)", 1.23)]
    [InlineData("Abs(-5m)", 5.0)]
    [InlineData("If(1 > 0, \"yes\", \"no\")", "yes")]
    public void Evaluates_library_functions(string expression, object expected)
    {
        object? result = _engine.Evaluate(expression, Ctx());
        result!.ToString().Should().Be(expected.ToString());
    }

    [Fact]
    public void Field_and_param_access()
    {
        ReportExpressionContext ctx = Ctx(
            fields: new Dictionary<string, object?> { ["Qty"] = 3m, ["Price"] = 4m },
            parameters: new Dictionary<string, object?> { ["Vat"] = 0.15m });

        _engine.Evaluate("Qty * Price", ctx).Should().Be(12m);
        _engine.Evaluate("Field(\"Qty\")", ctx).Should().Be(3m);
        _engine.Evaluate("Param(\"Vat\")", ctx).Should().Be(0.15m);
    }

    [Fact]
    public void Page_variables_available()
    {
        ReportExpressionContext ctx = new() { PageNumber = 2, TotalPages = 5, CompanyName = "One4All" };
        _engine.Evaluate("\"Page \" + PageNumber + \" of \" + TotalPages", ctx).Should().Be("Page 2 of 5");
        _engine.Evaluate("CompanyName", ctx).Should().Be("One4All");
    }

    [Fact]
    public void Number_to_words()
    {
        NumberToWords.Convert(115).Should().Be("One Hundred And Fifteen");
        NumberToWords.Convert(1000).Should().Be("One Thousand");
        NumberToWords.Amount(115.05m, "Dollars", "Cents").Should().Be("One Hundred And Fifteen Dollars and Five Cents");
    }

    [Fact]
    public void To_base_helper_invoked()
    {
        ReportExpressionContext ctx = new()
        {
            ToBase = (amount, currency, _) => currency == "EUR" ? amount * 1.1m : amount,
        };

        _engine.Evaluate("ToBase(100m, \"EUR\", PrintDate)", ctx).Should().Be(110.0m);
    }
}
