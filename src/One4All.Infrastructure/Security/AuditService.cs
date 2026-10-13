using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Security;

/// <summary>Reads and verifies the append-only, hash-chained change log.</summary>
public sealed class AuditService : IAuditService
{
    private const int MaxTake = 5_000;
    private const int VerifyBatch = 2_000;
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public AuditService(One4AllDbContext db, IPermissionService? permissions = null)
    {
        _db = db;
        _permissions = permissions;
    }

    // Every payroll entity: their changes are kept from anyone without the payroll audit right.
    private static readonly string[] PayrollEntities = [.. typeof(Core.Payroll.Employee).Assembly.GetTypes()
        .Where(t => t.Namespace == typeof(Core.Payroll.Employee).Namespace && t.IsClass && !t.IsAbstract).Select(t => t.Name)];

    private readonly IPermissionService? _permissions;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntryView>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        IQueryable<ChangeLogEntry> q = _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == query.CompanyId || (query.IncludeUnscoped && c.CompanyId == null));

        if (query.From is { } from)
        {
            DateTimeOffset fromUtc = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(c => c.OccurredOnUtc >= fromUtc);
        }

        if (query.To is { } to)
        {
            DateTimeOffset toUtc = new(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            q = q.Where(c => c.OccurredOnUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            string type = query.EntityType.Trim();
            q = q.Where(c => c.EntityType == type);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            string id = query.EntityId.Trim();
            q = q.Where(c => c.EntityId == id);
        }

        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            string user = query.UserName.Trim().ToLower();
            q = q.Where(c => c.UserName.ToLower() == user);
        }

        if (query.Action is { } action)
        {
            q = q.Where(c => c.Action == action);
        }

        if (query.RequestingUserId is long requester && _permissions is not null)
        {
            Core.Security.EffectivePermissions rights = await _permissions.ResolveAsync(requester, ct).ConfigureAwait(false);
            if (!rights.IsSuperAdministrator && !rights.Has("Payroll.Audit", Core.Security.OperationFlags.View))
            {
                q = q.Where(c => !PayrollEntities.Contains(c.EntityType));
            }
        }

        if (query.BeforeId is long before)
        {
            q = q.Where(c => c.Id < before);
        }

        int take = Math.Clamp(query.Take, 1, MaxTake);
        List<ChangeLogEntry> entries = await q.OrderByDescending(c => c.Id).Take(take).ToListAsync(ct).ConfigureAwait(false);

        return entries.Select(c => new AuditEntryView(
            c.Id, c.OccurredOnUtc, string.IsNullOrEmpty(c.UserName) ? "system" : c.UserName, c.Action, c.EntityType, c.EntityId,
            c.ChangedFields, c.OldValues, c.NewValues, c.Reason, c.Terminal)).ToList();
    }

    /// <inheritdoc />
    public async Task<AuditChainVerification> VerifyChainAsync(long? companyId, CancellationToken ct = default)
    {
        // Walk the chain in id order, in batches so a long log never loads at once, from where the archived years left it.
        string previous = (await AuditArchiveService.ChainHeadsAsync(_db, ct).ConfigureAwait(false)).GetValueOrDefault(AuditArchiveService.ChainKey(companyId)) ?? string.Empty;
        long lastId = 0;
        int checkedCount = 0;
        while (true)
        {
            List<ChangeLogEntry> batch = await _db.ChangeLog.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Id > lastId)
                .OrderBy(c => c.Id)
                .Take(VerifyBatch)
                .ToListAsync(ct).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (ChangeLogEntry entry in batch)
            {
                checkedCount++;
                if (!entry.VerifyAgainst(previous))
                {
                    return new AuditChainVerification(checkedCount, false, entry.Id,
                        $"Audit chain broken at entry {entry.Id} ({entry.Action} {entry.EntityType} {entry.EntityId}, {entry.OccurredOnUtc:yyyy-MM-dd HH:mm:ss} UTC): the entry or its predecessor has been altered.");
                }

                previous = entry.RowHash;
                lastId = entry.Id;
            }
        }

        return new AuditChainVerification(checkedCount, true, null, $"Audit chain intact: {checkedCount} entr{(checkedCount == 1 ? "y" : "ies")} verified.");
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        if (auditEvent.Action == AuditAction.Override && string.IsNullOrWhiteSpace(auditEvent.Reason))
        {
            return Error.Validation("Audit.ReasonRequired", "An override is recorded with its reason.");
        }

        return await _db.AtomicAsync(async () =>
        {
            // The chain is read and extended under its lock, so a concurrent writer cannot link to the same previous entry.
            string? previous = Persistence.Auditing.AuditChainLock.HeldHead(_db, auditEvent.CompanyId);
            if (previous is null)
            {
                await Persistence.Auditing.AuditChainLock.AcquireAsync(_db, [auditEvent.CompanyId], ct).ConfigureAwait(false);
                previous = await _db.ChangeLog.Where(c => c.CompanyId == auditEvent.CompanyId).OrderByDescending(c => c.Id).Select(c => c.RowHash).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? (await AuditArchiveService.ChainHeadsAsync(_db, ct).ConfigureAwait(false)).GetValueOrDefault(AuditArchiveService.ChainKey(auditEvent.CompanyId)) ?? string.Empty;
            }

            ChangeLogEntry entry = ChangeLogEntry.Create(previous, DateTimeOffset.UtcNow, auditEvent.CompanyId, auditEvent.UserId, auditEvent.UserName, auditEvent.Module, auditEvent.EntityType,
                auditEvent.EntityId, auditEvent.Action, newValues: auditEvent.Details, reason: auditEvent.Reason, terminal: auditEvent.Terminal);
            _db.ChangeLog.Add(entry);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            Persistence.Auditing.AuditChainLock.Advance(_db, auditEvent.CompanyId, entry.RowHash);
            return Result.Success(entry.Id);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DatabaseChangeView>> DatabaseChangesAsync(DateTimeOffset from, DateTimeOffset to, bool outsideApplicationOnly, int take = 500, CancellationToken ct = default)
    {
        IQueryable<Core.Audit.DatabaseChange> q = _db.DatabaseChanges.AsNoTracking().Where(c => c.OccurredOnUtc >= from && c.OccurredOnUtc <= to);
        if (outsideApplicationOnly)
        {
            q = q.Where(c => c.ApplicationName == null || c.ApplicationName != Persistence.One4AllDbContextOptions.ApplicationName);
        }

        return [.. (await q.OrderByDescending(c => c.Id).Take(take).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => new DatabaseChangeView(c.Id, c.OccurredOnUtc, c.TableName, c.Operation, c.RowId, c.DbUser, c.ApplicationName, c.ClientAddress,
                c.ApplicationName != Persistence.One4AllDbContextOptions.ApplicationName))];
    }

    /// <inheritdoc />
    public async Task<RecordHistory> RecordHistoryAsync(long companyId, string entityType, string entityId, CancellationToken ct = default)
    {
        string type = (entityType ?? string.Empty).Trim(), id = (entityId ?? string.Empty).Trim();
        IReadOnlyList<AuditEntryView> entries = await QueryAsync(new AuditQuery { CompanyId = companyId, EntityType = type, EntityId = id, Take = MaxTake, IncludeUnscoped = true }, ct).ConfigureAwait(false);
        List<AuditEntryView> oldestFirst = [.. entries.OrderBy(e => e.OccurredOnUtc).ThenBy(e => e.Id)];

        // The database trigger names rows by schema.table: find the record type's table in the model.
        Microsoft.EntityFrameworkCore.Metadata.IEntityType? mapped = _db.Model.GetEntityTypes().FirstOrDefault(e => e.ClrType.Name == type && e.GetTableName() is not null);
        List<DatabaseChangeView> database = [];
        if (mapped is not null)
        {
            string table = $"{mapped.GetSchema() ?? "public"}.{mapped.GetTableName()}";
            database = [.. (await _db.DatabaseChanges.AsNoTracking().Where(c => c.TableName == table && c.RowId == id).OrderByDescending(c => c.Id).Take(MaxTake).ToListAsync(ct).ConfigureAwait(false))
                .Select(c => new DatabaseChangeView(c.Id, c.OccurredOnUtc, c.TableName, c.Operation, c.RowId, c.DbUser, c.ApplicationName, c.ClientAddress,
                    c.ApplicationName != Persistence.One4AllDbContextOptions.ApplicationName))];
        }

        return new RecordHistory(type, id, oldestFirst.FirstOrDefault(e => e.Action == AuditAction.Insert),
            oldestFirst.LastOrDefault(e => e.Action is AuditAction.Insert or AuditAction.Update or AuditAction.Delete),
            [.. oldestFirst.Select(e => e.UserName).Distinct(StringComparer.OrdinalIgnoreCase)], entries, database);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> EntityTypesAsync(long companyId, CancellationToken ct = default)
        => await _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == companyId || c.CompanyId == null).Select(c => c.EntityType).Distinct().OrderBy(t => t).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<Application.Abstractions.Reporting.RenderedDocument>> ExportAsync(AuditQuery query, Application.Abstractions.Reporting.ReportFormat format, long? userId, string userName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (format is not (Application.Abstractions.Reporting.ReportFormat.Csv or Application.Abstractions.Reporting.ReportFormat.Xlsx or Application.Abstractions.Reporting.ReportFormat.Pdf))
        {
            return Error.Validation("Audit.ExportFormat", "The audit trail exports as CSV, Excel or PDF.");
        }

        IReadOnlyList<AuditEntryView> entries = await QueryAsync(query, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [.. entries.Select(e => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["When"] = e.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "Z",
            ["User"] = e.UserName,
            ["Action"] = e.Action.ToString(),
            ["Record"] = string.IsNullOrEmpty(e.EntityId) ? e.EntityType : $"{e.EntityType} #{e.EntityId}",
            ["Fields"] = string.Join(", ", e.ChangedFields),
            ["Reason"] = e.Reason,
            ["Terminal"] = e.Terminal,
        })];

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        Application.Abstractions.Reporting.RenderedDocument document;
        switch (format)
        {
            case Application.Abstractions.Reporting.ReportFormat.Csv:
                document = new(new One4All.Reporting.Engine.CsvReportExporter().Export(rows), "text/csv", $"audit-trail-{stamp}.csv");
                break;
            case Application.Abstractions.Reporting.ReportFormat.Xlsx:
                document = new(new One4All.Reporting.Engine.XlsxReportExporter().Export(rows, "Audit trail"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"audit-trail-{stamp}.xlsx");
                break;
            default:
                One4All.Reporting.Model.ReportDefinition listing = One4All.Reporting.SystemReports.Listing("AUDIT-EXPORT", "Audit Trail", "Security", "Audit trail export", "AuditExport",
                    [new("When", "When (UTC)", 95), new("User", "User", 70), new("Action", "Action", 55), new("Record", "Record", 110), new("Fields", "Changed fields", 140),
                        new("Reason", "Reason", 90)], []);
                listing.Page.Orientation = One4All.Reporting.Model.ReportOrientation.Landscape;
                One4All.Reporting.Engine.RenderModel model = new One4All.Reporting.Engine.ReportEngine().Build(new One4All.Reporting.Engine.ReportInput
                {
                    Definition = listing, Rows = rows, UserName = userName,
                    CompanyName = await _db.Companies.Where(c => c.Id == query.CompanyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty,
                });
                document = new(new One4All.Reporting.Engine.PdfReportRenderer().Render(model), "application/pdf", $"audit-trail-{stamp}.pdf");
                break;
        }

        // An export of the audit trail is itself audited.
        Result<long> logged = await RecordAsync(new AuditEvent(query.CompanyId, userId, userName, "Audit", "AuditTrail", null, AuditAction.Export,
            $"{entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} exported as {format} ({query.EntityType ?? "all records"}{(query.EntityId is null ? string.Empty : $" #{query.EntityId}")})"), ct)
            .ConfigureAwait(false);
        return logged.IsFailure ? logged.Error : document;
    }
}
