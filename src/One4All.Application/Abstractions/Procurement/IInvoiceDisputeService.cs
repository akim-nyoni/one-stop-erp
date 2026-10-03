using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>What became of a submitted supplier invoice.</summary>
/// <param name="Match">The posting, when it matched.</param>
/// <param name="DisputeId">The queued dispute, when it did not.</param>
/// <param name="Reason">Why it was disputed.</param>
public sealed record InvoiceSubmission(MatchInvoiceResult? Match, long? DisputeId, string? Reason)
{
    /// <summary>Whether the invoice matched and posted.</summary>
    public bool Matched => Match is not null;
}

/// <summary>A disputed invoice in the query queue.</summary>
/// <param name="DisputeId">The dispute.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="InvoiceNumber">The supplier's invoice number.</param>
/// <param name="InvoiceDate">The invoice date.</param>
/// <param name="ReasonCode">The match failure code.</param>
/// <param name="Reason">The match failure in words.</param>
/// <param name="InvoicedValue">The invoice value as presented.</param>
public sealed record OpenDispute(long DisputeId, long SupplierId, string InvoiceNumber, DateOnly InvoiceDate, string ReasonCode, string Reason, decimal InvoicedValue);

/// <summary>
/// The invoice query / dispute queue (brief §4.3): supplier invoices go through the three-way match, and one outside
/// tolerance is queued with why instead of being lost; the queue is then worked by accepting an invoice as presented
/// (a price difference, approved and recorded) or rejecting it back to the supplier.
/// </summary>
public interface IInvoiceDisputeService
{
    /// <summary>Matches an invoice, queueing it as a dispute when the price or quantity is out of line.</summary>
    /// <param name="request">The invoice.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting or the dispute; an error only for invoices that cannot be queued (unknown order, say).</returns>
    Task<Result<InvoiceSubmission>> SubmitAsync(MatchInvoiceRequest request, CancellationToken ct = default);

    /// <summary>The open disputes, oldest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The queue.</returns>
    Task<IReadOnlyList<OpenDispute>> OpenAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Accepts a price dispute: the invoice posts as presented, whatever its price difference, with the decision recorded.
    /// A quantity dispute cannot be accepted until the goods are received.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="disputeId">The dispute.</param>
    /// <param name="userId">Who accepts it.</param>
    /// <param name="note">Why; required.</param>
    /// <param name="priceVarianceAccountCode">Where the price difference goes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting, or why it was refused.</returns>
    Task<Result<MatchInvoiceResult>> AcceptAsync(long companyId, long disputeId, long userId, string note, string priceVarianceAccountCode, CancellationToken ct = default);

    /// <summary>Rejects a disputed invoice back to the supplier.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="disputeId">The dispute.</param>
    /// <param name="userId">Who rejects it.</param>
    /// <param name="note">Why; required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RejectAsync(long companyId, long disputeId, long userId, string note, CancellationToken ct = default);
}
