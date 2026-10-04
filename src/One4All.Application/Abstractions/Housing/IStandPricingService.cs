using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A pricing rule to propose.</summary>
public sealed class PricingRuleInput
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>What it prices.</summary>
    public PricingScope Scope { get; init; }

    /// <summary>The scheme (scheme and size-band rules).</summary>
    public long? SchemeId { get; init; }

    /// <summary>The smallest stand in the band.</summary>
    public decimal? MinSize { get; init; }

    /// <summary>The largest stand in the band.</summary>
    public decimal? MaxSize { get; init; }

    /// <summary>The stand (stand rules).</summary>
    public long? StandId { get; init; }

    /// <summary>When it takes effect.</summary>
    public DateOnly EffectiveFrom { get; init; }

    /// <summary>Overhead / administration recovery on cost (%).</summary>
    public decimal OverheadPercent { get; init; }

    /// <summary>Development levy per stand.</summary>
    public decimal LevyPerStand { get; init; }

    /// <summary>Development levy per square metre.</summary>
    public decimal LevyPerSquareMetre { get; init; }

    /// <summary>Margin on cost plus overhead (%).</summary>
    public decimal MarginPercent { get; init; }

    /// <summary>The price is rounded up to a multiple of this (0 = to the cent).</summary>
    public decimal RoundTo { get; init; }

    /// <summary>Who proposes it.</summary>
    public long UserId { get; init; }
}

/// <summary>A stand's price as quoted.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="RuleId">The rule that priced it.</param>
/// <param name="Scope">The rule's scope.</param>
/// <param name="EffectiveFrom">When the rule took effect.</param>
/// <param name="BuildUp">The price built up.</param>
public sealed record StandPriceQuote(string StandNumber, long RuleId, PricingScope Scope, DateOnly EffectiveFrom, PriceBuildUp BuildUp);

/// <summary>
/// Stand selling prices (brief §4.10.2): effective-dated rules by scheme, size band or stand, proposed and approved by
/// different officers (the approver holding the pricing Approve permission); a stand's price built up from its capitalised
/// land and servicing cost plus overhead recovery, development levy and margin; and a beneficiary's price frozen at the
/// offer date (allocation offers carry it).
/// </summary>
public interface IStandPricingService
{
    /// <summary>Proposes a rule (needs the pricing Edit permission).</summary>
    /// <param name="input">The rule.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rule id, or why it was refused.</returns>
    Task<Result<long>> ProposeAsync(PricingRuleInput input, CancellationToken ct = default);

    /// <summary>Approves a proposed rule (needs the pricing Approve permission; not the proposer).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ruleId">The rule.</param>
    /// <param name="userId">The approver.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApproveAsync(long companyId, long ruleId, long userId, DateOnly date, CancellationToken ct = default);

    /// <summary>Quotes a stand's price on a date from the approved rules.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quote, or why there is none.</returns>
    Task<Result<StandPriceQuote>> QuoteAsync(long companyId, long standId, DateOnly date, CancellationToken ct = default);
}
