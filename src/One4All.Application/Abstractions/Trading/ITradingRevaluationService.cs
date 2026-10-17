using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>One foreign-currency open item's revaluation at the closing rate.</summary>
/// <param name="OpenItemId">The open item.</param>
/// <param name="DocumentNumber">Its document number.</param>
/// <param name="TraderId">The customer or supplier.</param>
/// <param name="Currency">Its currency.</param>
/// <param name="OutstandingForeign">Outstanding in its currency at the date, in the ledger's natural sign.</param>
/// <param name="BookedBase">Outstanding in base currency at the historic rates it was booked at, natural sign.</param>
/// <param name="ClosingRate">The closing rate applied.</param>
/// <param name="RevaluedBase">The outstanding retranslated at the closing rate, natural sign.</param>
/// <param name="Difference">Revalued − booked: positive increases the control balance.</param>
public readonly record struct OpenItemRevaluation(long OpenItemId, string DocumentNumber, long TraderId, string Currency, decimal OutstandingForeign, decimal BookedBase, decimal ClosingRate, decimal RevaluedBase, decimal Difference);

/// <summary>The result of revaluing a trading ledger's foreign-currency balances.</summary>
/// <param name="AsAtDate">The revaluation date.</param>
/// <param name="Ledger">The ledger revalued.</param>
/// <param name="Items">The per-item revaluations.</param>
/// <param name="TargetRevaluation">The total unrealised difference the control account should carry at the date.</param>
/// <param name="AlreadyBooked">The unrealised revaluation already on the control account at the date (from an earlier run).</param>
/// <param name="JournalNumber">The revaluation journal, or null when nothing needed posting.</param>
/// <param name="ReversalJournalNumber">The next-day reversal journal, or null.</param>
public sealed record TradingRevaluationResult(DateOnly AsAtDate, TradingLedger Ledger, IReadOnlyList<OpenItemRevaluation> Items, decimal TargetRevaluation, decimal AlreadyBooked, string? JournalNumber, string? ReversalJournalNumber)
{
    /// <summary>The amount posted by this run (target less already booked).</summary>
    public decimal Posted => TargetRevaluation - AlreadyBooked;
}

/// <summary>
/// Period-end retranslation of foreign-currency receivables and payables at the closing rate (IAS 21 monetary items).
/// The open items keep their historic base amounts - allocation still realises the exchange difference against those -
/// and the unrealised difference is booked on the control account against FX gain / loss, normally reversed the next
/// day. The run posts only the difference between the target and what is already booked at the date, so repeating it
/// is harmless; the control-account reconciliation shows the booked revaluation as a reconciling item.
/// </summary>
public interface ITradingRevaluationService
{
    /// <summary>The document type of revaluation journals.</summary>
    public const string RevaluationDocumentType = "TradingRevaluation";

    /// <summary>The document type of their reversals.</summary>
    public const string ReversalDocumentType = "TradingRevaluationReversal";

    /// <summary>
    /// Works out the revaluation without posting anything: the per-item differences, the target and what is already
    /// booked. <see cref="TradingRevaluationResult.Posted"/> is then what a run would post.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">Receivables or payables.</param>
    /// <param name="asAtDate">The revaluation date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The revaluation that a run would make.</returns>
    Task<Result<TradingRevaluationResult>> PreviewAsync(long companyId, TradingLedger ledger, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Revalues a ledger's foreign-currency open items as at a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">Receivables or payables.</param>
    /// <param name="asAtDate">The revaluation date (normally a period end).</param>
    /// <param name="autoReverse">Whether to reverse the posting on the following day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The revaluation.</returns>
    Task<Result<TradingRevaluationResult>> RevalueAsync(long companyId, TradingLedger ledger, DateOnly asAtDate, bool autoReverse = true, CancellationToken ct = default);
}
