using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Security;
using One4All.Application.Reporting;
using One4All.Core.Common;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Runs reports through the generic runner with permission checks and an append-only run log.</summary>
public sealed class AuditedReportRunner : IAuditedReportRunner
{
    private const string ReportPermission = "Reporting.Report";

    private readonly One4AllDbContext _db;
    private readonly IReportRunner _runner;
    private readonly IPermissionService _permissions;

    /// <summary>Creates the runner.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="runner">The generic report runner.</param>
    /// <param name="permissions">The permission resolver.</param>
    public AuditedReportRunner(One4AllDbContext db, IReportRunner runner, IPermissionService permissions)
    {
        _db = db;
        _runner = runner;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<Result<AuditedReport>> RunAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format, ReportRunPurpose purpose, long? userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        string code = (reportCode ?? string.Empty).Trim();
        string canonical = ReportRunAudit.Canonical(parameters);
        int earlierPrints = purpose is ReportRunPurpose.Print or ReportRunPurpose.Reprint
            ? await _db.Set<ReportRun>().CountAsync(r => r.CompanyId == companyId && r.ReportCode == code && r.Parameters == canonical
                && (r.Purpose == ReportRunPurpose.Print || r.Purpose == ReportRunPurpose.Reprint), ct).ConfigureAwait(false)
            : 0;
        (ReportRunPurpose recorded, int? copy) = ReportRunAudit.Classify(purpose, earlierPrints);

        if (userId is long user && !(await _permissions.ResolveAsync(user, ct).ConfigureAwait(false)).Has(ReportPermission, ReportRunAudit.RequiredFlag(recorded)))
        {
            return Error.Rule("Rpt.NotAuthorised", $"You may not {recorded.ToString().ToLowerInvariant()} reports.");
        }

        Result<RenderedDocument> rendered = await _runner.RenderAsync(companyId, code, parameters, format, ct).ConfigureAwait(false);
        if (rendered.IsFailure)
        {
            return rendered.Error;
        }

        _db.Set<ReportRun>().Add(new ReportRun(companyId, code, userId, recorded, format.ToString(), canonical, DateTimeOffset.UtcNow, rendered.Value.Content.Length,
            ReportRunAudit.Fingerprint(rendered.Value.Content), copy));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AuditedReport(rendered.Value, recorded, copy);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportRunEntry>> RunLogAsync(long companyId, string reportCode, CancellationToken ct = default)
    {
        string code = (reportCode ?? string.Empty).Trim();
        return await _db.Set<ReportRun>().AsNoTracking().Where(r => r.CompanyId == companyId && r.ReportCode == code)
            .OrderByDescending(r => r.RunAt).ThenByDescending(r => r.Id)
            .Select(r => new ReportRunEntry(r.RunAt, r.ReportCode, r.UserId, r.Purpose, r.Format, r.Parameters, r.CopyNumber, r.ContentHash)).ToListAsync(ct).ConfigureAwait(false);
    }
}
