using FluentAssertions;
using One4All.Application.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class MeterReadingCsvParserTests
{
    [Fact]
    public void Readings_parse_in_any_column_order_with_an_optional_tamper_flag()
    {
        MeterReadingCsvResult result = MeterReadingCsvParser.Parse("Reading,Serial,Date,Tamper\r\n130,w-1,2026-02-01,\r\n20,W-2,01/02/2026,Y\r\n");
        result.Errors.Should().BeEmpty();
        result.Rows.Should().Equal(
            new MeterReadingCsvRow(2, "W-1", new DateOnly(2026, 2, 1), 130m, false),
            new MeterReadingCsvRow(3, "W-2", new DateOnly(2026, 2, 1), 20m, true));
    }

    [Fact]
    public void Bad_lines_are_reported_and_the_rest_kept()
    {
        MeterReadingCsvResult result = MeterReadingCsvParser.Parse("Serial,Date,Reading\n,2026-02-01,5\nW-1,2026-02-xx,5\nW-1,2026-02-01,abc\nW-1,2026-02-01,-4\nW-1,2026-02-01,7.5\n");
        result.Errors.Select(e => e.LineNumber).Should().Equal(2, 3, 4, 5);
        result.Rows.Should().ContainSingle().Which.Reading.Should().Be(7.5m);
    }

    [Fact]
    public void A_header_without_the_required_columns_is_refused()
    {
        MeterReadingCsvParser.Parse("Serial,Value\nW-1,5\n").Errors.Should().ContainSingle().Which.Message.Should().Contain("DATE, READING");
        MeterReadingCsvParser.Parse(string.Empty).Errors.Should().ContainSingle();
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("N", false)]
    public void Tamper_flags_accept_common_spellings(string flag, bool expected)
    {
        MeterReadingCsvParser.Parse($"Serial,Date,Reading,Tamper\nW-1,2026-02-01,5,{flag}\n").Rows.Single().TamperSuspected.Should().Be(expected);
    }
}
