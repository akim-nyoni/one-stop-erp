using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>
/// The printer a preparation station's kitchen tickets print on (<c>rst.station_printer</c>; brief §4.2 kitchen order tickets routed to
/// the right station printer): a Windows printer by name, or a network printer by host and port.
/// </summary>
public sealed class KitchenStationPrinter : AuditableEntity, ICompanyScoped
{
    /// <summary>The station name used for tickets with no station of their own (the main kitchen).</summary>
    public const string MainKitchen = "KITCHEN";

    private KitchenStationPrinter() => Station = string.Empty;

    /// <summary>Maps a station to a printer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="station">The station (upper case; KITCHEN for the main kitchen).</param>
    public KitchenStationPrinter(long companyId, string station)
    {
        CompanyId = companyId;
        Station = station;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the station.</summary>
    public string Station { get; private set; }

    /// <summary>Gets or sets the Windows printer name (when printed through the spooler).</summary>
    public string? PrinterName { get; set; }

    /// <summary>Gets or sets the network printer's host (when printed straight to it).</summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the network printer's port.</summary>
    public int Port { get; set; } = 9100;

    /// <summary>Gets or sets how many copies print.</summary>
    public int Copies { get; set; } = 1;
}
