using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A request to repossess a stand sold on instalments.</summary>
public sealed class RepossessionRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The instalment agreement in default.</summary>
    public string AgreementNumber { get; init; } = string.Empty;

    /// <summary>The repossession date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The stand's net realisable value now.</summary>
    public decimal NetRealisableValue { get; init; }

    /// <summary>Why it is repossessed.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Who requested it.</summary>
    public long RequestedByUserId { get; init; }

    /// <summary>Who approved it (not the requester).</summary>
    public long ApprovedByUserId { get; init; }

    /// <summary>The stands-available inventory account the stand returns to.</summary>
    public string StandsAvailableAccountCode { get; init; } = string.Empty;

    /// <summary>The repossession gain/loss account (separately reportable).</summary>
    public string GainLossAccountCode { get; init; } = string.Empty;
}

/// <summary>A request to take back a stand its holder surrenders to council.</summary>
public sealed class StandSurrenderRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The stand.</summary>
    public string StandNumber { get; init; } = string.Empty;

    /// <summary>The surrender date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Why it is surrendered.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Who requested it.</summary>
    public long RequestedByUserId { get; init; }

    /// <summary>Who approved it (not the requester).</summary>
    public long ApprovedByUserId { get; init; }

    /// <summary>A sold stand's net realisable value now (it returns to inventory at the lower of cost and this).</summary>
    public decimal NetRealisableValue { get; init; }

    /// <summary>The stands-available inventory account a sold stand returns to.</summary>
    public string StandsAvailableAccountCode { get; init; } = string.Empty;

    /// <summary>The surrender gain/loss account (the receivable written off and the stand re-recognised).</summary>
    public string GainLossAccountCode { get; init; } = string.Empty;

    /// <summary>What is refunded to the holder (credited to their account), zero for none.</summary>
    public decimal Refund { get; init; }

    /// <summary>The account the refund is charged to (required with a refund).</summary>
    public string? RefundAccountCode { get; init; }
}

/// <summary>A surrender made or on the register.</summary>
/// <param name="Reference">The reference.</param>
/// <param name="StandNumber">The stand.</param>
/// <param name="Holder">The holder (customer code, or the allocatee's name).</param>
/// <param name="Date">The date.</param>
/// <param name="ReceivableDerecognised">The receivable written off.</param>
/// <param name="InventoryRecognised">What a sold stand came back into inventory at.</param>
/// <param name="Refund">What was credited to the holder.</param>
public sealed record StandSurrenderEntry(string Reference, string StandNumber, string Holder, DateOnly Date, decimal ReceivableDerecognised, decimal InventoryRecognised, decimal Refund);

/// <summary>A repossession made or on the register.</summary>
/// <param name="Reference">The reference.</param>
/// <param name="AgreementNumber">The agreement.</param>
/// <param name="StandNumber">The stand.</param>
/// <param name="Date">The date.</param>
/// <param name="ReceivableDerecognised">The receivable written off.</param>
/// <param name="InventoryRecognised">The amount the stand returned to inventory at.</param>
/// <param name="GainOrLoss">The gain (positive) or loss (negative).</param>
public sealed record RepossessionEntry(string Reference, string AgreementNumber, string StandNumber, DateOnly Date, decimal ReceivableDerecognised, decimal InventoryRecognised, decimal GainOrLoss);

/// <summary>
/// Repossession of stands sold on instalments (brief §4.10.5, accounted per §4.10.9): with approval from someone other
/// than the requester, the buyer's receivable is derecognised, the stand returns to inventory at the lower of its
/// previous carrying amount and net realisable value and to the available pool, the agreement closes, and the
/// difference goes to a repossession gain/loss account; the repossessions register reports the figures.
/// </summary>
public interface IRepossessionService
{
    /// <summary>Repossesses a stand, in one unit.</summary>
    /// <param name="request">The repossession.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The repossession, or why it was refused.</returns>
    Task<Result<RepossessionEntry>> RepossessAsync(RepossessionRequest request, CancellationToken ct = default);

    /// <summary>The repossessions in a period, in date order.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="start">The first day.</param>
    /// <param name="end">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The register.</returns>
    Task<IReadOnlyList<RepossessionEntry>> RegisterAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default);

    /// <summary>
    /// Takes back a stand its holder surrenders, in one unit, approved by someone other than the requester: an instalment agreement closes and its
    /// receivable is derecognised, a sold stand returns to inventory at the lower of cost and net realisable value, any refund is credited to the holder,
    /// and the surrender joins the ownership chain and the register.
    /// </summary>
    /// <param name="request">The surrender.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The surrender, or why it was refused.</returns>
    Task<Result<StandSurrenderEntry>> SurrenderAsync(StandSurrenderRequest request, CancellationToken ct = default);

    /// <summary>The surrenders in a period, in date order.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="start">The first day.</param>
    /// <param name="end">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The register.</returns>
    Task<IReadOnlyList<StandSurrenderEntry>> SurrenderRegisterAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default);
}
