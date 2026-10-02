using One4All.Application.Procurement;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>Builds and renders purchase order documents for suppliers (brief §4.3).</summary>
public interface IPurchaseOrderDocumentService
{
    /// <summary>Builds the document for a purchase order from the order, its supplier and items. Refuses a cancelled order.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The purchase order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document data.</returns>
    Task<Result<PurchaseOrderDocument>> BuildAsync(long companyId, long purchaseOrderId, CancellationToken ct = default);

    /// <summary>Renders the document as fixed-width text.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The purchase order.</param>
    /// <param name="width">The character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text.</returns>
    Task<Result<string>> RenderTextAsync(long companyId, long purchaseOrderId, int width = PurchaseOrderFormatter.DefaultWidth, CancellationToken ct = default);
}
