namespace One4All.Application.Budgeting;

/// <summary>
/// Department isolation in budget preparation (brief §4.18 "Departments cannot see or edit each other's numbers unless granted; Finance sees everything"):
/// a user tied to departments — through their employee record or a department data scope — reaches only those departments' budgets, unless granted every
/// department. A user tied to no department is not restricted by this rule. Pure.
/// </summary>
public static class BudgetDepartmentAccess
{
    /// <summary>The departments a user is confined to, or null when they reach every department.</summary>
    /// <param name="allDepartments">Whether the user holds the all-departments permission (or is a super administrator).</param>
    /// <param name="ownDepartments">The departments the user belongs to or is scoped to.</param>
    /// <returns>The departments, or null for all.</returns>
    public static IReadOnlySet<string>? Confined(bool allDepartments, IEnumerable<string?> ownDepartments)
    {
        if (allDepartments)
        {
            return null;
        }

        HashSet<string> own = new(ownDepartments.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d!.Trim()), StringComparer.OrdinalIgnoreCase);
        return own.Count == 0 ? null : own;
    }

    /// <summary>Whether a confined user may reach a budget.</summary>
    /// <param name="confined">The departments the user is confined to (null: all).</param>
    /// <param name="budgetDepartment">The budget's department (null: company-wide).</param>
    /// <returns>True when the user may.</returns>
    public static bool Allows(IReadOnlySet<string>? confined, string? budgetDepartment)
        => confined is null || (budgetDepartment is not null && confined.Contains(budgetDepartment.Trim()));
}
