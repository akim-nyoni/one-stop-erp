using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>The kind of a pay element (brief §4.15.2).</summary>
public enum PayElementType
{
    /// <summary>An earning (adds to gross).</summary>
    Earning = 1,

    /// <summary>A deduction (reduces net).</summary>
    Deduction = 2,

    /// <summary>An employer contribution (cost to employer, not deducted from the employee).</summary>
    EmployerContribution = 3,
}

/// <summary>How a pay element's amount is calculated (brief §4.15.2).</summary>
public enum CalculationBasis
{
    /// <summary>A fixed amount (from the element or the employee override).</summary>
    FixedAmount = 0,

    /// <summary>A percentage of basic salary.</summary>
    PercentOfBasic = 1,

    /// <summary>A percentage of gross earnings to date.</summary>
    PercentOfGross = 2,

    /// <summary>A rate multiplied by units.</summary>
    RatePerUnit = 3,

    /// <summary>A formula evaluated by the shared expression engine (brief §13.1).</summary>
    Formula = 4,
}

/// <summary>
/// A configured pay element (brief §4.15.2, <c>pay.element</c>) — never code. Carries its calculation
/// basis, taxable/pensionable flags, calculation sequence (order matters), and GL account mapping. Formula
/// elements reuse the report expression engine so payroll formulas are written and tested the same way.
/// </summary>
public sealed class PayElement : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PayElement()
    {
        Code = string.Empty;
        Name = string.Empty;
        AccountCode = string.Empty;
    }

    /// <summary>Creates a pay element.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The element code (used as a formula variable).</param>
    /// <param name="name">The element name.</param>
    /// <param name="type">The element type.</param>
    /// <param name="basis">The calculation basis.</param>
    /// <param name="sequence">The calculation sequence (order matters).</param>
    /// <param name="accountCode">The GL account (expense for earnings, liability for deductions).</param>
    public PayElement(long companyId, string code, string name, PayElementType type, CalculationBasis basis, int sequence, string accountCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Type = type;
        Basis = basis;
        Sequence = sequence;
        AccountCode = accountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The element code (used as a formula variable).</summary>
    public string Code { get; private set; }

    /// <summary>The element name.</summary>
    public string Name { get; set; }

    /// <summary>The element type.</summary>
    public PayElementType Type { get; private set; }

    /// <summary>The calculation basis.</summary>
    public CalculationBasis Basis { get; private set; }

    /// <summary>The calculation sequence (order matters — e.g. pension before tax).</summary>
    public int Sequence { get; private set; }

    /// <summary>A default fixed amount (for <see cref="CalculationBasis.FixedAmount"/>).</summary>
    public decimal FixedAmount { get; set; }

    /// <summary>A default rate (for percentage or rate-per-unit bases).</summary>
    public decimal Rate { get; set; }

    /// <summary>The formula (for <see cref="CalculationBasis.Formula"/>), over variables like BASIC/GROSS/TAXABLE and element codes.</summary>
    public string? Formula { get; set; }

    /// <summary>Whether the element is taxable (earnings) or pre-tax (deductions).</summary>
    public bool Taxable { get; set; }

    /// <summary>The GL account (expense for earnings, liability for deductions and employer contributions).</summary>
    public string AccountCode { get; private set; }

    /// <summary>
    /// For an employer contribution, the expense account its cost is charged to (the contribution is owed on
    /// <see cref="AccountCode"/>). A run refuses a non-zero contribution without one rather than leave it off the ledger.
    /// </summary>
    public string? ContributionExpenseAccountCode { get; set; }
}
