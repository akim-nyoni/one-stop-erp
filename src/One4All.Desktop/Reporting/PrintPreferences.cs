using System.Printing;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.System;
using One4All.Core.System;
using One4All.Reporting.Model;

namespace One4All.Desktop.Reporting;

/// <summary>Offers a report's preferred tray and two-sided printing in the print dialog (the user can still change them).</summary>
public static class PrintPreferences
{
    /// <summary>Preselects the report's tray and duplexing.</summary>
    /// <param name="dialog">The print dialog.</param>
    /// <param name="definitionJson">The report's definition.</param>
    public static void Apply(PrintDialog dialog, string definitionJson)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        PageSetup page;
        try
        {
            page = ReportDefinitionSerializer.Deserialize(definitionJson).Page;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            return;
        }

        if (Bin(page.Tray) is InputBin bin)
        {
            dialog.PrintTicket.InputBin = bin;
        }

        dialog.PrintTicket.Duplexing = page.Duplex switch
        {
            DuplexMode.LongEdge => Duplexing.TwoSidedLongEdge,
            DuplexMode.ShortEdge => Duplexing.TwoSidedShortEdge,
            _ => dialog.PrintTicket.Duplexing,
        };
    }

    /// <summary>
    /// Preselects this workstation's printer, tray and copies for the purpose (set under System Manager › Printers per workstation). A printer this
    /// computer cannot reach is ignored, leaving the user's default.
    /// </summary>
    /// <param name="dialog">The print dialog.</param>
    /// <param name="services">The application services.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="purpose">What is being printed.</param>
    /// <returns>A task.</returns>
    public static async Task ApplyWorkstationAsync(PrintDialog dialog, IServiceProvider services, long companyId, PrintPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(services);
        PrinterSetting? setting;
        using (IServiceScope scope = services.CreateScope())
        {
            setting = await scope.ServiceProvider.GetRequiredService<IWorkstationPrinterService>().ResolveAsync(companyId, Environment.MachineName, purpose).ConfigureAwait(true);
        }

        if (setting is null)
        {
            return;
        }

        try
        {
            using LocalPrintServer server = new();
            PrintQueue? queue = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
                .FirstOrDefault(q => string.Equals(q.FullName, setting.PrinterName, StringComparison.OrdinalIgnoreCase) || string.Equals(q.Name, setting.PrinterName, StringComparison.OrdinalIgnoreCase));
            if (queue is null)
            {
                return;
            }

            dialog.PrintQueue = queue;
            dialog.PrintTicket = queue.DefaultPrintTicket;
        }
        catch (PrintSystemException)
        {
            return;
        }

        if (Bin(setting.Tray) is InputBin bin)
        {
            dialog.PrintTicket.InputBin = bin;
        }

        dialog.PrintTicket.CopyCount = setting.Copies;
    }

    /// <summary>The printers this computer can print to, for choosing one.</summary>
    /// <returns>The printer names.</returns>
    public static IReadOnlyList<string> InstalledPrinters()
    {
        try
        {
            using LocalPrintServer server = new();
            return [.. server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]).Select(q => q.FullName).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (PrintSystemException)
        {
            return [];
        }
    }

    /// <summary>The printer input bin a tray name stands for.</summary>
    /// <param name="tray">The tray name.</param>
    /// <returns>The bin, or null for a name the print system has no bin for.</returns>
    public static InputBin? Bin(string? tray) => tray?.Trim().ToUpperInvariant() switch
    {
        "AUTO" or "AUTOSELECT" => InputBin.AutoSelect,
        "MANUAL" or "MANUAL FEED" => InputBin.Manual,
        "CASSETTE" or "TRAY" => InputBin.Cassette,
        "TRACTOR" or "CONTINUOUS" => InputBin.Tractor,
        "AUTOSHEETFEEDER" or "SHEET FEEDER" => InputBin.AutoSheetFeeder,
        _ => null,
    };
}
