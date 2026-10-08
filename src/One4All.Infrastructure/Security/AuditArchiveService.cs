using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Security;

/// <summary>
/// Archives closed years of the partitioned audit log: checks the licence holder's signed authorisation, records where every company's hash
/// chain had reached and a hash of the year's entries, then detaches the year's partition into <c>audit_archive</c> (kept append-only there).
/// </summary>
public sealed class AuditArchiveService : IAuditArchiveService
{
    /// <summary>The action an authorisation must name.</summary>
    public const string Action = "audit.archive";

    /// <summary>The key a chain head is stored under for entries with no company (the system chain).</summary>
    public const string SystemChain = "system";

    private readonly One4AllDbContext _db;
    private readonly IClock _clock;
    private readonly ITrustedLicenceKeys? _keys;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="keys">The licence holder's trusted keys (none: archiving is refused).</param>
    public AuditArchiveService(One4AllDbContext db, IClock clock, ITrustedLicenceKeys? keys = null)
    {
        _db = db;
        _clock = clock;
        _keys = keys;
    }

    /// <summary>The partition (live) or archive table name for a year.</summary>
    /// <param name="year">The year.</param>
    /// <returns>The unqualified table name.</returns>
    public static string PartitionName(int year) => $"change_log_y{year.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Each chain's head after the latest archive (company id, or <see cref="SystemChain"/>, to the last archived row hash).</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The heads (empty when nothing is archived).</returns>
    public static async Task<IReadOnlyDictionary<string, string>> ChainHeadsAsync(One4AllDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        string? json = await db.AuditArchives.AsNoTracking().OrderByDescending(a => a.Year).Select(a => a.ChainHeadsJson).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return json is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    /// <summary>The chain key for a company.</summary>
    /// <param name="companyId">The company (null: the system chain).</param>
    /// <returns>The key.</returns>
    public static string ChainKey(long? companyId) => companyId?.ToString(CultureInfo.InvariantCulture) ?? SystemChain;

    /// <inheritdoc />
    public async Task<Result<AuditArchiveEntry>> ArchiveYearAsync(int year, string authorisationToken, long? userId, CancellationToken ct = default)
    {
        DateTimeOffset now = _clock.UtcNow;
        if (year >= now.Year)
        {
            return Error.Rule("Audit.YearOpen", $"{year} is not a closed year; only years before {now.Year} can be archived.");
        }

        if (_keys is null || _keys.PublicKeysByKid.Count == 0)
        {
            return Error.Forbidden("Audit.NoTrustedKey", "No licence-holder key is configured, so the audit log cannot be archived.");
        }

        (MaintenanceAuthorisation? authorisation, string? refusal) = MaintenanceAuthorisationCodec.Verify(authorisationToken, _keys.PublicKeysByKid, Action,
            year.ToString(CultureInfo.InvariantCulture), now);
        if (authorisation is null)
        {
            return Error.Forbidden("Audit.NotAuthorised", refusal ?? "The archive is not authorised.");
        }

        string partition = PartitionName(year);
        if (await _db.AuditArchives.AnyAsync(a => a.Year == year, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Audit.AlreadyArchived", $"{year} is already archived.");
        }

        if (!await PartitionExistsAsync("audit", partition, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Audit.NoPartition", $"The audit log has no partition for {year}.");
        }

        DateTimeOffset start = new(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        if (await _db.ChangeLog.AnyAsync(c => c.OccurredOnUtc < start, ct).ConfigureAwait(false))
        {
            return Error.Rule("Audit.ArchiveOldestFirst", $"Earlier years still hold audit entries: archive them before {year}.");
        }

        return await _db.AtomicAsync(async () =>
        {
            // Where each chain stands at the end of the year, carried on from the archives before it, and a hash of the year's entries.
            Dictionary<string, string> heads = new(await ChainHeadsAsync(_db, ct).ConfigureAwait(false));
            DateTimeOffset end = start.AddYears(1);
            long rows = 0;
            using IncrementalHash content = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long lastId = 0;
            while (true)
            {
                var batch = await _db.ChangeLog.AsNoTracking().Where(c => c.OccurredOnUtc >= start && c.OccurredOnUtc < end && c.Id > lastId).OrderBy(c => c.Id).Take(5000)
                    .Select(c => new { c.Id, c.CompanyId, c.RowHash }).ToListAsync(ct).ConfigureAwait(false);
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var entry in batch)
                {
                    content.AppendData(Encoding.UTF8.GetBytes(entry.RowHash + "\n"));
                    heads[ChainKey(entry.CompanyId)] = entry.RowHash;
                    rows++;
                    lastId = entry.Id;
                }
            }

            string hash = Convert.ToHexString(content.GetHashAndReset()).ToLowerInvariant();
            string table = $"audit_archive.{partition}";
#pragma warning disable EF1002 // the partition name is built from an integer year, never from input text
            await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE audit.change_log DETACH PARTITION audit.{partition}", ct).ConfigureAwait(false);
            await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE audit.{partition} SET SCHEMA audit_archive", ct).ConfigureAwait(false);
            await _db.Database.ExecuteSqlRawAsync(
                $"CREATE OR REPLACE TRIGGER trg_{partition}_append_only BEFORE UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION audit.change_log_append_only()", ct).ConfigureAwait(false);
            await _db.Database.ExecuteSqlRawAsync(
                $"CREATE OR REPLACE TRIGGER trg_{partition}_no_truncate BEFORE TRUNCATE ON {table} FOR EACH STATEMENT EXECUTE FUNCTION audit.change_log_append_only()", ct).ConfigureAwait(false);
#pragma warning restore EF1002

            AuditArchive archive = new(year, table, rows, JsonSerializer.Serialize(heads), hash, authorisation.KeyId, now, userId);
            _db.AuditArchives.Add(archive);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success(Entry(archive));
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditYear>> YearsAsync(CancellationToken ct = default)
    {
        List<AuditArchive> archived = await _db.AuditArchives.AsNoTracking().OrderBy(a => a.Year).ToListAsync(ct).ConfigureAwait(false);
        var live = await _db.ChangeLog.AsNoTracking().GroupBy(c => c.OccurredOnUtc.Year).Select(g => new { Year = g.Key, Rows = g.LongCount() }).ToListAsync(ct).ConfigureAwait(false);
        return [.. archived.Select(a => new AuditYear(a.Year, a.RowCount, true)).Concat(live.Select(l => new AuditYear(l.Year, l.Rows, false))).OrderBy(y => y.Year)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditArchiveEntry>> ArchivesAsync(CancellationToken ct = default)
        => [.. (await _db.AuditArchives.AsNoTracking().OrderBy(a => a.Year).ToListAsync(ct).ConfigureAwait(false)).Select(Entry)];

    private static AuditArchiveEntry Entry(AuditArchive a) => new(a.Year, a.TableName, a.RowCount, a.ContentHash, a.AuthorisationKeyId, a.ArchivedOnUtc);

    private async Task<bool> PartitionExistsAsync(string schema, string table, CancellationToken ct)
        => await _db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {schema} AND c.relname = {table}")
            .SingleAsync(ct).ConfigureAwait(false) > 0;
}
