using FluentAssertions;
using One4All.Application.Communications;
using Xunit;

namespace One4All.UnitTests.Communications;

/// <summary>Covers filling an SMS provider's request and normalising numbers.</summary>
public sealed class SmsRequestTests
{
    [Fact]
    public void Json_and_form_templates_are_escaped_for_their_content_type()
    {
        SmsRequest.Fill("{\"to\":\"{to}\",\"text\":\"{body}\"}", "application/json", "+263770000001", "Fees \"due\"\nnow", "ONE4ALL")
            .Should().Be("{\"to\":\"+263770000001\",\"text\":\"Fees \\\"due\\\"\\nnow\"}");
        SmsRequest.Fill("to={to}&text={body}&from={from}", "application/x-www-form-urlencoded", "+263770000001", "Pay 50 & thanks", "SCH")
            .Should().Be("to=%2B263770000001&text=Pay%2050%20%26%20thanks&from=SCH");
    }

    [Theory]
    [InlineData("+263 77 000 0001", "+263770000001")]
    [InlineData("077-000-0001", "0770000001")]
    [InlineData("12", null)]
    [InlineData("call me", null)]
    public void Numbers_are_normalised(string given, string? expected) => SmsRequest.Number(given).Should().Be(expected);
}
