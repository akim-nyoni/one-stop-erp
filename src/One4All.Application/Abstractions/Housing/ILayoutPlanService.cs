using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A layout (or an amendment of one) as submitted.</summary>
public sealed class LayoutSubmission
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The layout number.</summary>
    public string PlanNumber { get; init; } = string.Empty;

    /// <summary>The parcel (on a first submission).</summary>
    public long LandParcelId { get; init; }

    /// <summary>The planner.</summary>
    public string Planner { get; init; } = string.Empty;

    /// <summary>The density.</summary>
    public PlanDensity Density { get; init; }

    /// <summary>The stands by use.</summary>
    public IReadOnlyDictionary<StandUse, int> Uses { get; init; } = new Dictionary<StandUse, int>();

    /// <summary>When it is submitted.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Why it amends the plan (required on an amendment).</summary>
    public string? AmendmentReason { get; init; }
}

/// <summary>A layout version in the plan's history.</summary>
/// <param name="Version">The version.</param>
/// <param name="Planner">The planner.</param>
/// <param name="Density">The density.</param>
/// <param name="SubmittedOn">When it was submitted.</param>
/// <param name="ApprovedOn">When it was approved.</param>
/// <param name="ApprovalAuthority">Who approved it.</param>
/// <param name="AmendmentReason">Why it amended the previous version.</param>
/// <param name="Stands">The stands it creates.</param>
/// <param name="Changes">What it changed from the previous version.</param>
public sealed record LayoutVersionEntry(int Version, string Planner, PlanDensity Density, DateOnly SubmittedOn, DateOnly? ApprovedOn, string? ApprovalAuthority,
    string? AmendmentReason, int Stands, IReadOnlyList<LayoutChange> Changes);

/// <summary>The plan in force against the stands actually created on the parcel.</summary>
/// <param name="PlanNumber">The plan.</param>
/// <param name="VersionInForce">The approved version in force.</param>
/// <param name="PlannedStands">The stands it provides for.</param>
/// <param name="StandsOnRegister">The stands on the register for the parcel.</param>
/// <param name="Difference">Register less plan: positive when more stands exist than the plan allows.</param>
public sealed record LayoutReconciliation(string PlanNumber, int VersionInForce, int PlannedStands, int StandsOnRegister, int Difference);

/// <summary>
/// The layout plan register (brief §4.10.2): a parcel's layout submitted with its planner, density and stands by use,
/// approved by the planning authority, amended and re-approved as new versions with the history and what each changed
/// kept, and reconciled against the stands actually created on the parcel.
/// </summary>
public interface ILayoutPlanService
{
    /// <summary>Submits a parcel's first layout.</summary>
    /// <param name="submission">The layout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SubmitAsync(LayoutSubmission submission, CancellationToken ct = default);

    /// <summary>Approves the version awaiting approval.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="planNumber">The plan.</param>
    /// <param name="authority">The approving authority.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApproveAsync(long companyId, string planNumber, string authority, DateOnly date, CancellationToken ct = default);

    /// <summary>Submits an amendment of an approved plan as a new version; the approved version stays in force until it is approved.</summary>
    /// <param name="submission">The amended layout (its parcel is ignored).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new version number, or why it was refused.</returns>
    Task<Result<int>> AmendAsync(LayoutSubmission submission, CancellationToken ct = default);

    /// <summary>The plan's versions with what each changed.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="planNumber">The plan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The history, or why there is none.</returns>
    Task<Result<IReadOnlyList<LayoutVersionEntry>>> HistoryAsync(long companyId, string planNumber, CancellationToken ct = default);

    /// <summary>Compares the plan in force with the stands on the register for the parcel.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="planNumber">The plan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reconciliation, or why there is none.</returns>
    Task<Result<LayoutReconciliation>> ReconcileAsync(long companyId, string planNumber, CancellationToken ct = default);
}
