using One4All.Core.Accounting;
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

    /// <summary>Shown on the payslip for information (leave days, an employer's notional cost); neither paid nor posted.</summary>
    Memo = 4,

    /// <summary>An employer accrual (leave pay, a thirteenth cheque, gratuity): Dr its expense, Cr the provision; not paid on the payslip.</summary>
    Provision = 5,
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

    /// <summary>Income tax from the effective-dated tax table in force (brief §4.15.3), per period or cumulative year-to-date.</summary>
    TaxTable = 5,

    /// <summary>A percentage of a named element group's total this period (<see cref="PayElement.BasisGroup"/>).</summary>
    PercentOfGroup = 6,

    /// <summary>An amount (or percentage) looked up in the element's table by a variable's value (<see cref="PayElement.LookupBands"/>).</summary>
    TableLookup = 7,

    /// <summary>A fixed amount per period until the employee's balance for the element (an advance, a deposit being recovered) is used up.</summary>
    BalanceDriven = 8,
}

/// <summary>
/// A configured pay element (brief §4.15.2, <c>pay.element</c>) - never code. Carries its calculation
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

    /// <summary>The calculation sequence (order matters - e.g. pension before tax).</summary>
    public int Sequence { get; private set; }

    /// <summary>A default fixed amount (for <see cref="CalculationBasis.FixedAmount"/>).</summary>
    public decimal FixedAmount { get; set; }

    /// <summary>A default rate (for percentage or rate-per-unit bases).</summary>
    public decimal Rate { get; set; }

    /// <summary>The formula (for <see cref="CalculationBasis.Formula"/>), over variables like BASIC/GROSS/TAXABLE and element codes.</summary>
    public string? Formula { get; set; }

    /// <summary>Gets or sets the variable a table-lookup element looks up (BASIC when blank).</summary>
    public string? LookupVariable { get; set; }

    /// <summary>Gets or sets a table-lookup element's bands ("0-1000:50; 1000-5000:2.5%; 5000-:150").</summary>
    public string? LookupBands { get; set; }

    /// <summary>Whether the element is taxable (earnings) or pre-tax (deductions).</summary>
    public bool Taxable { get; set; }

    /// <summary>The GL account (expense for earnings, liability for deductions and employer contributions).</summary>
    public string AccountCode { get; private set; }

    /// <summary>
    /// For an employer contribution, the expense account its cost is charged to (the contribution is owed on
    /// <see cref="AccountCode"/>). A run refuses a non-zero contribution without one rather than leave it off the ledger.
    /// </summary>
    public string? ContributionExpenseAccountCode { get; set; }

    /// <summary>Gets or sets the taxable-benefit category (motor vehicle, housing, loan, school fees) when the earning is a benefit in kind.</summary>
    public string? BenefitCategory { get; set; }

    /// <summary>
    /// Gets or sets what the deduction recovers from the employee, if anything: staff purchases on the POS staff-account tender
    /// (formula variable STAFFPURCHASES) or council / employer housing rent (HOUSINGRENT, receipted to the tenancy's account).
    /// </summary>
    public PayRecovery Recovers { get; set; }

    /// <summary>
    /// Gets or sets the currency the element is paid in for a split-currency payroll (null = the base currency). Amounts stay in base;
    /// the multi-currency tax split (PR35) converts them at the pay date's rate.
    /// </summary>
    public CurrencyCode? PaidInCurrency { get; set; }

    /// <summary>
    /// Gets or sets the deduction's suspension priority under net-pay protection: 0 (the default - tax, pension, court orders) is never
    /// suspended; otherwise the highest number is suspended first.
    /// </summary>
    public int SuspensionPriority { get; set; }

    /// <summary>Gets or sets the tax table a <see cref="CalculationBasis.TaxTable"/> element uses (null: the base table in force).</summary>
    public string? TaxTableCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a tax-table element is cumulative: the year-to-date taxable pay is taxed on the table's bands
    /// times the period number, less the tax already deducted this year (the tax year runs January to December).
    /// </summary>
    public bool CumulativeTax { get; set; }

    /// <summary>
    /// A tax-table element that taxes each currency's earnings apart (split-currency remuneration, brief §4.15.3): earnings paid in a currency with
    /// its own tax table are converted at the pay date's rate, taxed on that table and converted back; the rest bear their share of the base
    /// table's tax on the whole. Applies to period (not cumulative) tax.
    /// </summary>
    public bool SplitCurrencyTax { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the earning is an annual payment (bonus, 13th cheque): a non-cumulative tax-table element taxes
    /// it annualised and apportioned - the annual tax on twelve periods of regular pay plus the payment, less that on the regular pay alone -
    /// rather than at one period's marginal rate.
    /// </summary>
    public bool IsAnnualPayment { get; set; }

    /// <summary>Gets or sets a value indicating whether an earning counts towards pensionable pay (formula variable PENSIONABLE).</summary>
    public bool Pensionable { get; set; }

    /// <summary>Gets or sets a value indicating whether an earning is subject to social security (formula variable SOCIALSECURITYBASE).</summary>
    public bool SubjectToSocialSecurity { get; set; }

    /// <summary>Gets or sets a value indicating whether an earning is in the overtime rate's base (formula variable OVERTIMEBASE).</summary>
    public bool OvertimeBase { get; set; }

    /// <summary>Gets or sets a value indicating whether an earning is in leave pay's base (formula variable LEAVEBASE).</summary>
    public bool LeavePayBase { get; set; }

    /// <summary>Gets or sets a value indicating whether an earning is pro-rated for a part period (someone joining or leaving mid-period), like basic pay.</summary>
    public bool ProRated { get; set; }

    /// <summary>Gets or sets a value indicating whether a deduction that net-pay protection cannot take in full goes into arrears to be recovered later (on by default).</summary>
    public bool ArrearsRecoverable { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether an earning counts towards terminal benefits (the <c>TERMINALBASE</c> formula variable).</summary>
    public bool TerminalBenefit { get; set; }

    /// <summary>Gets or sets a value indicating whether an earning is subject to union dues (the <c>UNIONBASE</c> formula variable a dues element can take a percentage of).</summary>
    public bool UnionDues { get; set; }

    /// <summary>Gets or sets the group an earning adds to (formula variable GROUP_name; a <see cref="CalculationBasis.PercentOfGroup"/> element's base).</summary>
    public string? Group { get; set; }

    /// <summary>Gets or sets the group a <see cref="CalculationBasis.PercentOfGroup"/> element is a percentage of.</summary>
    public string? BasisGroup { get; set; }

    /// <summary>Gets or sets the least the element comes to in a period (when it is not zero).</summary>
    public decimal? Floor { get; set; }

    /// <summary>Gets or sets the most the element comes to in a period (e.g. a social security ceiling).</summary>
    public decimal? Ceiling { get; set; }

    /// <summary>Gets or sets the unit the amount is rounded to (0.05, 1, 10…); null for cents.</summary>
    public decimal? RoundTo { get; set; }

    /// <summary>Gets or sets how the amount is rounded to <see cref="RoundTo"/>.</summary>
    public PayRounding Rounding { get; set; }

    /// <summary>Gets or sets the expense account per department, as "DEPT=ACCOUNT;…" (the element's own account elsewhere).</summary>
    public string? DepartmentAccounts { get; set; }

    /// <summary>The amount after the element's floor, ceiling and rounding rules.</summary>
    /// <param name="amount">The calculated amount.</param>
    /// <returns>The amount to pay.</returns>
    public decimal ApplyLimits(decimal amount)
    {
        if (Floor is decimal floor && amount != 0m && amount < floor)
        {
            amount = floor;
        }

        if (Ceiling is decimal ceiling && amount > ceiling)
        {
            amount = ceiling;
        }

        if (RoundTo is decimal unit and > 0m)
        {
            decimal units = amount / unit;
            units = Rounding switch
            {
                PayRounding.Up => Math.Ceiling(units),
                PayRounding.Down => Math.Floor(units),
                _ => Math.Round(units, MidpointRounding.AwayFromZero),
            };
            amount = units * unit;
        }

        return decimal.Round(amount, 2, MidpointRounding.ToEven);
    }

    /// <summary>The account an amount for an employee of a department posts to.</summary>
    /// <param name="department">The employee's department code.</param>
    /// <param name="fallback">The account otherwise.</param>
    /// <returns>The account.</returns>
    public string AccountFor(string? department, string fallback)
    {
        if (string.IsNullOrWhiteSpace(department) || string.IsNullOrWhiteSpace(DepartmentAccounts))
        {
            return fallback;
        }

        foreach (string pair in DepartmentAccounts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = pair.Split('=', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[0], department.Trim(), StringComparison.OrdinalIgnoreCase) && parts[1].Length > 0)
            {
                return parts[1].ToUpperInvariant();
            }
        }

        return fallback;
    }
}

/// <summary>How a pay element's amount is rounded to its unit.</summary>
public enum PayRounding
{
    /// <summary>To the nearest unit.</summary>
    Nearest = 0,

    /// <summary>Up to the next unit.</summary>
    Up = 1,

    /// <summary>Down to the unit below.</summary>
    Down = 2,
}

/// <summary>A balance a payroll deduction recovers from the employee (brief §4.15.4).</summary>
public enum PayRecovery
{
    /// <summary>Not a recovery.</summary>
    None = 0,

    /// <summary>Staff purchases charged to the employee's staff account at the till.</summary>
    StaffPurchases = 1,

    /// <summary>Rent on the employee's council / employer housing tenancy.</summary>
    HousingRent = 2,
}
