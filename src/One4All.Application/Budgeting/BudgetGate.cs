using One4All.Core.Budgeting;

namespace One4All.Application.Budgeting;

/// <summary>Chooses the enforcement action for an overrun (brief §4.18.5). Pure.</summary>
public static class BudgetGate
{
    /// <summary>
    /// The most specific rule that matches: a department rule over an any-department one, then the longest account prefix, then the
    /// highest value band reached. With no rule, overruns are allowed.
    /// </summary>
    /// <param name="rules">The company's rules.</param>
    /// <param name="department">The spend's department.</param>
    /// <param name="accountCode">The spend's account.</param>
    /// <param name="value">The spend's value.</param>
    /// <returns>The action.</returns>
    public static BudgetAction ActionFor(IEnumerable<BudgetEnforcementRule> rules, string? department, string accountCode, decimal value)
        => rules
            .Where(r => (r.Department is null || string.Equals(r.Department, department, StringComparison.OrdinalIgnoreCase))
                && (r.AccountPrefix is null || accountCode.StartsWith(r.AccountPrefix, StringComparison.OrdinalIgnoreCase))
                && value >= r.MinimumValue)
            .OrderByDescending(r => r.Department is not null)
            .ThenByDescending(r => r.AccountPrefix?.Length ?? 0)
            .ThenByDescending(r => r.MinimumValue)
            .Select(r => r.Action)
            .FirstOrDefault(BudgetAction.Allow);

    /// <summary>The action for a whole document: the strictest among its lines that overrun (no overrun: allow).</summary>
    /// <param name="lines">Each line's shortfall and action.</param>
    /// <returns>The document's action.</returns>
    public static BudgetAction Overall(IEnumerable<(decimal Shortfall, BudgetAction Action)> lines)
        => lines.Where(l => l.Shortfall > 0m).Select(l => l.Action).DefaultIfEmpty(BudgetAction.Allow).Max();
}
