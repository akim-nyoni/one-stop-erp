using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Water put into a supply zone over a period, from the bulk (zone) meter or the treatment works (<c>muni.bulk_supply</c>).</summary>
public sealed class BulkSupplyReading : Entity, ICompanyScoped
{
    private BulkSupplyReading()
    {
        Zone = string.Empty;
    }

    /// <summary>Records bulk supply.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="zone">The supply zone.</param>
    /// <param name="periodStart">The first day covered.</param>
    /// <param name="periodEnd">The last day covered.</param>
    /// <param name="volume">The volume supplied, in the same units as consumer meters.</param>
    /// <param name="source">The bulk meter or works.</param>
    public BulkSupplyReading(long companyId, string zone, DateOnly periodStart, DateOnly periodEnd, decimal volume, string? source)
    {
        CompanyId = companyId;
        Zone = zone;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Volume = volume;
        Source = source;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the supply zone.</summary>
    public string Zone { get; private set; }

    /// <summary>Gets the first day covered.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>Gets the last day covered.</summary>
    public DateOnly PeriodEnd { get; private set; }

    /// <summary>Gets the volume supplied.</summary>
    public decimal Volume { get; private set; }

    /// <summary>Gets the bulk meter or works.</summary>
    public string? Source { get; private set; }
}
