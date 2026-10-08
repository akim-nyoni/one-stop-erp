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
public sealed record EmployeeUpdate
{
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

    /// <summary>Formula (formula basis), e.g. <c>BASIC</c> or <c>TAXABLE * 10 / 100</c>.</summary>
    public string? Formula { get; init; }

    /// <summary>Whether it counts towards taxable pay.</summary>
    public bool Taxable { get; init; }

    /// <summary>Tax table (tax-table basis).</summary>
    public string? TaxTableCode { get; init; }

    /// <summary>Expense account of an employer contribution.</summary>
    public string? ContributionExpenseAccountCode { get; init; }
}

/// <summary>One band of a tax table: taxed at the rate from <paramref name="From"/> up to <paramref name="To"/> (open-ended when null).</summary>
public sealed record TaxBandInput(decimal From, decimal? To, decimal RatePercent);

/// <summary>A request to change an employee's bank or mobile-money details, waiting for a second person.</summary>
public sealed record BankChangeRequest(long RequestId, string EmployeeNumber, string Field, string? OldValue, string NewValue, long? RequestedByUserId);

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

    /// <summary>Changes a pay element's amount, rate, formula or taxability.</summary>
    Task<Result> UpdatePayElementAsync(long companyId, string code, decimal? fixedAmount, decimal? rate, string? formula, bool? taxable, CancellationToken ct = default);

    /// <summary>Gives an employee a pay element (with its own amount, rate or units if they differ).</summary>
    Task<Result> AssignElementAsync(long companyId, string employeeNumber, string elementCode, decimal? overrideAmount = null, decimal? overrideRate = null, decimal? units = null, CancellationToken ct = default);

    /// <summary>Stops a pay element for an employee.</summary>
    Task<Result> EndElementAsync(long companyId, string employeeNumber, string elementCode, CancellationToken ct = default);

    /// <summary>Creates a tax table (bands must start at nil and follow on from each other; only the last is open-ended).</summary>
    Task<Result<long>> CreateTaxTableAsync(long companyId, string code, DateOnly effectiveFrom, CurrencyCode? currency, IReadOnlyList<TaxBandInput> bands, CancellationToken ct = default);

    /// <summary>Creates the standard pay elements the company does not have yet: BASIC, OVERTIME, HOUSING, TRANSPORT and PAYE (on the given tax table).</summary>
    /// <returns>The codes created.</returns>
    Task<Result<IReadOnlyList<string>>> SeedStandardElementsAsync(long companyId, string salaryExpenseAccount, string payeLiabilityAccount, string taxTableCode, CancellationToken ct = default);

    /// <summary>Asks for a change of an employee's bank or mobile-money details (BankName, BankBranch, BankAccountNumber, MobileMoneyNumber).</summary>
    Task<Result<IReadOnlyList<long>>> RequestBankChangeAsync(long companyId, string employeeNumber, IReadOnlyDictionary<string, string> changes, long requestedByUserId, CancellationToken ct = default);

    /// <summary>Bank-detail changes waiting for approval.</summary>
    Task<IReadOnlyList<BankChangeRequest>> PendingBankChangesAsync(long companyId, CancellationToken ct = default);

    /// <summary>Approves (applies) or rejects a bank-detail change; the person who asked cannot decide it.</summary>
    Task<Result> DecideBankChangeAsync(long companyId, long requestId, bool approve, long decidedByUserId, string? note, CancellationToken ct = default);
}
