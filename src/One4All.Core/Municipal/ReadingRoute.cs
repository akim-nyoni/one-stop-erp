using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>
/// A meter reading route (<c>mun.reading_route</c>, brief §4.9): the meters a reader visits, in walking order. A meter
/// is on one route at most.
/// </summary>
public sealed class ReadingRoute : AuditableEntity, ICompanyScoped
{
    private readonly List<ReadingRouteStop> _stops = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReadingRoute()
    {
        Code = string.Empty;
        Name = string.Empty;
        ReaderName = string.Empty;
    }

    /// <summary>Creates a route.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The route code.</param>
    /// <param name="name">The route name.</param>
    /// <param name="readerName">The meter reader assigned.</param>
    public ReadingRoute(long companyId, string code, string name, string readerName)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        ReaderName = readerName;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The route code.</summary>
    public string Code { get; private set; }

    /// <summary>The route name.</summary>
    public string Name { get; private set; }

    /// <summary>The meter reader assigned.</summary>
    public string ReaderName { get; set; }

    /// <summary>The meters in walking order.</summary>
    public IReadOnlyList<ReadingRouteStop> Stops => _stops.AsReadOnly();

    /// <summary>Adds the next meter on the route.</summary>
    /// <param name="meterId">The meter.</param>
    public void AddStop(long meterId) => _stops.Add(new ReadingRouteStop(_stops.Count + 1, meterId));
}

/// <summary>A meter on a reading route (<c>mun.reading_route_stop</c>).</summary>
public sealed class ReadingRouteStop : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReadingRouteStop()
    {
    }

    /// <summary>Creates a stop.</summary>
    /// <param name="sequence">Its place in the walking order, from 1.</param>
    /// <param name="meterId">The meter.</param>
    public ReadingRouteStop(int sequence, long meterId)
    {
        Sequence = sequence;
        MeterId = meterId;
    }

    /// <summary>The route.</summary>
    public long ReadingRouteId { get; private set; }

    /// <summary>Its place in the walking order, from 1.</summary>
    public int Sequence { get; private set; }

    /// <summary>The meter.</summary>
    public long MeterId { get; private set; }
}
