using One4All.Core.Payroll;

namespace One4All.Application.Payroll;

/// <summary>Which payslip lines are income tax (PAYE): any deduction whose code or description names tax.</summary>
public static class TaxLines
{
    /// <summary>Whether a line is income tax.</summary>
    /// <param name="type">The element type.</param>
    /// <param name="code">The element code.</param>
    /// <param name="description">The line description.</param>
    /// <returns>True for a tax deduction.</returns>
    public static bool IsTax(PayElementType type, string code, string description) => type == PayElementType.Deduction
        && (code.Contains("PAYE", StringComparison.OrdinalIgnoreCase) || code.Contains("TAX", StringComparison.OrdinalIgnoreCase) || description.Contains("tax", StringComparison.OrdinalIgnoreCase));
}
