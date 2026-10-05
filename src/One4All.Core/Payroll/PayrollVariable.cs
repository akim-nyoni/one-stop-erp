using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>
/// A variable input for one pay period (<c>pay.payroll_variable</c>, brief §4.15.6): overtime hours, a once-off earning or deduction,
/// keyed by hand or imported. Units feed a rate-per-unit element; an amount replaces the element's calculated amount for the period.
/// An element the employee does not normally have is paid for that period only.
/// </summary>
public sealed class PayrollVariable : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PayrollVariable()
    {
        ElementCode = string.Empty;
        PeriodLabel = string.Empty;
        Source = string.Empty;
    }

    /// <summary>Creates the variable.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="elementCode">The pay element.</param>
    /// <param name="periodLabel">The period it applies to.</param>
    /// <param name="units">Units (for a rate-per-unit element).</param>
    /// <param name="amount">An amount replacing the calculated one.</param>
    /// <param name="source">MANUAL or IMPORT.</param>
    public PayrollVariable(long companyId, long employeeId, string elementCode, string periodLabel, decimal? units, decimal? amount, string source)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        ElementCode = elementCode;
        PeriodLabel = periodLabel;
        Units = units;
        Amount = amount;
        Source = source;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The pay element.</summary>
    public string ElementCode { get; private set; }

    /// <summary>The period.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>Units, for a rate-per-unit element.</summary>
    public decimal? Units { get; private set; }

    /// <summary>An amount replacing the calculated one.</summary>
    public decimal? Amount { get; private set; }

    /// <summary>MANUAL or IMPORT.</summary>
    public string Source { get; private set; }
}
