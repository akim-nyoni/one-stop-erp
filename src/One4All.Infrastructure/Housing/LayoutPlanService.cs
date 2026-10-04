using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>The layout plan register: versions, approvals, amendments and reconciliation to the stand register.</summary>
public sealed class LayoutPlanService : ILayoutPlanService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public LayoutPlanService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SubmitAsync(LayoutSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        string? problem = Problem(submission);
        if (problem is not null)
        {
            return Result.Failure(Error.Validation("Hse.Layout", problem));
        }

        string number = submission.PlanNumber.Trim().ToUpperInvariant();
        if (!await _db.LandParcels.AnyAsync(p => p.Id == submission.LandParcelId && p.CompanyId == submission.CompanyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Hse.ParcelNotFound", $"Parcel {submission.LandParcelId} not found."));
        }

        if (await _db.Set<LayoutPlan>().AnyAsync(p => p.CompanyId == submission.CompanyId && (p.PlanNumber == number || p.LandParcelId == submission.LandParcelId), ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.LayoutExists", $"Layout {number} already exists, or the parcel already has a layout; amend it instead."));
        }

        LayoutPlan plan = new(submission.CompanyId, number, submission.LandParcelId);
        plan.AddVersion(Version(1, submission, null));
        _db.Set<LayoutPlan>().Add(plan);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ApproveAsync(long companyId, string planNumber, string authority, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return Result.Failure(Error.Validation("Hse.LayoutAuthority", "Name the approving authority."));
        }

        Result<LayoutPlan> found = await FindAsync(companyId, planNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        LayoutPlanVersion latest = found.Value.Latest;
        if (latest.ApprovedOn is not null)
        {
            return Result.Failure(Error.Rule("Hse.LayoutApproved", $"Version {latest.Version} of {found.Value.PlanNumber} is already approved; nothing awaits approval."));
        }

        if (date < latest.SubmittedOn)
        {
            return Result.Failure(Error.Validation("Hse.LayoutDate", "A layout cannot be approved before it was submitted."));
        }

        latest.Approve(authority.Trim(), date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> AmendAsync(LayoutSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        string? problem = Problem(submission) ?? (string.IsNullOrWhiteSpace(submission.AmendmentReason) ? "An amendment needs its reason." : null);
        if (problem is not null)
        {
            return Error.Validation("Hse.Layout", problem);
        }

        Result<LayoutPlan> found = await FindAsync(submission.CompanyId, submission.PlanNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        LayoutPlan plan = found.Value;
        if (plan.Latest.ApprovedOn is null)
        {
            return Error.Rule("Hse.LayoutPending", $"Version {plan.Latest.Version} of {plan.PlanNumber} still awaits approval.");
        }

        int next = plan.Latest.Version + 1;
        plan.AddVersion(Version(next, submission, submission.AmendmentReason!.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return next;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LayoutVersionEntry>>> HistoryAsync(long companyId, string planNumber, CancellationToken ct = default)
    {
        Result<LayoutPlan> found = await FindAsync(companyId, planNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        List<LayoutVersionEntry> entries = [];
        IReadOnlyDictionary<StandUse, int>? previous = null;
        foreach (LayoutPlanVersion version in found.Value.Versions)
        {
            Dictionary<StandUse, int> uses = Uses(version);
            entries.Add(new LayoutVersionEntry(version.Version, version.Planner, version.Density, version.SubmittedOn, version.ApprovedOn, version.ApprovalAuthority,
                version.AmendmentReason, LayoutChanges.Stands(uses), previous is null ? [] : LayoutChanges.Between(previous, uses)));
            previous = uses;
        }

        return entries;
    }

    /// <inheritdoc />
    public async Task<Result<LayoutReconciliation>> ReconcileAsync(long companyId, string planNumber, CancellationToken ct = default)
    {
        Result<LayoutPlan> found = await FindAsync(companyId, planNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        LayoutPlanVersion? inForce = found.Value.InForce;
        if (inForce is null)
        {
            return Error.Rule("Hse.LayoutNotApproved", $"{found.Value.PlanNumber} has no approved version yet.");
        }

        long parcelId = found.Value.LandParcelId;
        int planned = LayoutChanges.Stands(Uses(inForce));
        int onRegister = await _db.HousingStands.CountAsync(s => s.CompanyId == companyId && s.LandParcelId == parcelId, ct).ConfigureAwait(false);
        return new LayoutReconciliation(found.Value.PlanNumber, inForce.Version, planned, onRegister, onRegister - planned);
    }

    private static string? Problem(LayoutSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.PlanNumber) || string.IsNullOrWhiteSpace(submission.Planner) || !Enum.IsDefined(submission.Density))
        {
            return "A layout needs its number, planner and density.";
        }

        if (submission.Uses is null || submission.Uses.Count == 0 || submission.Uses.Any(u => !Enum.IsDefined(u.Key) || u.Value < 0) || LayoutChanges.Stands(submission.Uses) == 0)
        {
            return "A layout needs stands by use, none negative.";
        }

        return null;
    }

    private static LayoutPlanVersion Version(int number, LayoutSubmission submission, string? reason)
    {
        LayoutPlanVersion version = new(number, submission.Planner.Trim(), submission.Density, submission.Date, reason);
        foreach (KeyValuePair<StandUse, int> use in submission.Uses.Where(u => u.Value > 0).OrderBy(u => u.Key))
        {
            version.SetUse(use.Key, use.Value);
        }

        return version;
    }

    private static Dictionary<StandUse, int> Uses(LayoutPlanVersion version) => version.Uses.ToDictionary(u => u.Use, u => u.Stands);

    private async Task<Result<LayoutPlan>> FindAsync(long companyId, string planNumber, CancellationToken ct)
    {
        string number = (planNumber ?? string.Empty).Trim().ToUpperInvariant();
        LayoutPlan? plan = await _db.Set<LayoutPlan>().Include(p => p.Versions).ThenInclude(v => v.Uses)
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.PlanNumber == number, ct).ConfigureAwait(false);
        if (plan is null)
        {
            return Error.NotFound("Hse.LayoutNotFound", $"Layout {number} not found.");
        }

        return plan;
    }
}
