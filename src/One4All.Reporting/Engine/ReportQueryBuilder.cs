using System.Text;
using System.Text.RegularExpressions;

namespace One4All.Reporting.Engine;

/// <summary>A filter in a built query: a column compared with a report parameter.</summary>
/// <param name="Column">The column.</param>
/// <param name="Operator">=, &lt;&gt;, &lt;, &lt;=, &gt;, &gt;= or LIKE.</param>
/// <param name="Parameter">The report parameter it is compared with.</param>
public sealed record QueryFilter(string Column, string Operator, string Parameter);

/// <summary>
/// The designer's query builder (brief §13.1 "a designer-side query builder"): a SELECT over a reporting view from chosen columns, parameter filters and an
/// order, always limited to the company. Identifiers are checked, never quoted from free text. Pure.
/// </summary>
public static partial class ReportQueryBuilder
{
    private static readonly string[] Operators = ["=", "<>", "<", "<=", ">", ">=", "LIKE"];

    /// <summary>Builds the query.</summary>
    /// <param name="view">The reporting view (rpt.name).</param>
    /// <param name="columns">The columns (none: all).</param>
    /// <param name="filters">The filters.</param>
    /// <param name="orderBy">The order (a column, optionally followed by DESC).</param>
    /// <returns>The SQL command (sql:…), or why it cannot be built.</returns>
    public static (string? Command, string? Refusal) Build(string view, IReadOnlyList<string> columns, IReadOnlyList<QueryFilter> filters, IReadOnlyList<string> orderBy)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(orderBy);
        if (!ViewName().IsMatch(view ?? string.Empty))
        {
            return (null, "Choose a reporting view (rpt.<name>).");
        }

        if (columns.FirstOrDefault(c => !Identifier().IsMatch(c)) is string badColumn)
        {
            return (null, $"\"{badColumn}\" is not a column name.");
        }

        StringBuilder sql = new($"SELECT {(columns.Count == 0 ? "*" : string.Join(", ", columns))} FROM {view!.ToLowerInvariant()} WHERE company_id = @CompanyId");
        foreach (QueryFilter filter in filters)
        {
            string op = (filter.Operator ?? string.Empty).Trim().ToUpperInvariant();
            if (!Identifier().IsMatch(filter.Column) || !Identifier().IsMatch(filter.Parameter) || !Operators.Contains(op))
            {
                return (null, $"The filter {filter.Column} {filter.Operator} @{filter.Parameter} is not a column, a comparison and a parameter.");
            }

            sql.Append($" AND {filter.Column} {op} @{filter.Parameter}");
        }

        List<string> order = [];
        foreach (string item in orderBy)
        {
            string[] parts = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 0 or > 2 || !Identifier().IsMatch(parts[0]) || (parts.Length == 2 && !parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase) && !parts[1].Equals("ASC", StringComparison.OrdinalIgnoreCase)))
            {
                return (null, $"\"{item}\" is not a column to order by.");
            }

            order.Add(parts.Length == 2 ? $"{parts[0]} {parts[1].ToUpperInvariant()}" : parts[0]);
        }

        if (order.Count > 0)
        {
            sql.Append(" ORDER BY ").Append(string.Join(", ", order));
        }

        return ("sql:" + sql, null);
    }

    [GeneratedRegex(@"^rpt\.[a-z_][a-z0-9_]*$", RegexOptions.IgnoreCase)]
    private static partial Regex ViewName();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
