using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>A new employee (brief §4.15.1).</summary>
public sealed record NewEmployee(string EmployeeNumber, string FullName, decimal BasicSalary, DateOnly EngagementDate)
{
    /// <summary>Permanent, contract or casual.</summary>
    public EmploymentType EmploymentType { get; init; }

    /// <summary>Department.</summary>
    public string? Department { get; init; }

    /// <summary>Cost centre.</summary>
    public string? CostCentre { get; init; }

    /// <summary>Pay group (weekly, monthly...).</summary>
    public string? PayGroup { get; init; }

    /// <summary>Grade.</summary>
    public string? Grade { get; init; }

    /// <summary>Notch.</summary>
    public int? Notch { get; init; }

    /// <summary>National ID.</summary>
    public string? NationalIdNumber { get; init; }

    /// <summary>Tax number.</summary>
    public string? TaxNumber { get; init; }

    /// <summary>Gender.</summary>
    public Gender Gender { get; init; }

    /// <summary>Date of birth.</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>Contract end (contract staff).</summary>
    public DateOnly? ContractEndDate { get; init; }

    /// <summary>Probation end.</summary>
    public DateOnly? ProbationEndDate { get; init; }

    /// <summary>How they are paid.</summary>
    public PayMethod PaymentMethod { get; init; } = PayMethod.BankTransfer;

    /// <summary>Bank (set once at engagement; later changes need a second person's approval).</summary>
    public string? BankName { get; init; }

    /// <summary>Bank branch.</summary>
    public string? BankBranch { get; init; }

    /// <summary>Bank account number.</summary>
    public string? BankAccountNumber { get; init; }

    /// <summary>Mobile money number.</summary>
    public string? MobileMoneyNumber { get; init; }

    /// <summary>Email.</summary>
    public string? Email { get; init; }

    /// <summary>How often they are paid.</summary>
    public PayFrequency? PayFrequency { get; init; }

    /// <summary>The branch code they work at.</summary>
    public string? Branch { get; init; }

    /// <summary>The GL project their pay is charged to.</summary>
    public string? Project { get; init; }

    /// <summary>The GL fund their pay is charged to.</summary>
    public string? Fund { get; init; }

    /// <summary>The next of kin's name.</summary>
    public string? NextOfKinName { get; init; }

    /// <summary>The next of kin's phone.</summary>
    public string? NextOfKinPhone { get; init; }

    /// <summary>How the next of kin is related.</summary>
    public string? NextOfKinRelationship { get; init; }

    /// <summary>The marital status.</summary>
    public MaritalStatus? MaritalStatus { get; init; }

    /// <summary>The nationality.</summary>
    public string? Nationality { get; init; }

    /// <summary>The social security number.</summary>
    public string? SocialSecurityNumber { get; init; }

    /// <summary>The trade union.</summary>
    public string? UnionMembership { get; init; }

    /// <summary>The employee number of whom they report to.</summary>
    public string? ReportsTo { get; init; }
}

/// <summary>Changes to an employee's record (a null leaves the field as it is). Bank details are not here: they change through an approved request.</summary>
/// <summary>An element's options (see <see cref="NewPayElement"/> for each).</summary>
/// <param name="Recovers">What the deduction recovers.</param>
/// <param name="BenefitCategory">The taxable-benefit category, or null.</param>
/// <param name="PaidInCurrency">The currency paid in, or null for the base currency.</param>
/// <param name="CumulativeTax">Cumulative tax.</param>
/// <param name="SplitCurrencyTax">Tax per currency.</param>
/// <param name="IsAnnualPayment">An annual payment.</param>
public sealed record PayElementOptions(PayRecovery Recovers = PayRecovery.None, string? BenefitCategory = null, string? PaidInCurrency = null, bool CumulativeTax = false, bool SplitCurrencyTax = false,
    bool IsAnnualPayment = false);

public sealed record EmployeeUpdate
{
    /// <summary>The employee's council / employer housing tenancy by its number (rent is recovered from pay); empty string clears it.</summary>
    public string? HousingTenancyNumber { get; init; }

    /// <summary>Full name.</summary>
    public string? FullName { get; init; }

    /// <summary>Basic salary.</summary>
    public decimal? BasicSalary { get; init; }

    /// <summary>Employment type.</summary>
    public EmploymentType? EmploymentType { get; init; }

    /// <summary>Department.</summary>
    public string? Department { get; init; }

    /// <summary>Cost centre.</summary>
    public string? CostCentre { get; init; }

    /// <summary>Pay group.</summary>
    public string? PayGroup { get; init; }

    /// <summary>Grade.</summary>
    public string? Grade { get; init; }

    /// <summary>Notch.</summary>
    public int? Notch { get; init; }

    /// <summary>National ID.</summary>
    public string? NationalIdNumber { get; init; }

    /// <summary>Tax number.</summary>
    public string? TaxNumber { get; init; }

    /// <summary>Contract end.</summary>
    public DateOnly? ContractEndDate { get; init; }

    /// <summary>Probation end.</summary>
    public DateOnly? ProbationEndDate { get; init; }

    /// <summary>Work permit expiry.</summary>
    public DateOnly? WorkPermitExpiry { get; init; }

    /// <summary>How they are paid.</summary>
    public PayMethod? PaymentMethod { get; init; }

    /// <summary>Email.</summary>
    public string? Email { get; init; }

    /// <summary>How often they are paid.</summary>
    public PayFrequency? PayFrequency { get; init; }

    /// <summary>The branch code they work at.</summary>
    public string? Branch { get; init; }

    /// <summary>The GL project their pay is charged to.</summary>
    public string? Project { get; init; }

    /// <summary>The GL fund their pay is charged to.</summary>
    public string? Fund { get; init; }

    /// <summary>The next of kin's name.</summary>
    public string? NextOfKinName { get; init; }

    /// <summary>The next of kin's phone.</summary>
    public string? NextOfKinPhone { get; init; }

    /// <summary>How the next of kin is related.</summary>
    public string? NextOfKinRelationship { get; init; }

    /// <summary>The marital status.</summary>
    public MaritalStatus? MaritalStatus { get; init; }

    /// <summary>The nationality.</summary>
    public string? Nationality { get; init; }

    /// <summary>The social security number.</summary>
    public string? SocialSecurityNumber { get; init; }

    /// <summary>The trade union.</summary>
    public string? UnionMembership { get; init; }

    /// <summary>The employee number of whom they report to.</summary>
    public string? ReportsTo { get; init; }
}

/// <summary>A new pay element.</summary>
public sealed record NewPayElement(string Code, string Name, PayElementType Type, CalculationBasis Basis, int Sequence, string AccountCode)
{
    /// <summary>Fixed amount (fixed basis).</summary>
    public decimal FixedAmount { get; init; }

    /// <summary>Rate (percentage and per-unit bases).</summary>
    public decimal Rate { get; init; }

    /// <summary>The variable a table-lookup element looks up (BASIC when blank).</summary>
    public string? LookupVariable { get; init; }

    /// <summary>A table-lookup element's bands ("0-1000:50; 1000-5000:2.5%; 5000-:150").</summary>
    public string? LookupBands { get; init; }

    /// <summary>Formula (formula basis), e.g. <c>BASIC</c> or <c>TAXABLE * 10 / 100</c>.</summary>
    public string? Formula { get; init; }

    /// <summary>Whether it counts towards taxable pay.</summary>
    public bool Taxable { get; init; }

    /// <summary>Tax table (tax-table basis).</summary>
    public string? TaxTableCode { get; init; }

    /// <summary>Expense account of an employer contribution.</summary>
    public string? ContributionExpenseAccountCode { get; init; }

    /// <summary>Counts towards pensionable pay.</summary>
    public bool Pensionable { get; init; }

    /// <summary>Subject to social security.</summary>
    public bool SubjectToSocialSecurity { get; init; }

    /// <summary>In the overtime base.</summary>
    public bool OvertimeBase { get; init; }

    /// <summary>In the leave-pay base.</summary>
    public bool LeavePayBase { get; init; }

    /// <summary>Pro-rated for a part period (someone joining or leaving mid-period), like basic pay.</summary>
    public bool ProRated { get; init; }

    /// <summary>Whether a deduction net-pay protection cannot take in full goes into arrears (on by default).</summary>
    public bool ArrearsRecoverable { get; init; } = true;

    /// <summary>Counts towards terminal benefits (the TERMINALBASE formula variable).</summary>
    public bool TerminalBenefit { get; init; }

    /// <summary>Subject to union dues (the UNIONBASE formula variable).</summary>
    public bool UnionDues { get; init; }

    /// <summary>What the deduction recovers (staff purchases at the till, housing rent on the employee's tenancy): the run nets off what was recovered before.</summary>
    public PayRecovery Recovers { get; init; }

    /// <summary>The taxable-benefit category this element reports under (PR34), e.g. Housing, Vehicle; null: not a benefit.</summary>
    public string? BenefitCategory { get; init; }

    /// <summary>The currency the element is paid in when not the base currency (its own bank file at the pay date's rate).</summary>
    public string? PaidInCurrency { get; init; }

    /// <summary>Tax computed cumulatively over the tax year (year-to-date earnings and tax).</summary>
    public bool CumulativeTax { get; init; }

    /// <summary>Tax computed per currency on the apportioned threshold and bands (brief §4.15.2).</summary>
    public bool SplitCurrencyTax { get; init; }

    /// <summary>An annual payment (bonus) whose tax is spread over the year.</summary>
    public bool IsAnnualPayment { get; init; }

    /// <summary>The element group an earning adds to.</summary>
    public string? Group { get; init; }

    /// <summary>The group a percent-of-group element is a percentage of.</summary>
    public string? BasisGroup { get; init; }

    /// <summary>The least it comes to.</summary>
    public decimal? Floor { get; init; }

    /// <summary>The most it comes to.</summary>
    public decimal? Ceiling { get; init; }

    /// <summary>The unit it is rounded to.</summary>
    public decimal? RoundTo { get; init; }

    /// <summary>How it is rounded.</summary>
    public PayRounding Rounding { get; init; }

    /// <summary>Expense accounts per department, "DEPT=ACCOUNT;…".</summary>
    public string? DepartmentAccounts { get; init; }
}

/// <summary>One band of a tax table: taxed at the rate from <paramref name="From"/> up to <paramref name="To"/> (open-ended when null).</summary>
public sealed record TaxBandInput(decimal From, decimal? To, decimal RatePercent);

/// <summary>A request to change an employee's bank or mobile-money details, waiting for a second person.</summary>
public sealed record BankChangeRequest(long RequestId, string EmployeeNumber, string Field, string? OldValue, string NewValue, long? RequestedByUserId);

/// <summary>A split of net pay to add, change or take off, asked for and approved like any other bank-detail change (brief §4.15.1).</summary>
/// <param name="Method">How that part is paid.</param>
/// <param name="Destination">The account or wallet number.</param>
/// <param name="Amount">A fixed amount each period (or null with a percentage).</param>
/// <param name="Percent">A share of net pay (or null with an amount).</param>
/// <param name="BankName">The bank of that account (null: the employee's bank).</param>
/// <param name="BankBranch">Its branch.</param>
/// <param name="Remove">True to take the split to that destination off.</param>
public sealed record PaymentSplitChange(PayMethod Method, string Destination, decimal? Amount, decimal? Percent, string? BankName, string? BankBranch, bool Remove = false);

/// <summary>
/// Payroll master data (brief §4.15.1–4.15.2): employees, pay elements and their assignment, tax tables, a standard element set, and bank
/// detail changes that need a second person's approval.
/// </summary>
public interface IPayrollSetupService
{
    /// <summary>Engages an employee.</summary>
    Task<Result<long>> CreateEmployeeAsync(long companyId, NewEmployee employee, CancellationToken ct = default);

    /// <summary>Changes an employee's record (not bank details).</summary>
    Task<Result> UpdateEmployeeAsync(long companyId, string employeeNumber, EmployeeUpdate update, CancellationToken ct = default);

    /// <summary>Creates a pay element.</summary>
    Task<Result<long>> CreatePayElementAsync(long companyId, NewPayElement element, CancellationToken ct = default);

    /// <summary>
    /// Works a formula out before it is saved on an element (brief §4.15.2 "formula preview"): against an employee's basic pay (BASIC, GROSS) and any other
    /// variables given, so a mistyped formula is caught at the desk and not in the run.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="formula">The formula.</param>
    /// <param name="employeeNumber">The employee whose basic pay it uses (blank: none).</param>
    /// <param name="values">Other variables (OVERTIMEHOURS, an element code…).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The amount, or why the formula fails.</returns>
    Task<Result<decimal>> PreviewFormulaAsync(long companyId, string formula, string? employeeNumber, IReadOnlyDictionary<string, decimal> values, CancellationToken ct = default);

    /// <summary>
    /// Changes a pay element in full (brief §4.15.2): everything it was created with but its code and type - name, basis, sequence, account, amounts, formula, tax table,
    /// the bases it counts in, group, limits, rounding, lookup table and options - checked as a new element is. Runs already closed keep what they paid.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="element">The element as it should be, found by its code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> EditPayElementAsync(long companyId, NewPayElement element, CancellationToken ct = default);

    /// <summary>Changes a pay element's amount, rate, formula or taxability.</summary>
    Task<Result> UpdatePayElementAsync(long companyId, string code, decimal? fixedAmount, decimal? rate, string? formula, bool? taxable, CancellationToken ct = default);

    /// <summary>Sets an element's options after it exists: what it recovers, its benefit category, the currency it is paid in, and how its tax is computed.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The element.</param>
    /// <param name="options">The options (every one is applied).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SetPayElementOptionsAsync(long companyId, string code, PayElementOptions options, CancellationToken ct = default);

    /// <summary>Saves a tax credit (brief §4.15.3): fixed a month, or a percentage of a pay element; for everyone or only those given it; optionally from an age.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="credit">The credit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or what is wrong with it.</returns>
    Task<Result> SaveTaxCreditAsync(long companyId, TaxCreditInput credit, CancellationToken ct = default);

    /// <summary>Gives a tax credit to an employee, or takes it away.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeNumber">The employee.</param>
    /// <param name="creditCode">The credit.</param>
    /// <param name="given">True to give it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an unknown employee or credit.</returns>
    Task<Result> SetEmployeeTaxCreditAsync(long companyId, string employeeNumber, string creditCode, bool given, CancellationToken ct = default);

    /// <summary>Gives an employee a pay element (with its own amount, rate or units if they differ).</summary>
    Task<Result> AssignElementAsync(long companyId, string employeeNumber, string elementCode, decimal? overrideAmount = null, decimal? overrideRate = null, decimal? units = null, CancellationToken ct = default)
        => AssignElementAsync(companyId, employeeNumber, elementCode, overrideAmount, overrideRate, units, null, ct);

    /// <summary>Assigns a pay element with a balance to recover (a balance-driven element: an advance, a deposit), taken off period by period.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeNumber">The employee.</param>
    /// <param name="elementCode">The element.</param>
    /// <param name="overrideAmount">The employee's own amount per period, or null for the element's.</param>
    /// <param name="overrideRate">The employee's own rate, or null.</param>
    /// <param name="units">Units for a rate-per-unit element.</param>
    /// <param name="balance">The balance to recover, for a balance-driven element.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> AssignElementAsync(long companyId, string employeeNumber, string elementCode, decimal? overrideAmount, decimal? overrideRate, decimal? units, decimal? balance, CancellationToken ct = default);

    /// <summary>Stops a pay element for an employee.</summary>
    Task<Result> EndElementAsync(long companyId, string employeeNumber, string elementCode, CancellationToken ct = default);

    /// <summary>Creates a tax table (bands must start at nil and follow on from each other; only the last is open-ended).</summary>
    Task<Result<long>> CreateTaxTableAsync(long companyId, string code, DateOnly effectiveFrom, CurrencyCode? currency, IReadOnlyList<TaxBandInput> bands, CancellationToken ct = default);

    /// <summary>
    /// Creates the standard pay elements the company does not have yet (brief §4.15.2): basic, overtime (1.5×, 2× and public holiday), the
    /// allowances, commission, bonus and 13th cheque, backpay, leave pay and encashment, gratuity, severance and notice pay; and PAYE (on the
    /// given tax table), pension, social security, medical aid, union dues, advances, garnishee orders, funeral and savings schemes, staff
    /// purchases, housing rent and overpayment recovery.
    /// </summary>
    /// <returns>The codes created.</returns>
    Task<Result<IReadOnlyList<string>>> SeedStandardElementsAsync(long companyId, string salaryExpenseAccount, string payeLiabilityAccount, string taxTableCode, CancellationToken ct = default);

    /// <summary>Asks for a change of an employee's bank or mobile-money details (BankName, BankBranch, BankAccountNumber, MobileMoneyNumber).</summary>
    Task<Result<IReadOnlyList<long>>> RequestBankChangeAsync(long companyId, string employeeNumber, IReadOnlyDictionary<string, string> changes, long requestedByUserId, CancellationToken ct = default);

    /// <summary>Bank-detail changes waiting for approval.</summary>
    Task<IReadOnlyList<BankChangeRequest>> PendingBankChangesAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Asks for a split of an employee's net pay to be added, changed or taken off. Like a bank-detail change it waits for a different user to approve it
    /// (through <see cref="DecideBankChangeAsync"/>) before the pay is sent anywhere new, and the run's bank-details-changed check sees it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeNumber">The employee.</param>
    /// <param name="change">The split.</param>
    /// <param name="requestedByUserId">Who asks; they cannot approve it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The request id, or why not.</returns>
    Task<Result<long>> RequestPaymentSplitAsync(long companyId, string employeeNumber, PaymentSplitChange change, long requestedByUserId, CancellationToken ct = default);

    /// <summary>Approves (applies) or rejects a bank-detail change; the person who asked cannot decide it.</summary>
    Task<Result> DecideBankChangeAsync(long companyId, long requestId, bool approve, long decidedByUserId, string? note, CancellationToken ct = default);
}

/// <summary>A tax credit to save.</summary>
/// <param name="Code">The code.</param>
/// <param name="Name">The name.</param>
/// <param name="Basis">How it is worked out.</param>
/// <param name="Amount">The monthly amount (fixed).</param>
/// <param name="Percent">The percentage (of an element).</param>
/// <param name="ElementCode">The element it is a percentage of.</param>
/// <param name="MinimumAge">The age it applies from (null: any).</param>
/// <param name="AppliesToAll">Whether every employee has it.</param>
/// <param name="IsActive">Whether it is in use.</param>
public sealed record TaxCreditInput(string Code, string Name, TaxCreditBasis Basis, decimal Amount, decimal Percent, string? ElementCode, int? MinimumAge, bool AppliesToAll, bool IsActive = true);
