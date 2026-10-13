using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>
/// Sends a rendered report to a named printer (brief §5 report delivery: email, folder or printer). The host supplies it; a subscription
/// with a printer is refused at run time when none is registered.
/// </summary>
public interface IReportPrinter
{
    /// <summary>Prints a document on a printer.</summary>
    /// <param name="printerName">The printer as the host knows it.</param>
    /// <param name="document">The rendered document (a PDF prints best).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it could not be printed.</returns>
    Task<Result> PrintAsync(string printerName, RenderedDocument document, CancellationToken ct = default);
}
