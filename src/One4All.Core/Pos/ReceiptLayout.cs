using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>
/// How receipts are laid out (<c>pos.receipt_layout</c>, brief §4.1): the company's layout (no till) and any till's own. Header and footer keep one
/// printed line per text line.
/// </summary>
public sealed class ReceiptLayout : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReceiptLayout() => Title = string.Empty;

    /// <summary>Creates a layout.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The till (null: the company's layout).</param>
    public ReceiptLayout(long companyId, long? terminalId)
    {
        CompanyId = companyId;
        TerminalId = terminalId;
        Title = "SALES RECEIPT";
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the till the layout is for (null: the company's).</summary>
    public long? TerminalId { get; private set; }

    /// <summary>Gets or sets the paper width in millimetres (80 or 58).</summary>
    public int PaperWidthMm { get; set; } = 80;

    /// <summary>Gets or sets the title under the company name.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the header lines.</summary>
    public string? HeaderText { get; set; }

    /// <summary>Gets or sets the footer lines.</summary>
    public string? FooterText { get; set; }

    /// <summary>Gets or sets whether the cashier is shown.</summary>
    public bool ShowCashier { get; set; } = true;

    /// <summary>Gets or sets whether the till is shown.</summary>
    public bool ShowTerminal { get; set; } = true;

    /// <summary>Gets or sets whether lines show quantity x unit price.</summary>
    public bool ShowUnitPrice { get; set; }

    /// <summary>Gets or sets whether the subtotal and tax are shown.</summary>
    public bool ShowTaxSummary { get; set; } = true;

    /// <summary>Gets or sets whether the company's tax number is shown.</summary>
    public bool ShowTaxNumber { get; set; }

    /// <summary>Gets or sets whether the fiscal code is shown.</summary>
    public bool ShowFiscal { get; set; } = true;

    /// <summary>Gets or sets how many copies print.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Gets or sets the logo printed at the top, as packed monochrome printer rows (null: none).</summary>
    public byte[]? LogoRaster { get; set; }

    /// <summary>Gets or sets the logo's row width in bytes (8 dots each).</summary>
    public int LogoWidthBytes { get; set; }

    /// <summary>Gets or sets the logo's height in dots.</summary>
    public int LogoHeight { get; set; }
}
