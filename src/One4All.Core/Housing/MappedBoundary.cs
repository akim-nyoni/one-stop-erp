namespace One4All.Core.Housing;

/// <summary>
/// A mapped boundary (brief §4.10.10 GIS hook), held on parcels and stands: the polygon as WKT - PostGIS-ready, the same
/// text loads into a geometry column - with its centroid and mapped area.
/// </summary>
public sealed class MappedBoundary
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private MappedBoundary()
    {
        Wkt = string.Empty;
    }

    /// <summary>Records a boundary.</summary>
    /// <param name="wkt">The polygon as WKT (longitude latitude, WGS 84).</param>
    /// <param name="centroidLongitude">The centroid's longitude.</param>
    /// <param name="centroidLatitude">The centroid's latitude.</param>
    /// <param name="areaSquareMetres">The mapped area.</param>
    public MappedBoundary(string wkt, double centroidLongitude, double centroidLatitude, decimal areaSquareMetres)
    {
        Wkt = wkt;
        CentroidLongitude = centroidLongitude;
        CentroidLatitude = centroidLatitude;
        AreaSquareMetres = areaSquareMetres;
    }

    /// <summary>The polygon as WKT.</summary>
    public string Wkt { get; private set; }

    /// <summary>The centroid's longitude.</summary>
    public double CentroidLongitude { get; private set; }

    /// <summary>The centroid's latitude.</summary>
    public double CentroidLatitude { get; private set; }

    /// <summary>The mapped area.</summary>
    public decimal AreaSquareMetres { get; private set; }
}
