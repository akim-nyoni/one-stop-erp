using One4All.Application.Pos;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Pos;

/// <summary>A receipt layout as entered.</summary>
/// <param name="TerminalCode">The till it is for (blank: the company's layout for every till without its own).</param>
/// <param name="PaperWidthMm">The paper width: 80 or 58.</param>
/// <param name="Title">The title under the company name (blank: none).</param>
/// <param name="Header">Header lines, one per line (address, phone, a message).</param>
/// <param name="Footer">Footer lines, one per line (blank: "Thank you!").</param>
/// <param name="ShowCashier">Whether the cashier is shown.</param>
/// <param name="ShowTerminal">Whether the till is shown.</param>
/// <param name="ShowUnitPrice">Whether lines show quantity x unit price.</param>
/// <param name="ShowTaxSummary">Whether subtotal and tax are shown.</param>
/// <param name="ShowTaxNumber">Whether the company's tax number is shown.</param>
/// <param name="ShowFiscal">Whether the fiscal code is shown.</param>
/// <param name="Copies">How many copies print (1 to 5).</param>
public sealed record ReceiptLayoutInput(string? TerminalCode, int PaperWidthMm = 80, string Title = "SALES RECEIPT", string? Header = null, string? Footer = null, bool ShowCashier = true,
    bool ShowTerminal = true, bool ShowUnitPrice = false, bool ShowTaxSummary = true, bool ShowTaxNumber = false, bool ShowFiscal = true, int Copies = 1);

/// <summary>Receipt layouts per company and till, and a preview of how a receipt prints in one (brief §4.1).</summary>
public interface IReceiptLayoutService
{
    /// <summary>Saves the company's layout, or a till's own.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SaveAsync(long companyId, ReceiptLayoutInput layout, CancellationToken ct = default);

    /// <summary>Removes a till's own layout, so it prints the company's again.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalCode">The till.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> RemoveAsync(long companyId, string terminalCode, CancellationToken ct = default);

    /// <summary>The layout a till prints with: its own, else the company's, else the standard 80mm layout.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The till (null: the company's).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The layout.</returns>
    Task<ReceiptOptions> OptionsAsync(long companyId, long? terminalId, CancellationToken ct = default);

    /// <summary>A sample receipt in a layout, drawn inside the paper's edges: the layout given (not yet saved), else the saved one for the till.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="layout">The layout to try (null: the saved one).</param>
    /// <param name="terminalCode">The till whose saved layout to show when no layout is given.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The preview.</returns>
    Task<Result<string>> PreviewAsync(long companyId, ReceiptLayoutInput? layout, string? terminalCode = null, CancellationToken ct = default);
}
