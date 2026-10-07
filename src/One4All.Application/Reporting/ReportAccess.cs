using One4All.Application.Security;
using One4All.Core.Security;

namespace One4All.Application.Reporting;

/// <summary>
/// Who is running a report and what they may see (brief §13.3): the restricted columns left out, the data scope rows are cut to, and
/// whether they may run reports of a module or one needing a named permission.
/// </summary>
/// <param name="Hidden">Columns the user may not see.</param>
/// <param name="Scope">The user's data scope (branches, departments, cost centres, warehouses, wards, jobs).</param>
/// <param name="MayRun">Whether the user may run a report of a module that needs a permission (the module, then the report's own required permission).</param>
public sealed record ReportAccess(IReadOnlySet<SensitiveField> Hidden, DataScope Scope, Func<string?, string?, bool> MayRun)
{
    /// <summary>Everything, for runs on the system's own behalf (schedules, tests).</summary>
    public static ReportAccess Unrestricted { get; } = new(new HashSet<SensitiveField>(), DataScope.Everything, (_, _) => true);

    /// <summary>
    /// The access of a user with these permissions and scope: a report of a module that has its own "Reports" permission
    /// (e.g. <c>Payroll.Reports</c>) needs it, as does a report's own required permission.
    /// </summary>
    /// <param name="permissions">The user's permissions.</param>
    /// <param name="scope">The user's scope.</param>
    /// <returns>The access.</returns>
    public static ReportAccess For(EffectivePermissions permissions, DataScope scope)
        => new(FieldSensitivity.HiddenFor(permissions), scope, (module, required) =>
            (string.IsNullOrWhiteSpace(module) || PermissionCatalogue.Find($"{module.Trim()}.Reports") is not PermissionDefinition modulePermission || permissions.Has(modulePermission.Code))
            && (string.IsNullOrWhiteSpace(required) || permissions.Has(required.Trim())));
}

/// <summary>
/// Cuts report rows to a user's data scope (brief §13.3, §4.15.10, §4.18.2): a row naming a branch, department, cost centre, warehouse,
/// ward or job outside the scope is left out — one with the column blank too, so company-wide figures do not reach a restricted user.
/// Rows without such columns pass.
/// </summary>
public static class ReportRowScope
{
    private static readonly (ScopeDimension Dimension, string[] Fields)[] Columns =
    [
        (ScopeDimension.Branch, ["Branch", "BranchCode"]),
        (ScopeDimension.Department, ["Department", "Dept", "DepartmentCode"]),
        (ScopeDimension.CostCentre, ["CostCentre", "CostCenter"]),
        (ScopeDimension.Warehouse, ["Warehouse", "WarehouseCode"]),
        (ScopeDimension.WardZone, ["Ward", "Zone"]),
        (ScopeDimension.Job, ["Job", "JobCode", "Contract"]),
    ];

    /// <summary>The rows the scope allows.</summary>
    /// <param name="rows">The report's rows.</param>
    /// <param name="scope">The user's scope.</param>
    /// <returns>The rows kept.</returns>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Apply(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, DataScope scope)
    {
        List<(ScopeDimension Dimension, string[] Fields)> restricted = [.. Columns.Where(c => scope.Restricts(c.Dimension))];
        if (restricted.Count == 0)
        {
            return rows;
        }

        return [.. rows.Where(row => restricted.All(c =>
        {
            string? field = c.Fields.FirstOrDefault(f => row.ContainsKey(f));
            if (field is null)
            {
                return true;
            }

            string? value = Convert.ToString(row[field], System.Globalization.CultureInfo.InvariantCulture)?.Trim();
            return !string.IsNullOrEmpty(value) && scope.Allows(c.Dimension, value);
        }))];
    }
}
