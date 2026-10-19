using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>What a workstation prints (each can go to its own printer).</summary>
public enum PrintPurpose
{
    /// <summary>Reports.</summary>
    Reports = 1,

    /// <summary>Customer documents: invoices, statements, bills.</summary>
    Documents = 2,

    /// <summary>Till and receipting slips.</summary>
    Receipts = 3,

    /// <summary>Cheques.</summary>
    Cheques = 4,

    /// <summary>Labels.</summary>
    Labels = 5,

    /// <summary>Payslips.</summary>
    Payslips = 6,
}

/// <summary>How a printer is reached.</summary>
public enum PrinterConnection
{
    /// <summary>A printer installed in Windows (USB, shared or driver-based), by its Windows name.</summary>
    Windows = 0,

    /// <summary>A network receipt or label printer taking raw ESC/POS on a TCP port (usually 9100).</summary>
    Network = 1,
}

/// <summary>
/// The printer a workstation uses for a purpose (<c>sys.workstation_printer</c>, brief §4.1 "printers per workstation"): its name as the
/// workstation knows it, the tray and the number of copies. Workstation "*" is the default for every workstation without its own.
/// </summary>
public sealed class WorkstationPrinter : Entity, ICompanyScoped
{
    /// <summary>Any workstation.</summary>
    public const string AnyWorkstation = "*";

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WorkstationPrinter()
    {
        Workstation = string.Empty;
        PrinterName = string.Empty;
    }

    /// <summary>Sets a workstation's printer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="workstation">The workstation (computer name), or "*" for every workstation.</param>
    /// <param name="purpose">What it prints.</param>
    /// <param name="printerName">The printer.</param>
    public WorkstationPrinter(long companyId, string workstation, PrintPurpose purpose, string printerName)
    {
        CompanyId = companyId;
        Workstation = workstation;
        Purpose = purpose;
        PrinterName = printerName;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The workstation (computer name), or "*" for every workstation.</summary>
    public string Workstation { get; private set; }

    /// <summary>What it prints.</summary>
    public PrintPurpose Purpose { get; private set; }

    /// <summary>The printer.</summary>
    public string PrinterName { get; set; }

    /// <summary>The tray, or null for the printer's default.</summary>
    public string? Tray { get; set; }

    /// <summary>The number of copies.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Gets or sets how the printer is reached.</summary>
    public PrinterConnection Connection { get; set; }

    /// <summary>Gets or sets the network printer's address (IP or host name).</summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the network printer's TCP port.</summary>
    public int Port { get; set; } = 9100;
}
