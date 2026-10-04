using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>Completion of a repair.</summary>
public sealed class RepairCompletion
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The repair.</summary>
    public string RepairNumber { get; init; } = string.Empty;

    /// <summary>When it was completed.</summary>
    public DateOnly Date { get; init; }

    /// <summary>What it cost.</summary>
    public decimal Cost { get; init; }

    /// <summary>The repairs expense account.</summary>
    public string ExpenseAccountCode { get; init; } = string.Empty;

    /// <summary>The bank account (or creditor control) the cost was paid from.</summary>
    public string PaidFromAccountCode { get; init; } = string.Empty;

    /// <summary>Whether the tenant caused the damage (the cost is then recharged to the tenant).</summary>
    public bool TenantCaused { get; init; }

    /// <summary>The account the recharge is credited to (needed for a recharge).</summary>
    public string? RechargeAccountCode { get; init; }
}

/// <summary>A unit's maintenance history.</summary>
/// <param name="UnitCode">The unit.</param>
/// <param name="RepairsOpen">Repairs reported and not yet done.</param>
/// <param name="RepairCost">What completed repairs cost.</param>
/// <param name="Recharged">What was recharged to tenants.</param>
/// <param name="LatestConditionGrade">The latest inspection's grade, if inspected.</param>
/// <param name="FollowUpsOpen">Inspection follow-ups still open.</param>
public sealed record UnitMaintenanceHistory(string UnitCode, int RepairsOpen, decimal RepairCost, decimal Recharged, int? LatestConditionGrade, int FollowUpsOpen);

/// <summary>
/// Maintenance and inspections of council rental units (brief §4.10.7): faults reported against a unit; repairs
/// completed and costed (Dr repairs / Cr bank), recharged to the tenant through AR where the tenant caused the damage;
/// inspections with a condition grade and follow-up actions; and the cost history per unit.
/// </summary>
public interface IRentalMaintenanceService
{
    /// <summary>Records a fault against a unit, linked to its running tenancy if let.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="unitCode">The unit.</param>
    /// <param name="reportedOn">When it was reported.</param>
    /// <param name="description">The fault.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The repair number, or why it was refused.</returns>
    Task<Result<string>> ReportRepairAsync(long companyId, string unitCode, DateOnly reportedOn, string description, CancellationToken ct = default);

    /// <summary>Completes a repair: books its cost and, if tenant-caused, recharges it to the tenant. One unit.</summary>
    /// <param name="completion">The completion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recharge invoice number if recharged, or why it was refused.</returns>
    Task<Result<string?>> CompleteRepairAsync(RepairCompletion completion, CancellationToken ct = default);

    /// <summary>Records an inspection.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="unitCode">The unit.</param>
    /// <param name="date">When.</param>
    /// <param name="inspector">Who inspected.</param>
    /// <param name="conditionGrade">The condition, 1 (poor) to 5 (excellent).</param>
    /// <param name="findings">What was found.</param>
    /// <param name="followUpRequired">Whether action is needed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The inspection, or why it was refused.</returns>
    Task<Result<long>> RecordInspectionAsync(long companyId, string unitCode, DateOnly date, string inspector, int conditionGrade, string findings, bool followUpRequired, CancellationToken ct = default);

    /// <summary>Closes an inspection's follow-up.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="inspectionId">The inspection.</param>
    /// <param name="date">When the follow-up was done.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CloseFollowUpAsync(long companyId, long inspectionId, DateOnly date, CancellationToken ct = default);

    /// <summary>A unit's maintenance history.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="unitCode">The unit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The history, or why there is none.</returns>
    Task<Result<UnitMaintenanceHistory>> HistoryAsync(long companyId, string unitCode, CancellationToken ct = default);
}
