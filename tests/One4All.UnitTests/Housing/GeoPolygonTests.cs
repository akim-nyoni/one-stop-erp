using System.Globalization;
using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class GeoPolygonTests
{
    // A 20 m × 30 m stand near Harare (17.8° S, 31.05° E).
    private static string Rectangle(double west = 31.05, double south = -17.8, double widthMetres = 20, double heightMetres = 30)
    {
        double dLat = heightMetres / 111_195.08;
        double dLon = widthMetres / (111_195.08 * Math.Cos((south + (dLat / 2)) * Math.PI / 180));
        string P(double lon, double lat) => $"{lon.ToString("R", CultureInfo.InvariantCulture)} {lat.ToString("R", CultureInfo.InvariantCulture)}";
        return $"POLYGON(({P(west, south)}, {P(west + dLon, south)}, {P(west + dLon, south + dLat)}, {P(west, south + dLat)}, {P(west, south)}))";
    }

    [Fact]
    public void A_rectangle_measures_its_area_and_centre()
    {
        GeoPolygon.TryParse(Rectangle(), out GeoBoundary? boundary).Should().BeNull();
        ((double)boundary!.AreaSquareMetres).Should().BeApproximately(600, 1);
        boundary.Centroid.Latitude.Should().BeApproximately(-17.8 + (15 / 111_195.08), 1e-6);
        boundary.Ring.Should().HaveCount(5);
    }

    [Fact]
    public void Winding_order_does_not_change_the_area()
    {
        GeoPolygon.TryParse("POLYGON((0 0, 0 0.001, 0.001 0.001, 0.001 0, 0 0))", out GeoBoundary? clockwise);
        GeoPolygon.TryParse("POLYGON((0 0, 0.001 0, 0.001 0.001, 0 0.001, 0 0))", out GeoBoundary? anticlockwise);
        clockwise!.AreaSquareMetres.Should().Be(anticlockwise!.AreaSquareMetres);
    }

    [Theory]
    [InlineData("LINESTRING(0 0, 1 1)", "WKT POLYGON")]
    [InlineData("POLYGON((0 0, 1 0, 1 1, 0 1))", "close on its first point")]
    [InlineData("POLYGON((0 0, 1 0, 0 0))", "at least three corners")]
    [InlineData("POLYGON((0 0, 1 1, 2 2, 0 0))", "encloses no area")]
    [InlineData("POLYGON((200 0, 1 0, 1 1, 200 0))", "outside the world")]
    [InlineData("POLYGON((0 0, x 0, 1 1, 0 0))", "not a longitude/latitude pair")]
    public void Bad_boundaries_are_explained(string wkt, string reason)
    {
        GeoPolygon.TryParse(wkt, out GeoBoundary? boundary).Should().Contain(reason);
        boundary.Should().BeNull();
    }

    [Fact]
    public void The_discrepancy_is_a_percentage_of_the_surveyed_size()
    {
        GeoPolygon.DiscrepancyPercent(630m, 600m).Should().Be(5m);
        GeoPolygon.DiscrepancyPercent(570m, 600m).Should().Be(-5m);
    }

    [Fact]
    public void GeoJson_carries_valid_features_and_escapes_text()
    {
        string json = GeoPolygon.ToGeoJson(
        [
            new MapFeature(7, "Stand \"7\"", "POLYGON((0 0, 0.001 0, 0.001 0.001, 0 0))", new Dictionary<string, string> { ["status"] = "Available" }),
            new MapFeature(8, "Broken", "POLYGON((0 0))", new Dictionary<string, string>()),
        ]);
        json.Should().StartWith("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"id\":7,")
            .And.Contain("\"coordinates\":[[[0,0],[0.001,0],[0.001,0.001],[0,0]]]")
            .And.Contain("\"label\":\"Stand \\\"7\\\"\"").And.Contain("\"status\":\"Available\"")
            .And.NotContain("Broken");
    }
}
