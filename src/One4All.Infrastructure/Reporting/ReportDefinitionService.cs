using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Reporting;
using One4All.Core.Common;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Model;

namespace One4All.Infrastructure.Reporting;

/// <summary>Company report definitions with permanent version history, diff and rollback.</summary>
public sealed class ReportDefinitionService : IReportDefinitionService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ReportDefinitionService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> CopySystemReportAsync(long companyId, string code, long? userId, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim();
        ReportDefinitionRecord? system = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(r => r.Scope == ReportScope.System && r.Code == key, ct).ConfigureAwait(false);
        if (system is null)
        {
            return Result.Failure(Error.NotFound("Rpt.SystemReportNotFound", $"No system report {key}."));
        }

        if (await CompanyRecordAsync(companyId, key, ct).ConfigureAwait(false) is not null)
        {
            return Result.Failure(Error.Conflict("Rpt.CompanyVersionExists", $"The company already has its own version of {key}."));
        }

        await CreateAsync(companyId, key, system.Name, system.Module, system.DefinitionJson, userId, $"Copied from system report v{system.Version}", ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> SaveAsync(long companyId, string code, string name, string? module, string definitionJson, long? userId, string? note, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim();
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("Rpt.Definition", "A report needs a code and a name.");
        }

        string? problem = Validate(definitionJson);
        if (problem is not null)
        {
            return Error.Validation("Rpt.Definition", problem);
        }

        ReportDefinitionRecord? record = await CompanyRecordAsync(companyId, key, ct).ConfigureAwait(false);
        if (record is null)
        {
            return await CreateAsync(companyId, key, name.Trim(), module, definitionJson, userId, string.IsNullOrWhiteSpace(note) ? "Created" : note.Trim(), ct).ConfigureAwait(false);
        }

        if (JsonDiff.Compare(record.DefinitionJson, definitionJson).Count == 0 && record.Name == name.Trim())
        {
            return Error.Rule("Rpt.NoChange", $"Nothing has changed since version {record.Version}.");
        }

        record.Name = name.Trim();
        record.Module = string.IsNullOrWhiteSpace(module) ? record.Module : module.Trim();
        record.UpdateDefinition(definitionJson);
        _db.Set<ReportDefinitionVersion>().Add(new ReportDefinitionVersion(record.Id, record.Version, definitionJson, userId, DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(note) ? "Saved" : note.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Version;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ReportVersionEntry>>> HistoryAsync(long companyId, string code, CancellationToken ct = default)
    {
        ReportDefinitionRecord? record = await CompanyRecordAsync(companyId, (code ?? string.Empty).Trim(), ct).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound("Rpt.CompanyReportNotFound", $"The company has no version of report {code}.");
        }

        return Result<IReadOnlyList<ReportVersionEntry>>.Success(await _db.Set<ReportDefinitionVersion>().AsNoTracking().Where(v => v.ReportDefinitionId == record.Id)
            .OrderByDescending(v => v.Version).Select(v => new ReportVersionEntry(v.Version, v.SavedAt, v.SavedByUserId, v.Note, v.Version == record.Version)).ToListAsync(ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JsonChange>>> DiffAsync(long companyId, string code, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        Result<(string From, string To)> pair = await VersionsAsync(companyId, code, fromVersion, toVersion, ct).ConfigureAwait(false);
        return pair.IsFailure ? pair.Error : Result<IReadOnlyList<JsonChange>>.Success(JsonDiff.Compare(pair.Value.From, pair.Value.To));
    }

    /// <inheritdoc />
    public async Task<Result<int>> RollbackAsync(long companyId, string code, int version, long? userId, CancellationToken ct = default)
    {
        ReportDefinitionRecord? record = await CompanyRecordAsync(companyId, (code ?? string.Empty).Trim(), ct).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound("Rpt.CompanyReportNotFound", $"The company has no version of report {code}.");
        }

        if (version == record.Version)
        {
            return Error.Rule("Rpt.AlreadyCurrent", $"Version {version} is already in use.");
        }

        string? json = await _db.Set<ReportDefinitionVersion>().Where(v => v.ReportDefinitionId == record.Id && v.Version == version).Select(v => v.DefinitionJson).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (json is null)
        {
            return Error.NotFound("Rpt.VersionNotFound", $"Report {record.Code} has no version {version}.");
        }

        record.UpdateDefinition(json);
        _db.Set<ReportDefinitionVersion>().Add(new ReportDefinitionVersion(record.Id, record.Version, json, userId, DateTimeOffset.UtcNow, $"Rolled back to v{version}"));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Version;
    }

    /// <inheritdoc />
    public async Task<Result<int>> SaveUserVersionAsync(long companyId, long userId, string code, string name, string? module, string definitionJson, string? note, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim();
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("Rpt.Definition", "A report needs a code and a name.");
        }

        string? problem = Validate(definitionJson);
        if (problem is not null)
        {
            return Error.Validation("Rpt.Definition", problem);
        }

        ReportDefinitionRecord? record = await _db.ReportDefinitions
            .FirstOrDefaultAsync(r => r.Scope == ReportScope.User && r.CompanyId == companyId && r.OwnerUserId == userId && r.Code == key, ct).ConfigureAwait(false);
        string remark = string.IsNullOrWhiteSpace(note) ? "Saved" : note.Trim();
        if (record is null)
        {
            record = new ReportDefinitionRecord(key, name.Trim(), definitionJson, ReportScope.User, companyId: companyId, ownerUserId: userId, module: string.IsNullOrWhiteSpace(module) ? null : module.Trim());
            _db.ReportDefinitions.Add(record);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            if (JsonDiff.Compare(record.DefinitionJson, definitionJson).Count == 0 && record.Name == name.Trim())
            {
                return Error.Rule("Rpt.NoChange", $"Nothing has changed since version {record.Version}.");
            }

            record.Name = name.Trim();
            record.UpdateDefinition(definitionJson);
        }

        _db.Set<ReportDefinitionVersion>().Add(new ReportDefinitionVersion(record.Id, record.Version, definitionJson, userId, DateTimeOffset.UtcNow, remark));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Version;
    }

    /// <inheritdoc />
    public async Task<Result> RemoveUserVersionAsync(long companyId, long userId, string code, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim();
        ReportDefinitionRecord? record = await _db.ReportDefinitions
            .FirstOrDefaultAsync(r => r.Scope == ReportScope.User && r.CompanyId == companyId && r.OwnerUserId == userId && r.Code == key, ct).ConfigureAwait(false);
        if (record is null)
        {
            return Result.Failure(Error.NotFound("Rpt.NoUserVersion", $"You have no version of your own of {key}."));
        }

        _db.ReportDefinitions.Remove(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<int> CreateAsync(long companyId, string code, string name, string? module, string json, long? userId, string note, CancellationToken ct)
    {
        ReportDefinitionRecord record = new(code, name, json, ReportScope.Company, companyId: companyId, module: string.IsNullOrWhiteSpace(module) ? null : module.Trim());
        _db.ReportDefinitions.Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _db.Set<ReportDefinitionVersion>().Add(new ReportDefinitionVersion(record.Id, record.Version, json, userId, DateTimeOffset.UtcNow, note));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Version;
    }

    private Task<ReportDefinitionRecord?> CompanyRecordAsync(long companyId, string code, CancellationToken ct)
        => _db.ReportDefinitions.FirstOrDefaultAsync(r => r.Scope == ReportScope.Company && r.CompanyId == companyId && r.Code == code, ct);

    private async Task<Result<(string From, string To)>> VersionsAsync(long companyId, string code, int fromVersion, int toVersion, CancellationToken ct)
    {
        ReportDefinitionRecord? record = await CompanyRecordAsync(companyId, (code ?? string.Empty).Trim(), ct).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound("Rpt.CompanyReportNotFound", $"The company has no version of report {code}.");
        }

        Dictionary<int, string> versions = await _db.Set<ReportDefinitionVersion>().AsNoTracking()
            .Where(v => v.ReportDefinitionId == record.Id && (v.Version == fromVersion || v.Version == toVersion))
            .ToDictionaryAsync(v => v.Version, v => v.DefinitionJson, ct).ConfigureAwait(false);
        if (!versions.ContainsKey(fromVersion) || !versions.ContainsKey(toVersion))
        {
            return Error.NotFound("Rpt.VersionNotFound", $"Report {record.Code} has no version {(versions.ContainsKey(fromVersion) ? toVersion : fromVersion)}.");
        }

        return (versions[fromVersion], versions[toVersion]);
    }

    private static string? Validate(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject)
            {
                return "The definition must be a JSON object.";
            }

            _ = ReportDefinitionSerializer.Deserialize(json);
            return null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return $"The definition is not a valid report: {ex.Message}";
        }
    }
}
