using System.Text.Json.Nodes;
using One4All.Core.Security;

namespace One4All.Application.Security;

/// <summary>Kinds of restricted field (brief §5.1 field-level sensitivity).</summary>
public enum SensitiveField
{
    /// <summary>Cost prices and values.</summary>
    Cost = 1,

    /// <summary>Margins and gross profit.</summary>
    Margin = 2,

    /// <summary>Salaries and pay.</summary>
    Salary = 3,

    /// <summary>Customer credit limits.</summary>
    CreditLimit = 4,
}

/// <summary>
/// Recognises restricted fields by name and strips them from what a user may not see: cost needs a grant with ViewCost, margin one with
/// ViewMargin, salary Payroll › View Salary, credit limit the credit control permission. Analysis codes (cost centre, cost head) and
/// account codes are not restricted. Pure.
/// </summary>
public static class FieldSensitivity
{
    /// <summary>The kind of restricted field a name is, or null.</summary>
    /// <param name="name">The field or column name.</param>
    /// <returns>The kind, or null.</returns>
    public static SensitiveField? Classify(string name)
    {
        string key = new([.. (name ?? string.Empty).Where(char.IsLetter).Select(char.ToLowerInvariant)]);
        if (key.Contains("account", StringComparison.Ordinal) || key.Contains("costcentre", StringComparison.Ordinal) || key.Contains("costcenter", StringComparison.Ordinal)
            || key.Contains("costhead", StringComparison.Ordinal))
        {
            return null;
        }

        if (key.Contains("margin", StringComparison.Ordinal) || key.Contains("grossprofit", StringComparison.Ordinal) || key.Contains("markup", StringComparison.Ordinal))
        {
            return SensitiveField.Margin;
        }

        if (key.Contains("salary", StringComparison.Ordinal) || key.Contains("grosspay", StringComparison.Ordinal) || key.Contains("netpay", StringComparison.Ordinal)
            || key.Contains("basicpay", StringComparison.Ordinal) || key.Contains("grossearnings", StringComparison.Ordinal) || key.Contains("totaldeductions", StringComparison.Ordinal)
            || key.Contains("hourlyrate", StringComparison.Ordinal))
        {
            return SensitiveField.Salary;
        }

        if (key.Contains("creditlimit", StringComparison.Ordinal))
        {
            return SensitiveField.CreditLimit;
        }

        return key.Contains("cost", StringComparison.Ordinal) ? SensitiveField.Cost : null;
    }

    /// <summary>The kinds of field a user may not see.</summary>
    /// <param name="permissions">The user's permissions.</param>
    /// <returns>The hidden kinds (none for a system administrator).</returns>
    public static IReadOnlySet<SensitiveField> HiddenFor(EffectivePermissions permissions)
    {
        HashSet<SensitiveField> hidden = [];
        if (permissions.IsSuperAdministrator)
        {
            return hidden;
        }

        bool Any(Func<string, OperationFlags, bool> test) => permissions.Codes.Any(c => test(c, permissions.OperationsFor(c)));
        if (!Any((_, f) => f.HasFlag(OperationFlags.ViewCost)))
        {
            hidden.Add(SensitiveField.Cost);
        }

        if (!Any((_, f) => f.HasFlag(OperationFlags.ViewMargin)))
        {
            hidden.Add(SensitiveField.Margin);
        }

        // Pay is seen only with Payroll › View Salary: other payroll rights (leave approval, say) do not open it (brief §4.15.10).
        if (!Any((c, f) => f.HasFlag(OperationFlags.View) && string.Equals(c, "Payroll.ViewSalary", StringComparison.OrdinalIgnoreCase)))
        {
            hidden.Add(SensitiveField.Salary);
        }

        if (!Any((c, f) => f != OperationFlags.None && string.Equals(c, "Sales.CreditControl", StringComparison.OrdinalIgnoreCase)))
        {
            hidden.Add(SensitiveField.CreditLimit);
        }

        return hidden;
    }

    /// <summary>Whether a field is hidden.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="hidden">The hidden kinds.</param>
    /// <returns>True when it must not be shown.</returns>
    public static bool IsHidden(string name, IReadOnlySet<SensitiveField> hidden) => hidden.Count > 0 && Classify(name) is SensitiveField kind && hidden.Contains(kind);

    /// <summary>Removes hidden fields from a JSON document, at any depth.</summary>
    /// <param name="node">The document.</param>
    /// <param name="hidden">The hidden kinds.</param>
    /// <returns>How many fields were removed.</returns>
    public static int Strip(JsonNode? node, IReadOnlySet<SensitiveField> hidden)
    {
        int removed = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach (string name in obj.Select(p => p.Key).ToList())
                {
                    if (IsHidden(name, hidden))
                    {
                        obj.Remove(name);
                        removed++;
                    }
                    else
                    {
                        removed += Strip(obj[name], hidden);
                    }
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    removed += Strip(item, hidden);
                }

                break;
        }

        return removed;
    }

    /// <summary>Removes hidden columns from report rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="hidden">The hidden kinds.</param>
    /// <returns>The rows without them.</returns>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Strip(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlySet<SensitiveField> hidden)
        => StripRows(rows, hidden);

    /// <summary>Removes the columns a user may not see from a desktop list (brief §5.1: the same rules as the API and the reports).</summary>
    /// <param name="table">The list.</param>
    /// <param name="hidden">The hidden kinds.</param>
    /// <returns>The number of columns removed.</returns>
    public static int Strip(System.Data.DataTable table, IReadOnlySet<SensitiveField> hidden)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(hidden);
        List<System.Data.DataColumn> gone = [.. table.Columns.Cast<System.Data.DataColumn>().Where(c => IsHidden(c.ColumnName, hidden))];
        foreach (System.Data.DataColumn column in gone)
        {
            table.Columns.Remove(column);
        }

        return gone.Count;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> StripRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlySet<SensitiveField> hidden)
        => hidden.Count == 0 ? rows
            : [.. rows.Select(r => (IReadOnlyDictionary<string, object?>)r.Where(p => !IsHidden(p.Key, hidden)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase))];
}
