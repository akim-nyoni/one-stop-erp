using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.System;
using One4All.Application.Pos;
using One4All.Core.System;

namespace One4All.Infrastructure.Pos;

/// <summary>Prints receipts on the workstation's receipt printers.</summary>
public sealed class ReceiptPrintService : IReceiptPrintService
{
    private readonly IWorkstationPrinterService _printers;
    private readonly IReceiptLayoutService _layouts;

    /// <summary>Creates the service.</summary>
    /// <param name="printers">The printers per workstation.</param>
    /// <param name="layouts">The receipt layouts (copies).</param>
    public ReceiptPrintService(IWorkstationPrinterService printers, IReceiptLayoutService layouts)
    {
        _printers = printers;
        _layouts = layouts;
    }

    /// <inheritdoc />
    public async Task<ReceiptPrintOutcome?> PrintAsync(long companyId, string workstation, long? terminalId, string receiptText, bool openDrawer, CancellationToken ct = default)
    {
        IReadOnlyList<PrinterSetting> printers = await _printers.ResolveAllAsync(companyId, workstation, PrintPurpose.Receipts, ct).ConfigureAwait(false);
        if (printers.Count == 0)
        {
            return null;
        }

        int copies = (await _layouts.OptionsAsync(companyId, terminalId, ct).ConfigureAwait(false)).Copies;
        ReceiptLogo? logo = await _layouts.LogoAsync(companyId, terminalId, ct).ConfigureAwait(false);
        int printed = 0;
        List<string> failures = [];
        for (int index = 0; index < printers.Count; index++)
        {
            PrinterSetting printer = printers[index];
            string? failed = null;
            for (int copy = 0; copy < Math.Max(1, copies) * Math.Max(1, printer.Copies) && failed is null; copy++)
            {
                failed = await Raw(printer).PrintAsync(EscPosBuilder.Receipt(receiptText, openDrawer && index == 0 && copy == 0, logo), ct).ConfigureAwait(false);
            }

            if (failed is null)
            {
                printed++;
            }
            else
            {
                failures.Add($"{printer.PrinterName}: {failed}");
            }
        }

        return new ReceiptPrintOutcome(printed, failures);
    }

    /// <inheritdoc />
    public async Task<byte[]> ReceiptBytesAsync(long companyId, long? terminalId, string receiptText, bool openDrawer, CancellationToken ct = default)
        => EscPosBuilder.Receipt(receiptText, openDrawer, await _layouts.LogoAsync(companyId, terminalId, ct).ConfigureAwait(false));

    /// <inheritdoc />
    public Task<string?> TestAsync(PrinterSetting setting, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        string text = string.Join(Environment.NewLine, "One4All printer test", setting.PrinterName, setting.Connection == PrinterConnection.Network ? $"{setting.Host}:{setting.Port}" : "Windows printer",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), new string('-', 32), "If you can read this, the printer works.");
        return Raw(setting).PrintAsync(EscPosBuilder.Receipt(text, openDrawer: false), ct);
    }

    private static Application.Abstractions.Pos.IReceiptPrinter Raw(PrinterSetting printer)
        => printer.Connection == PrinterConnection.Network
            ? new NetworkRawPrinter(new DeviceSettings { Connection = DeviceConnection.Network, Host = printer.Host, Port = printer.Port })
            : OperatingSystem.IsWindows() ? new WindowsRawPrinter(printer.PrinterName) : new SimulatedPrinter();
}
