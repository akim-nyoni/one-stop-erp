using System.IO.Ports;
using System.Text;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;

namespace One4All.Infrastructure.Pos;

/// <summary>
/// Opens a serial port as the settings describe (a COM port, or a USB device that appears as one), for one exchange: devices are opened and closed per use,
/// so a terminal that lost its scale or display carries on without it.
/// </summary>
internal static class SerialLink
{
    /// <summary>Opens the port.</summary>
    /// <param name="settings">The device settings.</param>
    /// <returns>The open port.</returns>
    public static SerialPort Open(DeviceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SerialPort port = new(settings.PortName ?? "COM1", settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
        {
            ReadTimeout = settings.TimeoutMs,
            WriteTimeout = settings.TimeoutMs,
            Encoding = Encoding.Latin1,
            Handshake = settings.Handshake,
        };
        port.Open();
        return port;
    }

    /// <summary>Whether an exception is the port being absent, busy or silent (the device is treated as unavailable).</summary>
    /// <param name="ex">The exception.</param>
    /// <returns>True for a device fault.</returns>
    public static bool IsDeviceFault(Exception ex) => ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException or ArgumentException;
}

/// <summary>Sends raw ESC/POS or label bytes to a serial printer (the cash drawer opens through it like a network printer's).</summary>
/// <param name="settings">The settings.</param>
public sealed class SerialRawPrinter(DeviceSettings settings) : IReceiptPrinter, ILabelPrinter
{
    /// <inheritdoc />
    public Task<string?> PrintAsync(byte[] escPos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(escPos);
        try
        {
            using SerialPort port = SerialLink.Open(settings);
            port.Write(escPos, 0, escPos.Length);
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex) when (SerialLink.IsDeviceFault(ex))
        {
            return Task.FromResult<string?>($"Printer on {settings.PortName} unavailable: {ex.Message}");
        }
    }
}

/// <summary>Reads a serial scale: sends the weight request ("W" by default) and parses the reply like a network scale's.</summary>
/// <param name="settings">The settings.</param>
public sealed class SerialScale(DeviceSettings settings) : IScale
{
    /// <inheritdoc />
    public async Task<ScaleReading?> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            using SerialPort port = SerialLink.Open(settings);
            port.DiscardInBuffer();
            port.Write(settings.ScaleRequest ?? "W\r\n");
            StringBuilder reply = new();
            DateTime until = DateTime.UtcNow.AddMilliseconds(settings.TimeoutMs);
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                if (port.BytesToRead > 0)
                {
                    reply.Append(port.ReadExisting());
                    if (reply.ToString().Contains('\r') || reply.ToString().Contains('\n'))
                    {
                        break;
                    }
                }
                else
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                }
            }

            return ScaleResponse.TryParse(reply.ToString());
        }
        catch (Exception ex) when (SerialLink.IsDeviceFault(ex) || ex is OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>Shows two 20-character lines on a serial (or USB virtual-COM) pole display.</summary>
/// <param name="settings">The settings.</param>
public sealed class SerialCustomerDisplay(DeviceSettings settings) : ICustomerDisplay
{
    /// <inheritdoc />
    public Task ShowAsync(string line1, string line2, CancellationToken ct = default)
    {
        try
        {
            using SerialPort port = SerialLink.Open(settings);
            byte[] frame = NetworkCustomerDisplay.Frame(line1, line2);
            port.Write(frame, 0, frame.Length);
        }
        catch (Exception ex) when (SerialLink.IsDeviceFault(ex))
        {
            // A dark display must not stop the sale.
        }

        return Task.CompletedTask;
    }
}
