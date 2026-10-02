using One4All.Application.Trading;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>Builds and renders customer invoice and credit note documents from posted AR items (brief §4.5).</summary>
public interface IInvoiceDocumentService
{
    /// <summary>
    /// Builds the document for an AR invoice or credit note open item from its posted journal: the AR
    /// control leg is skipped, VAT-control or tax-coded legs sum to the tax, every other leg becomes a line
    /// (named by its narrative or account name), all in transaction currency; totals and the outstanding
    /// balance come from the open item. Refuses items that are not invoices or credit notes.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="openItemId">The AR open item.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document data.</returns>
    Task<Result<InvoiceDocument>> BuildAsync(long companyId, long openItemId, CancellationToken ct = default);

    /// <summary>Renders the document as fixed-width text.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="openItemId">The AR open item.</param>
    /// <param name="width">The character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text.</returns>
    Task<Result<string>> RenderTextAsync(long companyId, long openItemId, int width = InvoiceFormatter.DefaultWidth, CancellationToken ct = default);
}
