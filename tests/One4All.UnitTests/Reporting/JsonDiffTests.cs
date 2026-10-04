using FluentAssertions;
using One4All.Application.Reporting;
using Xunit;

namespace One4All.UnitTests.Reporting;

public class JsonDiffTests
{
    [Fact]
    public void Property_order_and_formatting_do_not_matter()
    {
        JsonDiff.Compare("{\"a\":1,\"b\":{\"c\":true}}", "{ \"b\": { \"c\": true },\n \"a\": 1 }").Should().BeEmpty();
    }

    [Fact]
    public void Changed_added_and_removed_values_are_reported_with_their_paths()
    {
        string before = "{\"name\":\"TB\",\"page\":{\"size\":\"A4\"},\"bands\":[{\"h\":10},{\"h\":20}],\"old\":1}";
        string after = "{\"name\":\"TB\",\"page\":{\"size\":\"A3\",\"landscape\":true},\"bands\":[{\"h\":10},{\"h\":25},{\"h\":5}]}";
        JsonDiff.Compare(before, after).Should().Equal(
            new JsonChange("$.page.size", JsonChangeKind.Changed, "\"A4\"", "\"A3\""),
            new JsonChange("$.page.landscape", JsonChangeKind.Added, null, "true"),
            new JsonChange("$.bands[1].h", JsonChangeKind.Changed, "20", "25"),
            new JsonChange("$.bands[2]", JsonChangeKind.Added, null, "{\"h\":5}"),
            new JsonChange("$.old", JsonChangeKind.Removed, "1", null));
    }

    [Fact]
    public void A_value_turning_null_or_changing_type_is_a_change()
    {
        JsonDiff.Compare("{\"a\":1}", "{\"a\":null}").Should().Equal(new JsonChange("$.a", JsonChangeKind.Removed, "1", null));
        JsonDiff.Compare("{\"a\":[1]}", "{\"a\":\"1\"}").Should().Equal(new JsonChange("$.a", JsonChangeKind.Changed, "[1]", "\"1\""));
    }
}
