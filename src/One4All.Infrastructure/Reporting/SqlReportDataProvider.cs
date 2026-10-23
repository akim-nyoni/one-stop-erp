using System.Text.RegularExpressions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Rows for user-authored reports from SQL, a reporting view or a reporting function (brief §13.1 data sources), made safe: a single
/// SELECT / WITH statement that must bind <c>company_id = @CompanyId</c> (checked on its tokens, so quoting, comments and literals hide
/// nothing), never naming the security, payroll, licensing, audit, system or sync schemas or the catalogues; views and functions only from
/// the <c>rpt</c> schema; run on its own connection in a read-only transaction with a statement timeout as the <see cref="ReportRole"/>
/// role (SELECT on the reportable schemas only, so a reference the text checks miss is still denied by the database); the rows cut to
/// the company again when they carry a <c>company_id</c> column; report parameters bound by name; and at most <see cref="MaxRows"/> rows.
/// </summary>
public sealed partial class SqlReportDataProvider : IReportDataProvider, IReportFieldDiscovery
{
    /// <summary>The most rows a query returns: a guard against a runaway query, set well above the brief's 100,000-row report (§15) so such a report is fetched whole.</summary>
    public const int MaxRows = 250_000;

    private readonly string _connectionString;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context (for its connection string).</param>
    public SqlReportDataProvider(One4AllDbContext db) => _connectionString = db.Database.GetConnectionString() ?? string.Empty;

    /// <inheritdoc />
    public bool CanHandle(string command)
        => command.StartsWith("sql:", StringComparison.OrdinalIgnoreCase) || command.StartsWith("view:", StringComparison.OrdinalIgnoreCase)
           || command.StartsWith("function:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the statement to run, or says why the command is refused.</summary>
    /// <param name="command">The prefixed command.</param>
    /// <param name="parameterNames">The report's parameter names (a function receives them in this order after the company).</param>
    /// <returns>The SQL, or the refusal.</returns>
    public static (string? Sql, string? Refusal) Statement(string command, IReadOnlyList<string> parameterNames)
    {
        ArgumentNullException.ThrowIfNull(command);
        int colon = command.IndexOf(':', StringComparison.Ordinal);
        string kind = command[..colon].ToLowerInvariant(), body = command[(colon + 1)..].Trim();
        switch (kind)
        {
            case "view":
                return ReportingObject().IsMatch(body) ? ($"SELECT * FROM {body} WHERE company_id = @CompanyId", null) : (null, "Views must be rpt.<name>.");
            case "function":
                if (!ReportingObject().IsMatch(body))
                {
                    return (null, "Functions must be rpt.<name>.");
                }

                return ($"SELECT * FROM {body}(@CompanyId{string.Concat((parameterNames ?? []).Where(n => Identifier().IsMatch(n)).Select(n => $", @{n}"))})", null);
            default:
                string sql = body.TrimEnd().TrimEnd(';');
                string? refusal = Application.Reporting.SqlReportStatement.Refusal(sql);
                return refusal is null ? (sql, null) : (null, refusal);
        }
    }

    /// <summary>The read-only database role report queries run as: SELECT on the reportable business schemas only, nothing on security, payroll, audit, licensing, system or sync.</summary>
    public const string ReportRole = "one4all_report";

    /// <summary>The schemas the reporting role may read (granted by the ReportRole migration); everything else is denied by the database whatever the text checks miss.</summary>
    public static readonly IReadOnlyList<string> ReportableSchemas =
        ["gl", "ar", "ap", "trd", "cb", "inv", "pos", "res", "vou", "pop", "sop", "mun", "hse", "sch", "job", "fa", "hr", "exp", "bud", "crm", "rcp", "rpt", "wf"];

    // Opens the guarded transaction: read only, a statement timeout, and the reporting role (failing closed when the role is missing or cannot be taken).
    private static async Task<NpgsqlTransaction> BeginGuardedAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        NpgsqlTransaction transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        try
        {
            await using NpgsqlCommand guard = new($"SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '15s'; SET LOCAL ROLE {ReportRole}", connection, transaction);
            await guard.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return transaction;
        }
        catch (PostgresException ex) when (ex.SqlState is "42704" or "42501" or "22023")
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"Report queries cannot run: the database role {ReportRole} is missing or the application user may not take it ({ex.MessageText}). Apply the database migrations and grant the role.", ex);
        }
    }

    // The statement cut to the company again when its result carries a company_id column: the WHERE the author wrote cannot widen it (WHERE 1=1 OR ...).
    private static async Task<string> CompanyBoundAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Action<NpgsqlParameterCollection> bind, CancellationToken ct)
    {
        await using NpgsqlCommand describe = new($"SELECT * FROM ({sql}) AS report_rows LIMIT 0", connection, transaction);
        bind(describe.Parameters);
        await using NpgsqlDataReader reader = await describe.ExecuteReaderAsync(System.Data.CommandBehavior.SchemaOnly, ct).ConfigureAwait(false);
        string? company = (await reader.GetColumnSchemaAsync(ct).ConfigureAwait(false)).Select(c => c.ColumnName)
            .FirstOrDefault(n => string.Equals(n, Application.Reporting.SqlReportStatement.CompanyColumn, StringComparison.OrdinalIgnoreCase));
        return company is null ? sql : $"SELECT * FROM ({sql}) AS company_rows WHERE company_rows.\"{company.Replace("\"", "\"\"", StringComparison.Ordinal)}\" = @CompanyId";
    }

    private static void Bind(NpgsqlParameterCollection target, long companyId, IReadOnlyDictionary<string, object?> parameters)
    {
        target.AddWithValue("CompanyId", companyId);
        foreach ((string name, object? value) in parameters.Where(p => Identifier().IsMatch(p.Key) && !p.Key.Equals("CompanyId", StringComparison.OrdinalIgnoreCase)))
        {
            target.AddWithValue(name, value switch
            {
                null => DBNull.Value,
                DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                _ => value,
            });
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        (string? sql, string? refusal) = Statement(command, [.. parameters.Keys]);
        if (sql is null)
        {
            throw new InvalidOperationException($"Report query refused: {refusal}");
        }

        DynamicParameters bound = new();
        bound.Add("CompanyId", companyId);
        foreach ((string name, object? value) in parameters.Where(p => Identifier().IsMatch(p.Key) && !p.Key.Equals("CompanyId", StringComparison.OrdinalIgnoreCase)))
        {
            bound.Add(name, value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : value);
        }

        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await BeginGuardedAsync(connection, ct).ConfigureAwait(false);
        if (command.StartsWith("sql:", StringComparison.OrdinalIgnoreCase))
        {
            sql = await CompanyBoundAsync(connection, transaction, sql, p => Bind(p, companyId, parameters), ct).ConfigureAwait(false);
        }

        // One row past the cap tells us whether the cap cut the result; the reader then sees a notice row instead of a silently short report.
        IEnumerable<dynamic> rows = await connection.QueryAsync(new CommandDefinition($"SELECT * FROM ({sql}) AS report_rows LIMIT {MaxRows + 1}", bound, transaction, cancellationToken: ct))
            .ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> result = [.. rows.Take(MaxRows).Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(
            ((IDictionary<string, object?>)r).Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value is DateTime dt && dt.TimeOfDay == TimeSpan.Zero ? DateOnly.FromDateTime(dt) : kv.Value)),
            StringComparer.OrdinalIgnoreCase))];
        await transaction.RollbackAsync(ct).ConfigureAwait(false);
        if (result.Count == MaxRows && rows.Skip(MaxRows).Any())
        {
            result.Add(CapNotice(result[0].Keys));
        }

        return result;
    }

    /// <summary>The notice row appended when a source has more rows than <see cref="MaxRows"/>: the first column carries the message, the rest are empty.</summary>
    /// <param name="columns">The result's columns.</param>
    /// <returns>The row.</returns>
    public static IReadOnlyDictionary<string, object?> CapNotice(IEnumerable<string> columns)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach (string column in columns)
        {
            row[column] = row.Count == 0 ? CapNoticeText : null;
        }

        return row;
    }

    /// <summary>The text of the notice row.</summary>
    public static string CapNoticeText => $"Only the first {MaxRows:N0} rows are shown; narrow the parameters or filter the source to see the rest.";

    /// <inheritdoc />
    public async Task<Core.Common.Result<IReadOnlyList<ReportFieldInfo>>> DescribeAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!CanHandle(command ?? string.Empty))
        {
            return Core.Common.Error.Validation("Rpt.Source", "Fields can be discovered for SQL, view and function sources (sql:…, view:…, function:…).");
        }

        (string? sql, string? refusal) = Statement(command!, [.. parameters.Keys]);
        if (sql is null)
        {
            return Core.Common.Error.Validation("Rpt.Source", refusal ?? "The source was refused.");
        }

        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await BeginGuardedAsync(connection, ct).ConfigureAwait(false);

        // SELECT ... LIMIT 0: the shape of the result, no rows read.
        await using NpgsqlCommand describe = new($"SELECT * FROM ({sql}) AS report_rows LIMIT 0", connection, transaction);
        Bind(describe.Parameters, companyId, parameters);

        try
        {
            await using NpgsqlDataReader reader = await describe.ExecuteReaderAsync(System.Data.CommandBehavior.SchemaOnly, ct).ConfigureAwait(false);
            IReadOnlyList<ReportFieldInfo> fields = [.. (await reader.GetColumnSchemaAsync(ct).ConfigureAwait(false)).Select(c => new ReportFieldInfo(c.ColumnName, c.DataTypeName))];
            return Core.Common.Result<IReadOnlyList<ReportFieldInfo>>.Success(fields);
        }
        catch (PostgresException ex)
        {
            return Core.Common.Error.Validation("Rpt.Source", ex.MessageText);
        }
    }

    [GeneratedRegex(@"^rpt\.[a-z_][a-z0-9_]*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportingObject();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
