using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>One receiving cost centre of an allocation rule.</summary>
/// <param name="CostCentre">The receiving cost centre.</param>
/// <param name="Department">The receiving department, if any.</param>
/// <param name="Driver">Its driver: a percentage, headcount, floor area or usage quantity.</param>
public sealed record AllocationTargetRequest(string CostCentre, string? Department, decimal Driver);

/// <summary>A new overhead recharge rule.</summary>
public sealed record AllocationRuleRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The rule code, unique in the company.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>The rule name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The order rules run in, lowest first.</summary>
    public int Sequence { get; init; }

    /// <summary>The allocation basis.</summary>
    public AllocationBasis Basis { get; init; }

    /// <summary>The account holding the overhead.</summary>
    public string SourceAccountCode { get; init; } = string.Empty;

    /// <summary>The cost centre whose overhead is recharged.</summary>
    public string SourceCostCentre { get; init; } = string.Empty;

    /// <summary>The account receivers are charged on; null to charge them on the source account.</summary>
    public string? TargetAccountCode { get; init; }

    /// <summary>The receivers and their drivers.</summary>
    public IReadOnlyList<AllocationTargetRequest> Targets { get; init; } = [];
}

/// <summary>One receiver's share of a rule's pool.</summary>
/// <param name="CostCentre">The receiving cost centre.</param>
/// <param name="Department">The receiving department, if any.</param>
/// <param name="Driver">Its driver.</param>
/// <param name="Amount">Its share; negative when a credit pool is passed on.</param>
public sealed record AllocationShare(string CostCentre, string? Department, decimal Driver, decimal Amount);

/// <summary>What one rule recharges in a run.</summary>
/// <param name="RuleCode">The rule.</param>
/// <param name="RuleName">Its name.</param>
/// <param name="Basis">Its basis.</param>
/// <param name="Pool">The net cost in the source cost centre to recharge, including what earlier rules in the run passed into it.</param>
/// <param name="Shares">The receivers' shares; empty when the pool is nil.</param>
/// <param name="JournalNumber">The recharge journal, once run; null for a preview or a nil pool.</param>
public sealed record AllocationRuleRun(string RuleCode, string RuleName, AllocationBasis Basis, decimal Pool, IReadOnlyList<AllocationShare> Shares, string? JournalNumber);

/// <summary>The outcome of an allocation run or preview for a period.</summary>
/// <param name="PeriodStart">The first day of the period.</param>
/// <param name="PeriodEnd">The last day of the period; the recharge journals are dated here.</param>
/// <param name="Rules">Each active rule, in sequence order.</param>
public sealed record AllocationRunResult(DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<AllocationRuleRun> Rules)
{
    /// <summary>The total recharged across all rules.</summary>
    public decimal TotalRecharged => Rules.Sum(r => r.Pool);
}

/// <summary>
/// Overhead recharge rules run as a period-end routine (brief §4.4): each active rule, in sequence order, takes the net
/// cost booked in the period to its source account in its source cost centre, credits it out and charges it to the
/// receiving cost centres by fixed percentage, headcount, floor area or usage. A later rule's pool includes what earlier
/// rules recharged into it (step-down). A rerun recharges only what has been booked to a pool since, so repeating it
/// posts nothing.
/// </summary>
public interface IOverheadAllocationService
{
    /// <summary>Creates a rule.</summary>
    /// <param name="request">The rule.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rule id, or why it is invalid.</returns>
    Task<Result<long>> CreateRuleAsync(AllocationRuleRequest request, CancellationToken ct = default);

    /// <summary>Replaces a rule's receivers and drivers, say with this month's usage.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ruleId">The rule.</param>
    /// <param name="targets">The receivers and drivers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why they are invalid.</returns>
    Task<Result> SetDriversAsync(long companyId, long ruleId, IReadOnlyList<AllocationTargetRequest> targets, CancellationToken ct = default);

    /// <summary>Shows what a run for the period containing a date would recharge, without posting.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodDate">Any date in the period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The preview.</returns>
    Task<Result<AllocationRunResult>> PreviewAsync(long companyId, DateOnly periodDate, CancellationToken ct = default);

    /// <summary>Runs the rules for the period containing a date: one recharge journal per rule with a pool, in one unit.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodDate">Any date in the period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<AllocationRunResult>> RunAsync(long companyId, DateOnly periodDate, CancellationToken ct = default);
}
