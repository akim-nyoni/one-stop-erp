using One4All.Core.Common;

namespace One4All.Application.Abstractions.Pos;

/// <summary>What a print run did.</summary>
/// <param name="Printed">Printers the receipt reached.</param>
/// <param name="Failures">Printers it did not, with why.</param>
public sealed record ReceiptPrintOutcome(int Printed, IReadOnlyList<string> Failures);

/// <summary>
/// Prints till receipts on the printers set up for the workstation (Printers per workstation, purpose Receipts: one or several, Windows or network), in
/// the till's receipt layout's number of copies.
/// </summary>
public interface IReceiptPrintService
{
    /// <summary>Prints a receipt on every receipt printer of the workstation; the first one opens the cash drawer when asked.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="workstation">The workstation (computer name).</param>
    /// <param name="terminalId">The till (its layout gives the copies).</param>
    /// <param name="receiptText">The receipt.</param>
    /// <param name="openDrawer">Whether to kick the cash drawer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened, or null when no receipt printer is set up for the workstation (the till then uses its own device).</returns>
    Task<ReceiptPrintOutcome?> PrintAsync(long companyId, string workstation, long? terminalId, string receiptText, bool openDrawer, CancellationToken ct = default);

    /// <summary>The bytes a receipt prints as on a thermal printer: the till's logo (if any) above the text, fed and cut, the drawer opened when asked.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The till.</param>
    /// <param name="receiptText">The receipt.</param>
    /// <param name="openDrawer">Whether to kick the cash drawer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bytes.</returns>
    Task<byte[]> ReceiptBytesAsync(long companyId, long? terminalId, string receiptText, bool openDrawer, CancellationToken ct = default);

    /// <summary>Prints a test receipt on one printer.</summary>
    /// <param name="setting">The printer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Null when printed, else why not.</returns>
    Task<string?> TestAsync(System.PrinterSetting setting, CancellationToken ct = default);
}
