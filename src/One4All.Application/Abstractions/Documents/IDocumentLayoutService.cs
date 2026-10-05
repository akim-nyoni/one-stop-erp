using One4All.Application.Documents;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Documents;

/// <summary>The standard business document layouts (brief §14 S and the layouts named in B 50, C 67, E 94, H 139).</summary>
public interface IDocumentLayoutService
{
    /// <summary>A customer quotation with validity and total.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="quoteNumber">The quotation number.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> QuotationAsync(long companyId, string quoteNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>A sales order confirmation with what has been ordered and the requested delivery date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> OrderConfirmationAsync(long companyId, string orderNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>A delivery note (or packing note, without prices but with weights) for a despatch.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="deliveryReference">The despatch reference.</param>
    /// <param name="packing">Whether to print it as a packing note.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> DeliveryNoteAsync(long companyId, string deliveryReference, bool packing = false, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>A purchase requisition for approval.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requisitionNumber">The requisition number.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> RequisitionAsync(long companyId, string requisitionNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>A goods received note against its purchase order.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="grnNumber">The GRN number.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> GoodsReceivedNoteAsync(long companyId, string grnNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>Shelf / bin labels for the items on a price list (optionally only some items), with price and barcode.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="priceListCode">The price list.</param>
    /// <param name="itemCodes">The items to print (all on the list when empty).</param>
    /// <param name="perRow">Labels across.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The label sheet, or an error.</returns>
    Task<Result<string>> ShelfLabelsAsync(long companyId, string priceListCode, IReadOnlyList<string>? itemCodes = null, int perRow = 2, CancellationToken ct = default);
}
