using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>The terms of a new developer agreement.</summary>
public sealed class DeveloperAgreementTerms
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The scheme.</summary>
    public long SchemeId { get; init; }

    /// <summary>The developer's account (penalties are invoiced to it).</summary>
    public long DeveloperCustomerId { get; init; }

    /// <summary>The stands the developer must hand to the council.</summary>
    public int CouncilQuota { get; init; }

    /// <summary>The stands the developer keeps.</summary>
    public int DeveloperQuota { get; init; }

    /// <summary>When it was signed.</summary>
    public DateOnly SignedOn { get; init; }

    /// <summary>When the council's stands are due.</summary>
    public DateOnly DeliveryDeadline { get; init; }

    /// <summary>The penalty per council stand per month (or part) late.</summary>
    public decimal PenaltyPerStandPerMonth { get; init; }
}

/// <summary>One quota's delivery as reconciled.</summary>
/// <param name="Quota">The quota.</param>
/// <param name="Contracted">Stands contracted.</param>
/// <param name="Assigned">Stands assigned to it.</param>
/// <param name="HandedOver">Stands handed over by the date.</param>
/// <param name="Outstanding">Contracted stands not yet handed over.</param>
public sealed record QuotaDelivery(StandQuota Quota, int Contracted, int Assigned, int HandedOver, int Outstanding);

/// <summary>A developer agreement reconciled as at a date.</summary>
/// <param name="AgreementNumber">The agreement.</param>
/// <param name="Quotas">Delivery by quota.</param>
/// <param name="OverdueMilestones">Milestones due by the date and not achieved.</param>
/// <param name="PenaltyAccrued">The penalty accrued on late council stands.</param>
/// <param name="PenaltiesCharged">The penalties already invoiced.</param>
public sealed record DeveloperReconciliation(string AgreementNumber, IReadOnlyList<QuotaDelivery> Quotas, IReadOnlyList<string> OverdueMilestones, decimal PenaltyAccrued, decimal PenaltiesCharged);

/// <summary>
/// Developer / partner agreements (brief §4.10.8): the council contributes a scheme's land and a developer services and
/// builds; the stands are split between the council's and the developer's quotas; delivery milestones are tracked; the
/// stands handed over are reconciled against those contracted; and late council stands attract an invoiced penalty.
/// </summary>
public interface IDeveloperAgreementService
{
    /// <summary>Records an agreement.</summary>
    /// <param name="terms">The terms.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agreement number, or why it was refused.</returns>
    Task<Result<string>> CreateAsync(DeveloperAgreementTerms terms, CancellationToken ct = default);

    /// <summary>Adds a delivery milestone.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="description">What is to be delivered.</param>
    /// <param name="dueOn">When it is due.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AddMilestoneAsync(long companyId, string agreementNumber, string description, DateOnly dueOn, CancellationToken ct = default);

    /// <summary>Records a milestone achieved.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="description">The milestone.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CompleteMilestoneAsync(long companyId, string agreementNumber, string description, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Assigns scheme stands to a quota; they are blocked from any other allocation. Refused when a stand is outside the
    /// scheme, not available, already promised elsewhere, or the quota would be exceeded.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="quota">The quota.</param>
    /// <param name="standIds">The stands.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number assigned, or why it was refused.</returns>
    Task<Result<int>> AssignStandsAsync(long companyId, string agreementNumber, StandQuota quota, IReadOnlyList<long> standIds, DateOnly date, CancellationToken ct = default);

    /// <summary>Records a stand handed over; a council stand then returns to the council's pool for allocation.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> HandOverAsync(long companyId, string agreementNumber, long standId, DateOnly date, CancellationToken ct = default);

    /// <summary>Reconciles stands handed over against those contracted, with overdue milestones and the penalty, as at a date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reconciliation, or why there is none.</returns>
    Task<Result<DeveloperReconciliation>> ReconcileAsync(long companyId, string agreementNumber, DateOnly asAt, CancellationToken ct = default);

    /// <summary>Invoices the developer the penalty accrued since the last charge (nothing when nothing new has accrued).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="asAt">The assessment date (the invoice date).</param>
    /// <param name="penaltyIncomeAccountCode">The income account for penalties.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The amount invoiced now, or why it was refused.</returns>
    Task<Result<decimal>> ChargePenaltiesAsync(long companyId, string agreementNumber, DateOnly asAt, string penaltyIncomeAccountCode, CancellationToken ct = default);
}
