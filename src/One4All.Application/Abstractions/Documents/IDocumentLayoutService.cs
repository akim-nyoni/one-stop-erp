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

    /// <summary>A supplier debit note for goods returned: what went back, at what price, and the credit claimed.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentNumber">The return / debit note number.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> SupplierDebitNoteAsync(long companyId, string documentNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>A cheque for printing on the leaf: date, payee, the amount in figures and in words, and the payment it settles.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountCode">The bank account (its code).</param>
    /// <param name="chequeNumber">The cheque number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cheque text, or an error (a voided or cancelled cheque is not printed).</returns>
    Task<Result<string>> ChequeAsync(long companyId, string bankAccountCode, string chequeNumber, CancellationToken ct = default);

    /// <summary>A ratepayer's municipal bill for a stand and month: each service charged in the month's billing runs, the balance brought forward and the total due.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="month">Any day in the billing month.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> MunicipalBillAsync(long companyId, string standNumber, DateOnly month, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>A school fee invoice: the fee items of the student's structure, any bursary, and the amount billed.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="invoiceNumber">The fee invoice number.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> FeeInvoiceAsync(long companyId, string invoiceNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>An interim payment certificate / progress valuation: work done, materials on site, previously certified, retention and net payable.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="certificateNumber">The certificate number.</param>
    /// <param name="width">The width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or an error.</returns>
    Task<Result<string>> PaymentCertificateAsync(long companyId, string certificateNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>Shelf / bin labels for the items on a price list (optionally only some items), with price and barcode.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="priceListCode">The price list.</param>
    /// <param name="itemCodes">The items to print (all on the list when empty).</param>
    /// <param name="perRow">Labels across.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The label sheet, or an error.</returns>
    Task<Result<string>> ShelfLabelsAsync(long companyId, string priceListCode, IReadOnlyList<string>? itemCodes = null, int perRow = 2, CancellationToken ct = default);

    /// <summary>
    /// Builds one of the business documents (the kinds in <see cref="DocumentKinds"/> except the tax invoice and purchase order, which have their own
    /// services) as the model a layout prints from.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="kind">The document kind.</param>
    /// <param name="number">The document's number (the stand number for a municipal bill).</param>
    /// <param name="date">The date that goes with it (any day in the month for a municipal bill).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document, or why there is none.</returns>
    Task<Result<BusinessDocument>> BuildAsync(long companyId, string kind, string number, DateOnly? date = null, CancellationToken ct = default);
}
