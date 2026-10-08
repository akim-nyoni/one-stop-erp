using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Security;

/// <summary>Which key is in use and whether it is only the development key.</summary>
/// <param name="ActiveKeyId">The key new values are written with.</param>
/// <param name="DevelopmentKey">True when no key was configured and the development key file is in use.</param>
public sealed record FieldEncryptionStatus(string ActiveKeyId, bool DevelopmentKey);

/// <summary>Encrypted columns still holding plaintext or an old key's values.</summary>
/// <param name="ActiveKeyId">The active key.</param>
/// <param name="DevelopmentKey">Whether it is the development key.</param>
/// <param name="Pending">Column → values not yet under the active key.</param>
public sealed record FieldEncryptionReport(string ActiveKeyId, bool DevelopmentKey, IReadOnlyDictionary<string, int> Pending)
{
    /// <summary>Gets a value indicating whether everything is encrypted with the active key.</summary>
    public bool Complete => Pending.Values.All(n => n == 0);
}

/// <summary>Reports on and completes payroll column encryption (rows written before it was on, or under a key being retired).</summary>
public interface IFieldEncryptionService
{
    /// <summary>Counts values not yet under the active key, per column.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<FieldEncryptionReport> StatusAsync(CancellationToken ct = default);

    /// <summary>Rewrites every encrypted column with the active key (plaintext rows become encrypted; old-key rows move to the new key).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows rewritten.</returns>
    Task<int> ReencryptAsync(CancellationToken ct = default);
}

/// <summary>The field encryption maintenance service.</summary>
public sealed class FieldEncryptionService : IFieldEncryptionService
{
    private static readonly (string Table, string[] Columns)[] Encrypted =
    [
        ("hr.employee", ["basic_salary", "national_id_number", "tax_number", "bank_branch", "bank_account_number", "mobile_money_number"]),
        ("hr.detail_change_request", ["old_value", "new_value"]),
    ];

    private readonly One4AllDbContext _db;
    private readonly FieldEncryptionStatus _status;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="status">The key in use.</param>
    public FieldEncryptionService(One4AllDbContext db, FieldEncryptionStatus status)
    {
        _db = db;
        _status = status;
    }

    /// <inheritdoc />
    public async Task<FieldEncryptionReport> StatusAsync(CancellationToken ct = default)
    {
        System.Data.Common.DbConnection connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await _db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        }

        string prefix = $"enc:v1:{_status.ActiveKeyId}:%";
        Dictionary<string, int> pending = new(StringComparer.Ordinal);
        foreach ((string table, string[] columns) in Encrypted)
        {
            foreach (string column in columns)
            {
                pending[$"{table}.{column}"] = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    $"SELECT count(*) FROM {table} WHERE {column} IS NOT NULL AND {column} NOT LIKE @prefix", new { prefix }, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        return new FieldEncryptionReport(_status.ActiveKeyId, _status.DevelopmentKey, pending);
    }

    /// <inheritdoc />
    public async Task<int> ReencryptAsync(CancellationToken ct = default)
    {
        int rows = 0;
        rows += await RewriteAsync(_db.Employees.IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]), ct).ConfigureAwait(false);
        rows += await RewriteAsync(_db.Set<DetailChangeRequest>(), ct).ConfigureAwait(false);
        return rows;
    }

    // Reads each row (values decrypt, or pass through as plaintext) and marks the encrypted properties changed so they are written again with the active key.
    private async Task<int> RewriteAsync<T>(IQueryable<T> source, CancellationToken ct)
        where T : Entity
    {
        int rows = 0;
        long lastId = 0;
        while (true)
        {
            List<T> batch = await source.Where(e => e.Id > lastId).OrderBy(e => e.Id).Take(500).ToListAsync(ct).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return rows;
            }

            foreach (T entity in batch)
            {
                foreach (PropertyEntry property in _db.Entry(entity).Properties.Where(p => p.Metadata.FindAnnotation(FieldEncryptionModel.Annotation) is not null && p.CurrentValue is not null))
                {
                    property.IsModified = true;
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            rows += batch.Count;
            lastId = batch[^1].Id;
        }
    }
}

/// <summary>Scheduled task: encrypts payroll columns still in plaintext or under a retired key (key <c>security.reencrypt-fields</c>).</summary>
public sealed class ReencryptFieldsTask : IScheduledTask
{
    private readonly IFieldEncryptionService _service;

    /// <summary>Creates the task.</summary>
    /// <param name="service">The service.</param>
    public ReencryptFieldsTask(IFieldEncryptionService service) => _service = service;

    /// <inheritdoc />
    public string Key => "security.reencrypt-fields";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        FieldEncryptionReport before = await _service.StatusAsync(ct).ConfigureAwait(false);
        if (before.Complete)
        {
            return "All payroll fields are already encrypted with the active key.";
        }

        int rows = await _service.ReencryptAsync(ct).ConfigureAwait(false);
        return $"{rows} row(s) rewritten; {before.Pending.Values.Sum()} value(s) were not under key {before.ActiveKeyId}.";
    }
}
