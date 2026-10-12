using One4All.Application.Pos;

namespace One4All.Application.Abstractions.Pos;

/// <summary>A card terminal's answer.</summary>
/// <param name="Approved">Whether the payment was approved.</param>
/// <param name="AuthorisationCode">The authorisation code (approved) or the decline reason.</param>
/// <param name="MaskedCard">The card as the terminal shows it (e.g. ****1234).</param>
public sealed record CardAuthorisation(bool Approved, string AuthorisationCode, string? MaskedCard);

/// <summary>A receipt printer (ESC/POS).</summary>
public interface IReceiptPrinter
{
    /// <summary>Sends an ESC/POS byte stream.</summary>
    /// <param name="escPos">The bytes (see <see cref="EscPosBuilder"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Null when printed, or why not.</returns>
    Task<string?> PrintAsync(byte[] escPos, CancellationToken ct = default);
}

/// <summary>A cash drawer (usually kicked through the receipt printer).</summary>
public interface ICashDrawer
{
    /// <summary>Opens the drawer.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Null when opened, or why not.</returns>
    Task<string?> OpenAsync(CancellationToken ct = default);
}

/// <summary>A weighing scale.</summary>
public interface IScale
{
    /// <summary>Reads the current weight.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reading, or null when the scale did not answer.</returns>
    Task<ScaleReading?> ReadAsync(CancellationToken ct = default);
}

/// <summary>A customer-facing (pole) display of two lines.</summary>
public interface ICustomerDisplay
{
    /// <summary>Shows two lines.</summary>
    /// <param name="line1">The top line.</param>
    /// <param name="line2">The bottom line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task ShowAsync(string line1, string line2, CancellationToken ct = default);
}

/// <summary>An integrated card terminal.</summary>
public interface ICardTerminal
{
    /// <summary>Asks the terminal to take a card payment.</summary>
    /// <param name="amount">The amount.</param>
    /// <param name="reference">The sale reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The answer.</returns>
    Task<CardAuthorisation> ChargeAsync(decimal amount, string reference, CancellationToken ct = default);
}

/// <summary>A label printer (shelf-edge and scale labels), fed raw printer language (ZPL, EPL, TSPL...).</summary>
public interface ILabelPrinter
{
    /// <summary>Sends a label job.</summary>
    /// <param name="job">The printer-language bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Null when sent, or why not.</returns>
    Task<string?> PrintAsync(byte[] job, CancellationToken ct = default);
}

/// <summary>
/// The terminal's peripherals (brief §4.1 hardware): each is null when the terminal has none, a simulator in development and tests, or a real
/// device over the network, the Windows print spooler or TCP. Also carries the embedded-barcode templates the scanner input is read through.
/// </summary>
public interface IDeviceProvider
{
    /// <summary>Gets the receipt printer.</summary>
    IReceiptPrinter? Printer { get; }

    /// <summary>Gets the cash drawer.</summary>
    ICashDrawer? Drawer { get; }

    /// <summary>Gets the scale.</summary>
    IScale? Scale { get; }

    /// <summary>Gets the customer display.</summary>
    ICustomerDisplay? Display { get; }

    /// <summary>Gets the card terminal.</summary>
    ICardTerminal? CardTerminal { get; }

    /// <summary>Gets the label printer.</summary>
    ILabelPrinter? LabelPrinter { get; }

    /// <summary>Gets the embedded price / weight barcode templates.</summary>
    IReadOnlyList<EmbeddedBarcodeRule> BarcodeRules { get; }

    /// <summary>Gets the fingerprint reader, when the terminal has one (the hook a vendor's reader SDK plugs into).</summary>
    IFingerprintReader? FingerprintReader => null;
}

/// <summary>A fingerprint reader that matches a finger on the device and answers with the enrolment it matched.</summary>
public interface IFingerprintReader
{
    /// <summary>Waits for a finger and returns the matched enrolment id, or null when nobody matched.</summary>
    /// <returns>The enrolment id.</returns>
    Task<string?> IdentifyAsync(CancellationToken ct = default);
}
