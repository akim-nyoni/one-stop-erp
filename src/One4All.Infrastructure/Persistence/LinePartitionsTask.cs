using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Common;

namespace One4All.Infrastructure.Persistence;

/// <summary>
/// Keeps the partitioned line tables (journal, sale and payslip lines, by year) a year ahead: makes this year's and next year's partitions when
/// they are missing, through <c>sys.ensure_line_partitions</c>. Schedule it yearly (or monthly; it is idempotent). The DEFAULT partition takes rows
/// of a year without a partition, so nothing is refused if it has not run.
/// </summary>
public sealed class LinePartitionsTask : IScheduledTask
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the task.</summary>
    /// <param name="db">The database context.</param>
    public LinePartitionsTask(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public string Key => "system.partitions";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        int year = runAt.Year;
        await _db.Database.ExecuteSqlAsync($"SELECT sys.ensure_line_partitions({year})", ct).ConfigureAwait(false);
        await _db.Database.ExecuteSqlAsync($"SELECT sys.ensure_line_partitions({year + 1})", ct).ConfigureAwait(false);
        return $"Line-table partitions in place for {year} and {year + 1}.";
    }
}
