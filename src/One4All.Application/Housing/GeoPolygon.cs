using System.Globalization;
using System.Text;

namespace One4All.Application.Housing;

/// <summary>A longitude/latitude point (WGS 84 degrees).</summary>
/// <param name="Longitude">Degrees east.</param>
/// <param name="Latitude">Degrees north.</param>
public sealed record GeoPoint(double Longitude, double Latitude);

/// <summary>A boundary checked and measured.</summary>
/// <param name="Ring">The closed outer ring.</param>
/// <param name="Centroid">Its centroid.</param>
/// <param name="AreaSquareMetres">Its area (equirectangular approximation, ample for stand-sized shapes).</param>
public sealed record GeoBoundary(IReadOnlyList<GeoPoint> Ring, GeoPoint Centroid, decimal AreaSquareMetres);

/// <summary>A map feature (one stand, parcel or ward).</summary>
/// <param name="Id">The record's id.</param>
/// <param name="Label">What to show on the map.</param>
/// <param name="BoundaryWkt">Its boundary as WKT.</param>
/// <param name="Properties">Attributes to carry (status, size, scheme...).</param>
public sealed record MapFeature(long Id, string Label, string BoundaryWkt, IReadOnlyDictionary<string, string> Properties);

/// <summary>
/// GIS groundwork (brief §4.10.10 GIS hook): boundaries held as WKT polygons (PostGIS-ready - the same text loads into a
/// geometry column later), checked as closed rings, measured for centroid and area, compared with the surveyed size, and
/// exported as GeoJSON for any map. Pure and unit-tested.
/// </summary>
public static class GeoPolygon
{
    private const double EarthRadiusMetres = 6_371_008.8;

    /// <summary>Reads and measures a WKT polygon; only its outer ring is used.</summary>
    /// <param name="wkt">The WKT, e.g. <c>POLYGON((lon lat, lon lat, ...))</c>.</param>
    /// <param name="boundary">The boundary, when valid.</param>
    /// <returns>What is wrong, or null when valid.</returns>
    public static string? TryParse(string? wkt, out GeoBoundary? boundary)
    {
        boundary = null;
        string text = (wkt ?? string.Empty).Trim();
        if (!text.StartsWith("POLYGON", StringComparison.OrdinalIgnoreCase))
        {
            return "The boundary must be a WKT POLYGON.";
        }

        int open = text.IndexOf("((", StringComparison.Ordinal);
        int close = text.IndexOf(')', open + 2);
        if (open < 0 || close < 0)
        {
            return "The polygon's ring is not enclosed in double brackets.";
        }

        List<GeoPoint> ring = [];
        foreach (string pair in text[(open + 2)..close].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
            {
                return $"\"{pair}\" is not a longitude/latitude pair.";
            }

            if (lon is < -180 or > 180 || lat is < -90 or > 90)
            {
                return $"\"{pair}\" is outside the world's longitudes and latitudes.";
            }

            ring.Add(new GeoPoint(lon, lat));
        }

        if (ring.Count < 4 || ring[0] != ring[^1])
        {
            return "A polygon needs at least three corners and must close on its first point.";
        }

        if (ring.Distinct().Count() < 3)
        {
            return "A polygon needs at least three distinct corners.";
        }

        (GeoPoint centroid, double area) = Measure(ring);
        if (area <= 0d)
        {
            return "The polygon encloses no area.";
        }

        boundary = new GeoBoundary(ring, centroid, Math.Round((decimal)area, 2, MidpointRounding.AwayFromZero));
        return null;
    }

    /// <summary>The difference between a mapped area and the surveyed size, as a percentage of the surveyed size.</summary>
    /// <param name="mappedSquareMetres">The polygon's area.</param>
    /// <param name="surveyedSquareMetres">The size on the register.</param>
    /// <returns>The difference (%), positive when the map shows more.</returns>
    public static decimal DiscrepancyPercent(decimal mappedSquareMetres, decimal surveyedSquareMetres)
        => surveyedSquareMetres <= 0m ? 0m : Math.Round((mappedSquareMetres - surveyedSquareMetres) / surveyedSquareMetres * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>A GeoJSON FeatureCollection of features (coordinates as lon, lat).</summary>
    /// <param name="features">The features.</param>
    /// <returns>The GeoJSON text.</returns>
    public static string ToGeoJson(IEnumerable<MapFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        StringBuilder json = new("{\"type\":\"FeatureCollection\",\"features\":[");
        bool first = true;
        foreach (MapFeature feature in features)
        {
            if (TryParse(feature.BoundaryWkt, out GeoBoundary? boundary) is not null)
            {
                continue;
            }

            json.Append(first ? string.Empty : ",");
            first = false;
            json.Append("{\"type\":\"Feature\",\"id\":").Append(feature.Id.ToString(CultureInfo.InvariantCulture))
                .Append(",\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[")
                .Append(string.Join(",", boundary!.Ring.Select(p => $"[{p.Longitude.ToString("R", CultureInfo.InvariantCulture)},{p.Latitude.ToString("R", CultureInfo.InvariantCulture)}]")))
                .Append("]]},\"properties\":{\"label\":").Append(Quote(feature.Label));
            foreach (KeyValuePair<string, string> property in feature.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                json.Append(',').Append(Quote(property.Key)).Append(':').Append(Quote(property.Value));
            }

            json.Append("}}");
        }

        return json.Append("]}").ToString();
    }

    // Shoelace on an equirectangular projection about the ring's mean latitude.
    private static (GeoPoint Centroid, double Area) Measure(List<GeoPoint> ring)
    {
        double meanLat = ring.Take(ring.Count - 1).Average(p => p.Latitude) * Math.PI / 180d;
        double ox = ring[0].Longitude;
        double oy = ring[0].Latitude;
        double X(GeoPoint p) => (p.Longitude - ox) * Math.PI / 180d * EarthRadiusMetres * Math.Cos(meanLat);
        double Y(GeoPoint p) => (p.Latitude - oy) * Math.PI / 180d * EarthRadiusMetres;

        double twiceArea = 0d, cx = 0d, cy = 0d;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            double x0 = X(ring[i]), y0 = Y(ring[i]), x1 = X(ring[i + 1]), y1 = Y(ring[i + 1]);
            double cross = (x0 * y1) - (x1 * y0);
            twiceArea += cross;
            cx += (x0 + x1) * cross;
            cy += (y0 + y1) * cross;
        }

        double area = Math.Abs(twiceArea) / 2d;
        if (twiceArea == 0d)
        {
            return (ring[0], 0d);
        }

        cx /= 3d * twiceArea;
        cy /= 3d * twiceArea;
        double lon = ox + (cx / (EarthRadiusMetres * Math.Cos(meanLat)) * 180d / Math.PI);
        double lat = oy + (cy / EarthRadiusMetres * 180d / Math.PI);
        return (new GeoPoint(Math.Round(lon, 7), Math.Round(lat, 7)), area);
    }

    private static string Quote(string value)
    {
        StringBuilder quoted = new("\"");
        foreach (char c in value)
        {
            quoted.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when c < ' ' => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }

        return quoted.Append('"').ToString();
    }
}
