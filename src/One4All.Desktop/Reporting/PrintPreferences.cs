using System.Printing;
using System.Windows.Controls;
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
