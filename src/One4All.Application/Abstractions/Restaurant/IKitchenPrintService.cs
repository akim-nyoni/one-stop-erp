using One4All.Core.Common;

namespace One4All.Application.Abstractions.Restaurant;

/// <summary>
/// Prints kitchen order tickets at their preparation stations (brief §4.2): each station is mapped to a printer, and a fired order's
/// tickets — one per station — print where they are cooked. A station with no printer works from its kitchen display only.
/// </summary>
public interface IKitchenPrintService
{
    /// <summary>Maps a station to a Windows printer (by name) or a network printer (host and port); remove drops the mapping.</summary>
    Task<Result> SetStationPrinterAsync(long companyId, StationPrinterSetting setting, bool remove = false, CancellationToken ct = default);

    /// <summary>The station printers.</summary>
    Task<IReadOnlyList<StationPrinterSetting>> StationPrintersAsync(long companyId, CancellationToken ct = default);

    /// <summary>Prints the tickets fired under a ticket number (the number itself, or its per-station tickets <c>number-STATION</c>).</summary>
    /// <returns>What happened at each station.</returns>
    Task<Result<IReadOnlyList<KitchenPrintOutcome>>> PrintAsync(long companyId, string ticketNumber, CancellationToken ct = default);
}

/// <summary>A station's printer.</summary>
/// <param name="Station">The station (KITCHEN for tickets with no station).</param>
/// <param name="PrinterName">The Windows printer, or null.</param>
/// <param name="Host">The network printer's host, or null.</param>
/// <param name="Port">The network port.</param>
/// <param name="Copies">How many copies.</param>
public sealed record StationPrinterSetting(string Station, string? PrinterName, string? Host, int Port = 9100, int Copies = 1);

/// <summary>What happened to one ticket.</summary>
/// <param name="TicketNumber">The ticket.</param>
/// <param name="Station">Its station.</param>
/// <param name="Printer">The printer it went to, or null when the station has none.</param>
/// <param name="Error">Why it did not print, or null when it did (or there is no printer).</param>
/// <param name="Text">The ticket as text (for the screen or a manual print).</param>
public sealed record KitchenPrintOutcome(string TicketNumber, string Station, string? Printer, string? Error, string Text)
{
    /// <summary>Gets a value indicating whether it printed.</summary>
    public bool Printed => Printer is not null && Error is null;
}
