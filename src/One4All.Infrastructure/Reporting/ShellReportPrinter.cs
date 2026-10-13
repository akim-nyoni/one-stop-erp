using System.Diagnostics;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Prints through the Windows shell: the document is written to a temporary file and handed to its registered application with the
/// "printto" verb and the printer's name (a PDF reader prints PDFs this way). Off Windows, or with no application for the file type, it fails.
/// </summary>
public sealed class ShellReportPrinter : IReportPrinter
{
    /// <inheritdoc />
    public async Task<Result> PrintAsync(string printerName, RenderedDocument document, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Failure(Error.Rule("Rpt.PrinterUnavailable", "Printing to a named printer needs the Windows host."));
        }

        string folder = Path.Combine(Path.GetTempPath(), "one4all-print");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"{Guid.NewGuid():N}-{document.FileName}");
        try
        {
            await File.WriteAllBytesAsync(path, document.Content, ct).ConfigureAwait(false);
            using Process? process = Process.Start(new ProcessStartInfo(path)
            {
                Verb = "printto", Arguments = $"\"{printerName}\"", UseShellExecute = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            });
            return Result.Success();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Result.Failure(Error.Failure("Rpt.PrintFailed", $"Could not print to {printerName}: {ex.Message}"));
        }
    }
}
