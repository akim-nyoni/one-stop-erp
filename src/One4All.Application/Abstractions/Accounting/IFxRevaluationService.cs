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
/// <param name="JournalNumbers">The revaluation journals, one per foreign currency revalued (empty when nothing needed revaluing).</param>
/// <param name="ReversalJournalNumbers">The auto-reversal journals, one per revaluation journal, when auto-reverse was requested.</param>
public sealed record FxRevaluationResult(DateOnly AsAtDate, IReadOnlyList<AccountRevaluation> Accounts, IReadOnlyList<string> JournalNumbers, IReadOnlyList<string> ReversalJournalNumbers)
{
    /// <summary>The first revaluation journal, or null when nothing needed revaluing.</summary>
    public string? JournalNumber => JournalNumbers.Count > 0 ? JournalNumbers[0] : null;

    /// <summary>The first auto-reversal journal, or null.</summary>
    public string? ReversalJournalNumber => ReversalJournalNumbers.Count > 0 ? ReversalJournalNumbers[0] : null;

    /// <summary>The net unrealised gain (positive) or loss (negative) across all accounts.</summary>
    public decimal NetDifference => Accounts.Sum(a => a.Difference);
}

/// <summary>
/// Period-end revaluation of open foreign-currency monetary items (brief §3.3, IAS 21). Retranslates
/// each foreign monetary account at the closing rate and posts the unrealised FX gain/loss, one journal per foreign
/// currency (a currency-restricted account only takes its own currency), optionally auto-reversing the next day. Non-monetary items (stock, fixed assets, prepayments)
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
