using System.ComponentModel;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;

namespace One4All.Infrastructure.Pos;

/// <summary>How a peripheral is reached.</summary>
public enum DeviceConnection
{
    /// <summary>The terminal has none.</summary>
    None = 0,

    /// <summary>An in-memory simulator (development and tests).</summary>
    Simulator = 1,

    /// <summary>Raw TCP (network printers on port 9100, scale and display servers).</summary>
    Network = 2,

    /// <summary>The Windows print spooler, raw (USB receipt and label printers installed in Windows).</summary>
    WindowsPrinter = 3,
}

/// <summary>One peripheral's settings.</summary>
public sealed class DeviceSettings
{
    /// <summary>Gets or sets how it is reached.</summary>
    public DeviceConnection Connection { get; set; }

    /// <summary>Gets or sets the host (network).</summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the port (network).</summary>
    public int Port { get; set; } = 9100;

    /// <summary>Gets or sets the Windows printer name (spooler).</summary>
    public string? PrinterName { get; set; }

    /// <summary>Gets or sets the timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 3000;
}

/// <summary>The terminal's peripherals (<c>Pos:Devices</c>); the drawer opens through the receipt printer.</summary>
public sealed class PosDeviceOptions
{
    /// <summary>Gets or sets the receipt printer.</summary>
    public DeviceSettings Printer { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the cash drawer is wired to the receipt printer.</summary>
    public bool DrawerOnPrinter { get; set; } = true;

    /// <summary>Gets or sets the scale (network: a scale server answering "W" with the weight).</summary>
    public DeviceSettings Scale { get; set; } = new();

    /// <summary>Gets or sets the customer display (network: an ESC/POS-compatible pole display).</summary>
    public DeviceSettings Display { get; set; } = new();

    /// <summary>Gets or sets the card terminal (simulator only: real terminals plug in through their vendor's SDK).</summary>
    public DeviceSettings CardTerminal { get; set; } = new();

    /// <summary>Gets or sets the label printer.</summary>
    public DeviceSettings LabelPrinter { get; set; } = new();

    /// <summary>Gets or sets the embedded price / weight barcode templates (empty: the standard 21 weight / 22 price).</summary>
    public List<EmbeddedBarcodeRule> BarcodeRules { get; set; } = [];

    /// <summary>Gets or sets the simulator scale's weight.</summary>
    public decimal SimulatedWeight { get; set; } = 1.250m;

    /// <summary>Gets or sets the amount above which the simulated card terminal declines.</summary>
    public decimal SimulatedCardLimit { get; set; } = 1000m;
}

/// <summary>Builds the terminal's peripherals from its settings.</summary>
public sealed class DeviceProvider : IDeviceProvider
{
    /// <summary>Creates the provider.</summary>
    /// <param name="options">The settings.</param>
    public DeviceProvider(PosDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Printer = options.Printer.Connection switch
        {
            DeviceConnection.Simulator => new SimulatedPrinter(),
            DeviceConnection.Network => new NetworkRawPrinter(options.Printer),
            DeviceConnection.WindowsPrinter when OperatingSystem.IsWindows() => new WindowsRawPrinter(options.Printer.PrinterName ?? string.Empty),
            _ => null,
        };
        Drawer = Printer is not null && options.DrawerOnPrinter ? new PrinterCashDrawer(Printer) : null;
        Scale = options.Scale.Connection switch
        {
            DeviceConnection.Simulator => new SimulatedScale(options.SimulatedWeight),
            DeviceConnection.Network => new NetworkScale(options.Scale),
            _ => null,
        };
        Display = options.Display.Connection switch
        {
            DeviceConnection.Simulator => new SimulatedDisplay(),
            DeviceConnection.Network => new NetworkCustomerDisplay(options.Display),
            _ => null,
        };
        CardTerminal = options.CardTerminal.Connection == DeviceConnection.Simulator ? new SimulatedCardTerminal(options.SimulatedCardLimit) : null;
        LabelPrinter = options.LabelPrinter.Connection switch
        {
            DeviceConnection.Simulator => new SimulatedPrinter(),
            DeviceConnection.Network => new NetworkRawPrinter(options.LabelPrinter),
            DeviceConnection.WindowsPrinter when OperatingSystem.IsWindows() => new WindowsRawPrinter(options.LabelPrinter.PrinterName ?? string.Empty),
            _ => null,
        };
        BarcodeRules = options.BarcodeRules.Count > 0 ? options.BarcodeRules : Barcodes.DefaultRules;
    }

    /// <inheritdoc />
    public IReceiptPrinter? Printer { get; }

    /// <inheritdoc />
    public ICashDrawer? Drawer { get; }

    /// <inheritdoc />
    public IScale? Scale { get; }

    /// <inheritdoc />
    public ICustomerDisplay? Display { get; }

    /// <inheritdoc />
    public ICardTerminal? CardTerminal { get; }

    /// <inheritdoc />
    public ILabelPrinter? LabelPrinter { get; }

    /// <inheritdoc />
    public IReadOnlyList<EmbeddedBarcodeRule> BarcodeRules { get; }
}

/// <summary>Opens the drawer through the receipt printer's drawer port.</summary>
/// <param name="printer">The printer.</param>
public sealed class PrinterCashDrawer(IReceiptPrinter printer) : ICashDrawer
{
    /// <inheritdoc />
    public Task<string?> OpenAsync(CancellationToken ct = default) => printer.PrintAsync(new EscPosBuilder().KickDrawer().ToArray(), ct);
}

/// <summary>Records what would have been printed (development and tests).</summary>
public sealed class SimulatedPrinter : IReceiptPrinter, ILabelPrinter
{
    private readonly List<byte[]> _jobs = [];

    /// <summary>Gets the jobs sent, in order.</summary>
    public IReadOnlyList<byte[]> Jobs => _jobs;

    /// <inheritdoc />
    public Task<string?> PrintAsync(byte[] escPos, CancellationToken ct = default)
    {
        lock (_jobs)
        {
            _jobs.Add(escPos);
        }

        return Task.FromResult<string?>(null);
    }
}

/// <summary>Returns a fixed, stable weight.</summary>
/// <param name="kilograms">The weight.</param>
public sealed class SimulatedScale(decimal kilograms) : IScale
{
    /// <inheritdoc />
    public Task<ScaleReading?> ReadAsync(CancellationToken ct = default) => Task.FromResult<ScaleReading?>(new ScaleReading(kilograms, Stable: true));
}

/// <summary>Remembers the last two lines shown.</summary>
public sealed class SimulatedDisplay : ICustomerDisplay
{
    /// <summary>Gets the lines last shown.</summary>
    public (string Line1, string Line2) Showing { get; private set; } = (string.Empty, string.Empty);

    /// <inheritdoc />
    public Task ShowAsync(string line1, string line2, CancellationToken ct = default)
    {
        Showing = (line1, line2);
        return Task.CompletedTask;
    }
}

/// <summary>Approves up to a limit and declines above it.</summary>
/// <param name="limit">The highest amount approved.</param>
public sealed class SimulatedCardTerminal(decimal limit) : ICardTerminal
{
    /// <inheritdoc />
    public Task<CardAuthorisation> ChargeAsync(decimal amount, string reference, CancellationToken ct = default)
        => Task.FromResult(amount > 0m && amount <= limit
            ? new CardAuthorisation(true, $"SIM{Math.Abs(HashCode.Combine(reference, amount)) % 1000000:000000}", "****4242")
            : new CardAuthorisation(false, amount <= 0m ? "Invalid amount" : "Declined: over the simulated limit", null));
}

/// <summary>Sends raw bytes to a network printer (port 9100 by default).</summary>
/// <param name="settings">The settings.</param>
public sealed class NetworkRawPrinter(DeviceSettings settings) : IReceiptPrinter, ILabelPrinter
{
    /// <inheritdoc />
    public async Task<string?> PrintAsync(byte[] escPos, CancellationToken ct = default)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(settings.TimeoutMs);
            using TcpClient client = new();
            await client.ConnectAsync(settings.Host ?? string.Empty, settings.Port, timeout.Token).ConfigureAwait(false);
            await using NetworkStream stream = client.GetStream();
            await stream.WriteAsync(escPos, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            return $"Printer {settings.Host}:{settings.Port} not reachable: {ex.Message}";
        }
    }
}

/// <summary>Asks a scale server for the weight ("W" + CR LF) and reads its reply.</summary>
/// <param name="settings">The settings.</param>
public sealed class NetworkScale(DeviceSettings settings) : IScale
{
    /// <inheritdoc />
    public async Task<ScaleReading?> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(settings.TimeoutMs);
            using TcpClient client = new();
            await client.ConnectAsync(settings.Host ?? string.Empty, settings.Port, timeout.Token).ConfigureAwait(false);
            await using NetworkStream stream = client.GetStream();
            await stream.WriteAsync("W\r\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);
            byte[] buffer = new byte[256];
            int read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            return ScaleResponse.TryParse(Encoding.ASCII.GetString(buffer, 0, read));
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            return null;
        }
    }
}

/// <summary>Shows two 20-character lines on an ESC/POS-compatible customer display over TCP.</summary>
/// <param name="settings">The settings.</param>
public sealed class NetworkCustomerDisplay(DeviceSettings settings) : ICustomerDisplay
{
    /// <summary>The bytes that clear the display and show two lines.</summary>
    /// <param name="line1">The top line.</param>
    /// <param name="line2">The bottom line.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Frame(string line1, string line2)
        => [0x0C, .. Encoding.Latin1.GetBytes(Fit(line1)), 0x0D, 0x0A, .. Encoding.Latin1.GetBytes(Fit(line2))];

    /// <inheritdoc />
    public async Task ShowAsync(string line1, string line2, CancellationToken ct = default)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(settings.TimeoutMs);
            using TcpClient client = new();
            await client.ConnectAsync(settings.Host ?? string.Empty, settings.Port, timeout.Token).ConfigureAwait(false);
            await using NetworkStream stream = client.GetStream();
            await stream.WriteAsync(Frame(line1, line2), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            // A dark display must not stop the sale.
        }
    }

    private static string Fit(string text) => (text ?? string.Empty).Length > 20 ? text[..20] : (text ?? string.Empty).PadRight(20);
}

/// <summary>Sends raw bytes through the Windows print spooler to an installed printer (USB receipt and label printers).</summary>
/// <param name="printerName">The Windows printer name.</param>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsRawPrinter(string printerName) : IReceiptPrinter, ILabelPrinter
{
    /// <inheritdoc />
    public Task<string?> PrintAsync(byte[] escPos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(escPos);
        return Task.Run<string?>(() =>
        {
            if (!NativeMethods.OpenPrinter(printerName, out IntPtr handle, IntPtr.Zero))
            {
                return $"Printer \"{printerName}\" not found ({new Win32Exception(Marshal.GetLastWin32Error()).Message}).";
            }

            try
            {
                DocInfo doc = new() { DocName = "One4All receipt", DataType = "RAW" };
                if (!NativeMethods.StartDocPrinter(handle, 1, ref doc) || !NativeMethods.StartPagePrinter(handle))
                {
                    return $"Printer \"{printerName}\" refused the job.";
                }

                IntPtr buffer = Marshal.AllocHGlobal(escPos.Length);
                try
                {
                    Marshal.Copy(escPos, 0, buffer, escPos.Length);
                    bool written = NativeMethods.WritePrinter(handle, buffer, escPos.Length, out int count) && count == escPos.Length;
                    NativeMethods.EndPagePrinter(handle);
                    NativeMethods.EndDocPrinter(handle);
                    return written ? null : $"Printer \"{printerName}\" took {count} of {escPos.Length} bytes.";
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.ClosePrinter(handle);
            }
        }, ct);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string DocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? OutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string DataType;
    }

    private static class NativeMethods
    {
        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr handle);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool StartDocPrinter(IntPtr handle, int level, ref DocInfo doc);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr handle, IntPtr bytes, int count, out int written);
    }
}

/// <summary>Formats amounts for the customer display.</summary>
public static class DisplayText
{
    /// <summary>A label and an amount on one 20-character line, the amount right-aligned.</summary>
    /// <param name="label">The label.</param>
    /// <param name="amount">The amount.</param>
    /// <returns>The line.</returns>
    public static string Amount(string label, decimal amount)
    {
        string value = amount.ToString("N2", CultureInfo.InvariantCulture);
        int room = Math.Max(0, 20 - value.Length - 1);
        string left = label.Length > room ? label[..room] : label;
        return left.PadRight(20 - value.Length) + value;
    }
}
