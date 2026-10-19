using One4All.Core.Common;
using One4All.Core.System;

namespace One4All.Application.Abstractions.System;

/// <summary>A workstation's printer for a purpose.</summary>
/// <param name="Workstation">The workstation, or "*" for every workstation.</param>
/// <param name="Purpose">What it prints.</param>
/// <param name="PrinterName">The printer.</param>
/// <param name="Tray">The tray, or null.</param>
/// <param name="Copies">The number of copies.</param>
/// <param name="Connection">How the printer is reached: installed in Windows, or a network ESC/POS printer.</param>
/// <param name="Host">The network printer's address.</param>
/// <param name="Port">The network printer's TCP port.</param>
public sealed record PrinterSetting(string Workstation, PrintPurpose Purpose, string PrinterName, string? Tray, int Copies, PrinterConnection Connection = PrinterConnection.Windows,
    string? Host = null, int Port = 9100);

/// <summary>Printers per workstation (brief §4.1): which printer, tray and copies each workstation uses for each kind of printing.</summary>
public interface IWorkstationPrinterService
{
    /// <summary>Sets (or replaces) a workstation's printer for a purpose.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="setting">The setting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetAsync(long companyId, PrinterSetting setting, CancellationToken ct = default);

    /// <summary>Removes a workstation's printer for a purpose (it falls back to the default for every workstation).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="workstation">The workstation.</param>
    /// <param name="purpose">What it prints.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RemoveAsync(long companyId, string workstation, PrintPurpose purpose, CancellationToken ct = default);

    /// <summary>
    /// Adds another printer for a purpose on a workstation (brief §4.1 multiple printers: a customer copy and a shop copy, a receipt printer at each
    /// counter): everything printed for the purpose goes to each of them. Setting the printer again by name changes it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="setting">The printer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AddAsync(long companyId, PrinterSetting setting, CancellationToken ct = default);

    /// <summary>Removes one of a workstation's printers for a purpose, by name.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="workstation">The workstation.</param>
    /// <param name="purpose">What it prints.</param>
    /// <param name="printerName">The printer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RemovePrinterAsync(long companyId, string workstation, PrintPurpose purpose, string printerName, CancellationToken ct = default);

    /// <summary>Every printer a workstation uses for a purpose: its own, else the defaults for every workstation, else none.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="workstation">The workstation.</param>
    /// <param name="purpose">What it prints.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The printers.</returns>
    Task<IReadOnlyList<PrinterSetting>> ResolveAllAsync(long companyId, string workstation, PrintPurpose purpose, CancellationToken ct = default);

    /// <summary>The printer a workstation uses for a purpose: its own, else the default for every workstation, else none.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="workstation">The workstation.</param>
    /// <param name="purpose">What it prints.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The setting, or null to let the user choose.</returns>
    Task<PrinterSetting?> ResolveAsync(long companyId, string workstation, PrintPurpose purpose, CancellationToken ct = default);

    /// <summary>Every setting, by workstation and purpose.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settings.</returns>
    Task<IReadOnlyList<PrinterSetting>> ListAsync(long companyId, CancellationToken ct = default);
}
