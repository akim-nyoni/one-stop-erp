using System.Data.Common;
using System.Diagnostics;
using Dapper;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Reporting;

/// <summary>The reporting snapshots (materialised views in <c>rpt</c>) and the trigram quick search.</summary>
public sealed class ReportingSnapshotService : IReportingSnapshotService
{
    private static readonly string[] Snapshots = ["rpt.aged_debt", "rpt.stock_valuation", "rpt.sales_by_day"];
    private readonly One4AllDbContext _db;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context (its connection is used).</param>
    /// <param name="clock">The clock.</param>
    public ReportingSnapshotService(One4AllDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SnapshotRefresh>> RefreshAsync(CancellationToken ct = default)
    {
        DbConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        List<SnapshotRefresh> done = [];
        foreach (string name in Snapshots)
        {
            Stopwatch watch = Stopwatch.StartNew();
            await connection.ExecuteAsync(new CommandDefinition($"REFRESH MATERIALIZED VIEW CONCURRENTLY {name}", cancellationToken: ct)).ConfigureAwait(false);
            long rows = await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT count(*) FROM {name}", cancellationToken: ct)).ConfigureAwait(false);
            SnapshotRefresh refresh = new(name, _clock.UtcNow, (int)watch.ElapsedMilliseconds, rows);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO sys.snapshot_refresh (name, refreshed_on_utc, duration_ms, row_count) VALUES (@Name, @RefreshedOnUtc, @DurationMs, @Rows)
                ON CONFLICT (name) DO UPDATE SET refreshed_on_utc = excluded.refreshed_on_utc, duration_ms = excluded.duration_ms, row_count = excluded.row_count
                """, refresh, cancellationToken: ct)).ConfigureAwait(false);
            done.Add(refresh);
        }

        return done;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SnapshotRefresh>> LastRefreshesAsync(CancellationToken ct = default)
    {
        DbConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        return [.. (await connection.QueryAsync<RefreshRow>(new CommandDefinition(
                "SELECT name AS Name, refreshed_on_utc AS RefreshedOnUtc, duration_ms AS DurationMs, row_count AS RowCount FROM sys.snapshot_refresh ORDER BY name", cancellationToken: ct))
            .ConfigureAwait(false)).Select(r => new SnapshotRefresh(r.Name, new DateTimeOffset(DateTime.SpecifyKind(r.RefreshedOnUtc, DateTimeKind.Utc)), r.DurationMs, r.RowCount))];
    }

    /// <inheritdoc />
    public async Task<Snapshot<AgedDebtSnapshotRow>> AgedDebtAsync(long companyId, CancellationToken ct = default)
    {
        DbConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        IEnumerable<AgedRow> rows = await connection.QueryAsync<AgedRow>(new CommandDefinition(
            """
            SELECT customer_id AS CustomerId, customer_code AS CustomerCode, customer_name AS CustomerName, as_at::timestamp AS AsAt, current_due AS CurrentDue,
                   days_1_30 AS Days1To30, days_31_60 AS Days31To60, days_61_90 AS Days61To90, days_over_90 AS Over90, total AS Total
            FROM rpt.aged_debt WHERE company_id = @companyId ORDER BY total DESC, customer_code
            """, new { companyId }, cancellationToken: ct)).ConfigureAwait(false);
        return new Snapshot<AgedDebtSnapshotRow>([.. rows.Select(r => new AgedDebtSnapshotRow(r.CustomerId, r.CustomerCode, r.CustomerName, DateOnly.FromDateTime(r.AsAt), r.CurrentDue,
            r.Days1To30, r.Days31To60, r.Days61To90, r.Over90, r.Total))], await StampAsync(connection, "rpt.aged_debt", ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<Snapshot<StockValuationSnapshotRow>> StockValuationAsync(long companyId, CancellationToken ct = default)
    {
        DbConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        IEnumerable<StockRow> rows = await connection.QueryAsync<StockRow>(new CommandDefinition(
            """
            SELECT warehouse_id AS WarehouseId, warehouse_code AS WarehouseCode, category AS Category, items AS Items, quantity AS Quantity, value AS Value
            FROM rpt.stock_valuation WHERE company_id = @companyId ORDER BY warehouse_code, category
            """, new { companyId }, cancellationToken: ct)).ConfigureAwait(false);
        return new Snapshot<StockValuationSnapshotRow>([.. rows.Select(r => new StockValuationSnapshotRow(r.WarehouseId, r.WarehouseCode, r.Category, r.Items, r.Quantity, r.Value))],
            await StampAsync(connection, "rpt.stock_valuation", ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<Snapshot<SalesByDaySnapshotRow>> SalesByDayAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        DbConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        IEnumerable<SalesRow> rows = await connection.QueryAsync<SalesRow>(new CommandDefinition(
            """
            SELECT posting_date::timestamp AS Date, department AS Department, revenue AS Revenue, lines AS Lines
            FROM rpt.sales_by_day WHERE company_id = @companyId AND posting_date BETWEEN @from AND @to ORDER BY posting_date, department
            """, new { companyId, from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue) }, cancellationToken: ct)).ConfigureAwait(false);
        return new Snapshot<SalesByDaySnapshotRow>([.. rows.Select(r => new SalesByDaySnapshotRow(DateOnly.FromDateTime(r.Date), r.Department, r.Revenue, r.Lines))],
            await StampAsync(connection, "rpt.sales_by_day", ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuickSearchHit>> SearchAsync(long companyId, string term, int take = 25, CancellationToken ct = default)
    {
        string text = (term ?? string.Empty).Trim();
        if (text.Length < 2)
        {
            return [];
        }

        string like = $"%{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
        DbConnection connection = await OpenAsync(ct).ConfigureAwait(false);

        // "<%" is word similarity (a near match to some word of the name), served by the trigram indexes; containment ranks first. The
        // default threshold (0.6) misses a single dropped letter in a short word ("cemnt"), so this connection uses 0.45. Names are compared
        // without accents on either side (public.f_unaccent, an immutable wrapper the expression indexes use), so "Zoe" finds "Zoë".
        await connection.ExecuteAsync(new CommandDefinition("SELECT set_config('pg_trgm.word_similarity_threshold', '0.45', false)", cancellationToken: ct)).ConfigureAwait(false);
        IEnumerable<SearchRow> rows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
            """
            WITH q AS (SELECT public.f_unaccent(@text) AS text, public.f_unaccent(@like) AS pattern)
            SELECT kind AS Kind, id AS Id, code AS Code, name AS Name, CASE WHEN contained THEN 1.0 ELSE score END AS Score
            FROM (
                SELECT 'Item' AS kind, i.id, i.code, i.description AS name, word_similarity(q.text, public.f_unaccent(i.description)) AS score,
                       public.f_unaccent(i.description) ILIKE q.pattern OR i.code ILIKE @like AS contained
                FROM inv.item i, q
                WHERE i.company_id = @companyId AND NOT i.is_deleted
                  AND (public.f_unaccent(i.description) ILIKE q.pattern OR i.code ILIKE @like OR q.text <% public.f_unaccent(i.description))
                UNION ALL
                SELECT 'Customer', c.id, c.code, c.name, word_similarity(q.text, public.f_unaccent(c.name)), public.f_unaccent(c.name) ILIKE q.pattern OR c.code ILIKE @like
                FROM ar.customer c, q
                WHERE c.company_id = @companyId AND NOT c.is_deleted AND (public.f_unaccent(c.name) ILIKE q.pattern OR c.code ILIKE @like OR q.text <% public.f_unaccent(c.name))
                UNION ALL
                SELECT 'Supplier', s.id, s.code, s.name, word_similarity(q.text, public.f_unaccent(s.name)), public.f_unaccent(s.name) ILIKE q.pattern OR s.code ILIKE @like
                FROM ap.supplier s, q
                WHERE s.company_id = @companyId AND NOT s.is_deleted AND (public.f_unaccent(s.name) ILIKE q.pattern OR s.code ILIKE @like OR q.text <% public.f_unaccent(s.name))
                UNION ALL
                SELECT 'Lead', l.id, l.email, l.name || coalesce(' (' || l.organisation || ')', ''),
                       greatest(word_similarity(@text, l.name), word_similarity(@text, coalesce(l.organisation, ''))), l.name ILIKE @like OR l.organisation ILIKE @like
                FROM crm.lead l
                WHERE l.company_id = @companyId AND (l.name ILIKE @like OR l.organisation ILIKE @like OR @text <% l.name OR @text <% l.organisation)
                UNION ALL
                SELECT 'Account', a.id, a.account_code, a.name, word_similarity(@text, a.name), a.name ILIKE @like OR a.account_code ILIKE @like
                FROM gl.account a
                WHERE a.company_id = @companyId AND NOT a.is_deleted AND (a.name ILIKE @like OR a.account_code ILIKE @like OR @text <% a.name)
                UNION ALL
                SELECT 'Property', p.id, p.stand_number, p.stand_number || coalesce(' — ' || p.address, ''),
                       word_similarity(q.text, public.f_unaccent(coalesce(p.address, ''))), p.stand_number ILIKE @like OR public.f_unaccent(coalesce(p.address, '')) ILIKE q.pattern
                FROM mun.property p, q
                WHERE p.company_id = @companyId AND NOT p.is_deleted
                  AND (p.stand_number ILIKE @like OR public.f_unaccent(coalesce(p.address, '')) ILIKE q.pattern OR q.text <% public.f_unaccent(coalesce(p.address, '')))
                UNION ALL
                SELECT 'Student', st.id, st.admission_number, st.full_name, word_similarity(q.text, public.f_unaccent(st.full_name)),
                       st.admission_number ILIKE @like OR public.f_unaccent(st.full_name) ILIKE q.pattern
                FROM sch.student st, q
                WHERE st.company_id = @companyId AND NOT st.is_deleted
                  AND (st.admission_number ILIKE @like OR public.f_unaccent(st.full_name) ILIKE q.pattern OR q.text <% public.f_unaccent(st.full_name))
            ) hits
            ORDER BY 5 DESC, name
            LIMIT @take
            """, new { companyId, text, like, take = Math.Clamp(take, 1, 200) }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new QuickSearchHit(r.Kind, r.Id, r.Code, r.Name, Math.Round(r.Score, 3)))];
    }

    private static async Task<DateTimeOffset?> StampAsync(DbConnection connection, string name, CancellationToken ct)
    {
        DateTime? at = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition("SELECT refreshed_on_utc FROM sys.snapshot_refresh WHERE name = @name", new { name }, cancellationToken: ct))
            .ConfigureAwait(false);
        return at is DateTime value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;
    }

    // The context's own connection, so a surrounding transaction (and the connection's audit name) carry over.
    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        DbConnection connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await _db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        }

        return connection;
    }

    private sealed class RefreshRow
    {
        public string Name { get; set; } = string.Empty;

        public DateTime RefreshedOnUtc { get; set; }

        public int DurationMs { get; set; }

        public long RowCount { get; set; }
    }

    private sealed class AgedRow
    {
        public long CustomerId { get; set; }

        public string? CustomerCode { get; set; }

        public string? CustomerName { get; set; }

        public DateTime AsAt { get; set; }

        public decimal CurrentDue { get; set; }

        public decimal Days1To30 { get; set; }

        public decimal Days31To60 { get; set; }

        public decimal Days61To90 { get; set; }

        public decimal Over90 { get; set; }

        public decimal Total { get; set; }
    }

    private sealed class StockRow
    {
        public long WarehouseId { get; set; }

        public string WarehouseCode { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public long Items { get; set; }

        public decimal Quantity { get; set; }

        public decimal Value { get; set; }
    }

    private sealed class SalesRow
    {
        public DateTime Date { get; set; }

        public string Department { get; set; } = string.Empty;

        public decimal Revenue { get; set; }

        public long Lines { get; set; }
    }

    private sealed class SearchRow
    {
        public string Kind { get; set; } = string.Empty;

        public long Id { get; set; }

        public string? Code { get; set; }

        public string Name { get; set; } = string.Empty;

        public double Score { get; set; }
    }
}

/// <summary>Scheduled task: refreshes the reporting snapshots (key <c>reporting.refresh-snapshots</c>; one run serves every company).</summary>
public sealed class RefreshSnapshotsTask : IScheduledTask
{
    private readonly IReportingSnapshotService _snapshots;

    /// <summary>Creates the task.</summary>
    /// <param name="snapshots">The snapshot service.</param>
    public RefreshSnapshotsTask(IReportingSnapshotService snapshots) => _snapshots = snapshots;

    /// <inheritdoc />
    public string Key => "reporting.refresh-snapshots";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
        => string.Join("; ", (await _snapshots.RefreshAsync(ct).ConfigureAwait(false)).Select(r => $"{r.Name}: {r.Rows} row(s) in {r.DurationMs} ms"));
}
