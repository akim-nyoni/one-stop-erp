using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The revaluation of one foreign-currency monetary account.</summary>
/// <param name="AccountCode">The account revalued.</param>
/// <param name="Currency">The account's foreign currency.</param>
/// <param name="ForeignBalance">The net foreign-currency balance.</param>
/// <param name="ClosingRate">The closing rate applied.</param>
/// <param name="BaseBefore">The base balance before revaluation.</param>
/// <param name="BaseAfter">The base balance after revaluation.</param>
/// <param name="Difference">The unrealised gain (positive) or loss (negative).</param>
public readonly record struct AccountRevaluation(string AccountCode, string Currency, decimal ForeignBalance, decimal ClosingRate, decimal BaseBefore, decimal BaseAfter, decimal Difference);

/// <summary>The result of a period-end FX revaluation run.</summary>
/// <param name="AsAtDate">The revaluation date.</param>
/// <param name="Accounts">The per-account revaluations posted.</param>
/// <param name="JournalNumber">The revaluation journal number, or null when nothing needed revaluing.</param>
/// <param name="ReversalJournalNumber">The auto-reversal journal number, when auto-reverse was requested.</param>
public sealed record FxRevaluationResult(DateOnly AsAtDate, IReadOnlyList<AccountRevaluation> Accounts, string? JournalNumber, string? ReversalJournalNumber)
{
    /// <summary>The net unrealised gain (positive) or loss (negative) across all accounts.</summary>
    public decimal NetDifference => Accounts.Sum(a => a.Difference);
}

/// <summary>
/// Period-end revaluation of open foreign-currency monetary items (brief §3.3, IAS 21). Retranslates
/// each foreign monetary account at the closing rate and posts the unrealised FX gain/loss, optionally as
/// an auto-reversing journal in the next period. Non-monetary items (stock, fixed assets, prepayments)
/// are never revalued.
/// </summary>
public interface IFxRevaluationService
{
    /// <summary>Runs the revaluation as at a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The revaluation (closing) date.</param>
    /// <param name="autoReverse">When true, posts an auto-reversing journal in the next period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The revaluation result, or an error.</returns>
    Task<Result<FxRevaluationResult>> RevalueAsync(long companyId, DateOnly asAtDate, bool autoReverse = true, CancellationToken ct = default);
}
