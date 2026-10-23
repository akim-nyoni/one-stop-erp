using One4All.Core.Payroll;

namespace One4All.Application.Payroll;

/// <summary>
/// Which payslip lines are statutory: the element's own flag (<see cref="PayElement.StatutoryKind"/>) when it is set, otherwise by name - a
/// deduction whose code or description names AIDS is the AIDS levy, one naming PAYE or tax is income tax.
/// </summary>
public static class TaxLines
{
    /// <summary>Whether a line is income tax.</summary>
    /// <param name="type">The element type.</param>
    /// <param name="code">The element code.</param>
    /// <param name="description">The line description.</param>
    /// <param name="flagged">The element's flag (none: recognised by name).</param>
    /// <returns>True for a tax deduction.</returns>
    public static bool IsTax(PayElementType type, string code, string description, StatutoryKind flagged = StatutoryKind.None)
        => KindOf(type, code, description, flagged) == StatutoryKind.Paye;

    /// <summary>The statutory kind of a line: its element's flag, or for an unflagged deduction what its code or description names.</summary>
    /// <param name="type">The element type.</param>
    /// <param name="code">The element code.</param>
    /// <param name="description">The line description.</param>
    /// <param name="flagged">The element's flag (none: recognised by name).</param>
    /// <returns>The kind; none when it is not a tax or levy line.</returns>
    public static StatutoryKind KindOf(PayElementType type, string code, string description, StatutoryKind flagged = StatutoryKind.None)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(description);
        if (flagged != StatutoryKind.None)
        {
            return flagged;
        }

        if (type != PayElementType.Deduction)
        {
            return StatutoryKind.None;
        }

        if (code.Contains("AIDS", StringComparison.OrdinalIgnoreCase) || description.Contains("AIDS", StringComparison.OrdinalIgnoreCase))
        {
            return StatutoryKind.AidsLevy;
        }

        return code.Contains("PAYE", StringComparison.OrdinalIgnoreCase) || code.Contains("TAX", StringComparison.OrdinalIgnoreCase) || description.Contains("tax", StringComparison.OrdinalIgnoreCase)
            ? StatutoryKind.Paye
            : StatutoryKind.None;
    }
}
