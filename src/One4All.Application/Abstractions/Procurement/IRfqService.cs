using One4All.Application.Procurement;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>A requirement to put out to quotation.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Description">The description.</param>
/// <param name="Quantity">The quantity.</param>
public sealed record RfqLineRequest(long ItemId, string Description, decimal Quantity);

/// <summary>A new request for quotation.</summary>
/// <param name="RfqNumber">The number.</param>
/// <param name="IssuedOn">When it is issued.</param>
/// <param name="ClosesOn">The last day quotes are accepted.</param>
/// <param name="SupplierIds">The suppliers invited.</param>
/// <param name="Lines">The requirements.</param>
public sealed record RfqRequest(string RfqNumber, DateOnly IssuedOn, DateOnly ClosesOn, IReadOnlyList<long> SupplierIds, IReadOnlyList<RfqLineRequest> Lines);

/// <summary>A supplier's quote: a unit price per item and a lead time.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="ReceivedOn">When it was received.</param>
/// <param name="LeadTimeDays">The promised lead time in days.</param>
/// <param name="UnitPrices">The unit price for each item required.</param>
/// <param name="Terms">Terms quoted, if any.</param>
public sealed record QuoteRequest(long SupplierId, DateOnly ReceivedOn, int LeadTimeDays, IReadOnlyDictionary<long, decimal> UnitPrices, string? Terms = null);

/// <summary>One supplier's column in the comparison matrix.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Its code.</param>
/// <param name="UnitPrices">Its unit price per item.</param>
/// <param name="Score">Its total, lead time, points and rank.</param>
public sealed record QuoteComparison(long SupplierId, string SupplierCode, IReadOnlyDictionary<long, decimal> UnitPrices, TenderScore Score);

/// <summary>What sending a request for quotation to its suppliers did.</summary>
/// <param name="BatchId">The email batch queued, or null when no supplier had an address.</param>
/// <param name="Recipients">The addresses it was queued to.</param>
/// <param name="NoEmail">The invited suppliers (codes) with nobody to email.</param>
public sealed record RfqDespatch(long? BatchId, int Recipients, IReadOnlyList<string> NoEmail);

/// <summary>The comparison matrix and adjudication record of a request for quotation.</summary>
/// <param name="RfqNumber">The request.</param>
/// <param name="Quotes">The quotes, best first.</param>
/// <param name="NotQuoted">Invited suppliers who have not quoted.</param>
/// <param name="AwardedSupplierId">The supplier awarded, once awarded.</param>
/// <param name="AwardReason">Why the award passed over the best quote, if it did.</param>
public sealed record RfqComparison(string RfqNumber, IReadOnlyList<QuoteComparison> Quotes, IReadOnlyList<long> NotQuoted, long? AwardedSupplierId, string? AwardReason);

/// <summary>
/// Requests for quotation and tenders (brief §4.3): issue requirements to invited suppliers, capture their quotes,
/// compare and score them (price and lead time), and award one as a purchase order, with an adjudication trail.
/// </summary>
public interface IRfqService
{
    /// <summary>Issues a request for quotation to the invited suppliers.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The request id, or why it was refused.</returns>
    Task<Result<long>> IssueAsync(long companyId, RfqRequest request, CancellationToken ct = default);

    /// <summary>Records a quote: only from an invited supplier, by the closing date, pricing every requirement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="rfqId">The request.</param>
    /// <param name="quote">The quote.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RecordQuoteAsync(long companyId, long rfqId, QuoteRequest quote, CancellationToken ct = default);

    /// <summary>The printable request for quotation: the items and quantities wanted, the closing date and a column for the supplier's prices.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="rfqId">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document text, or why there is none.</returns>
    Task<Result<string>> RenderAsync(long companyId, long rfqId, CancellationToken ct = default);

    /// <summary>
    /// Emails the request (as an attached document) to every invited supplier's order contacts (else the primary contact), reporting the
    /// suppliers with nobody to email. Only an open request is sent.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="rfqId">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was queued, or why it was refused.</returns>
    Task<Result<RfqDespatch>> SendAsync(long companyId, long rfqId, CancellationToken ct = default);

    /// <summary>The comparison matrix: every quote scored and ranked, and who has not quoted.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="rfqId">The request.</param>
    /// <param name="priceWeight">The points for price, out of 100; the rest are for lead time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The comparison, or why there is none.</returns>
    Task<Result<RfqComparison>> CompareAsync(long companyId, long rfqId, decimal priceWeight = TenderEvaluation.DefaultPriceWeight, CancellationToken ct = default);

    /// <summary>
    /// Awards the request to a quoting supplier, raising a purchase order at the quoted prices. Passing over the
    /// best-scored quote needs a reason, recorded for the adjudication report.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="rfqId">The request.</param>
    /// <param name="supplierId">The supplier awarded.</param>
    /// <param name="orderDate">The purchase order date.</param>
    /// <param name="userId">Who makes the award.</param>
    /// <param name="reason">Why, when not the best-scored quote.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The purchase order, or why the award was refused.</returns>
    Task<Result<PurchaseOrderResult>> AwardAsync(long companyId, long rfqId, long supplierId, DateOnly orderDate, long userId, string? reason, CancellationToken ct = default);
}
