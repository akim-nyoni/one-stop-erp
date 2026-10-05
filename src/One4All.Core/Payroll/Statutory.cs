using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>What a statutory or third-party scheme is (brief §14 PR19, PR30–PR33).</summary>
public enum SchemeKind
{
    /// <summary>A pension fund.</summary>
    Pension = 1,

    /// <summary>A medical aid society.</summary>
    MedicalAid = 2,

    /// <summary>National social security.</summary>
    SocialSecurity = 3,

    /// <summary>A trade union.</summary>
    Union = 4,

    /// <summary>Workers' compensation.</summary>
    WorkersCompensation = 5,

    /// <summary>A skills / training levy.</summary>
    SkillsLevy = 6,

    /// <summary>The tax authority, for PAYE remitted through payables.</summary>
    TaxAuthority = 7,

    /// <summary>Anything else deducted for a third party.</summary>
    Other = 9,
}

/// <summary>
/// A scheme employees belong to and the payroll deducts or contributes for (<c>hr.statutory_scheme</c>): which pay elements
/// carry the employee's deduction and the employer's contribution, and who it is paid to.
/// </summary>
public sealed class StatutoryScheme : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StatutoryScheme()
    {
        Code = string.Empty;
        Name = string.Empty;
        Provider = string.Empty;
    }

    /// <summary>Sets up a scheme.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">Its code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="kind">What it is.</param>
    /// <param name="provider">Who it is paid to.</param>
    /// <param name="employeeElementCode">The pay element carrying the employee's deduction.</param>
    /// <param name="employerElementCode">The pay element carrying the employer's contribution.</param>
    public StatutoryScheme(long companyId, string code, string name, SchemeKind kind, string provider, string? employeeElementCode, string? employerElementCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Kind = kind;
        Provider = provider;
        EmployeeElementCode = employeeElementCode;
        EmployerElementCode = employerElementCode;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Its code.</summary>
    public string Code { get; private set; }

    /// <summary>Its name.</summary>
    public string Name { get; set; }

    /// <summary>What it is.</summary>
    public SchemeKind Kind { get; private set; }

    /// <summary>Who it is paid to.</summary>
    public string Provider { get; set; }

    /// <summary>The pay element carrying the employee's deduction.</summary>
    public string? EmployeeElementCode { get; set; }

    /// <summary>The pay element carrying the employer's contribution.</summary>
    public string? EmployerElementCode { get; set; }

    /// <summary>Gets or sets the AP supplier the scheme is paid through (§4.15.5); runs raise its invoice when set.</summary>
    public long? SupplierId { get; set; }

    /// <summary>Whether it is in use.</summary>
    public bool IsActive { get; set; }
}

/// <summary>An employee's membership of a scheme, with the scheme's member number (<c>hr.scheme_membership</c>).</summary>
public sealed class SchemeMembership : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SchemeMembership() => MemberNumber = string.Empty;

    /// <summary>Enrols an employee.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="memberNumber">The scheme's number for them.</param>
    /// <param name="joinedOn">When they joined.</param>
    public SchemeMembership(long companyId, long schemeId, long employeeId, string memberNumber, DateOnly joinedOn)
    {
        CompanyId = companyId;
        SchemeId = schemeId;
        EmployeeId = employeeId;
        MemberNumber = memberNumber;
        JoinedOn = joinedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme.</summary>
    public long SchemeId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The scheme's number for them.</summary>
    public string MemberNumber { get; private set; }

    /// <summary>When they joined.</summary>
    public DateOnly JoinedOn { get; private set; }

    /// <summary>When they left; a member while null.</summary>
    public DateOnly? LeftOn { get; private set; }

    /// <summary>Ends the membership.</summary>
    /// <param name="date">When.</param>
    public void Leave(DateOnly date) => LeftOn = date;
}

/// <summary>A payment of deductions and contributions to a scheme for a pay period (<c>hr.scheme_remittance</c>).</summary>
public sealed class SchemeRemittance : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SchemeRemittance()
    {
        PeriodLabel = string.Empty;
        Reference = string.Empty;
    }

    /// <summary>Records a remittance.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="periodLabel">The payroll period it covers.</param>
    /// <param name="amount">The amount paid.</param>
    /// <param name="paidOn">When.</param>
    /// <param name="reference">The payment reference.</param>
    public SchemeRemittance(long companyId, long schemeId, string periodLabel, decimal amount, DateOnly paidOn, string reference)
    {
        CompanyId = companyId;
        SchemeId = schemeId;
        PeriodLabel = periodLabel;
        Amount = amount;
        PaidOn = paidOn;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme.</summary>
    public long SchemeId { get; private set; }

    /// <summary>The payroll period it covers.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>The amount paid.</summary>
    public decimal Amount { get; private set; }

    /// <summary>When.</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>The payment reference.</summary>
    public string Reference { get; private set; }
}

/// <summary>
/// A PAYE tax table (<c>hr.tax_table</c>, brief §14 PR27, PR36): progressive monthly bands effective from a date; the table
/// in force on a pay date is the latest effective by then.
/// </summary>
public sealed class TaxTable : AuditableEntity, ICompanyScoped
{
    private readonly List<TaxBand> _bands = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TaxTable() => Code = string.Empty;

    /// <summary>Creates a table.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">Its code.</param>
    /// <param name="effectiveFrom">When it applies from.</param>
    /// <param name="currency">The currency the table applies to in a split-currency assessment (null = the base table).</param>
    public TaxTable(long companyId, string code, DateOnly effectiveFrom, CurrencyCode? currency = null)
    {
        Currency = currency;
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        EffectiveFrom = effectiveFrom;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Its code.</summary>
    public string Code { get; private set; }

    /// <summary>When it applies from.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The currency the table applies to (null for the base-currency table every other report uses).</summary>
    public CurrencyCode? Currency { get; private set; }

    /// <summary>The bands, lowest first.</summary>
    public IReadOnlyList<TaxBand> Bands => [.. _bands.OrderBy(b => b.From)];

    /// <summary>Adds a band.</summary>
    /// <param name="from">The monthly taxable income it starts at.</param>
    /// <param name="to">Where it ends (open when null).</param>
    /// <param name="ratePercent">The marginal rate.</param>
    public void AddBand(decimal from, decimal? to, decimal ratePercent) => _bands.Add(new TaxBand(from, to, ratePercent));
}

/// <summary>A band of a tax table.</summary>
public sealed class TaxBand : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TaxBand()
    {
    }

    /// <summary>Creates a band.</summary>
    /// <param name="from">The monthly taxable income it starts at.</param>
    /// <param name="to">Where it ends (open when null).</param>
    /// <param name="ratePercent">The marginal rate.</param>
    public TaxBand(decimal from, decimal? to, decimal ratePercent)
    {
        From = from;
        To = to;
        RatePercent = ratePercent;
    }

    /// <summary>The table.</summary>
    public long TaxTableId { get; private set; }

    /// <summary>The monthly taxable income it starts at.</summary>
    public decimal From { get; private set; }

    /// <summary>Where it ends (open when null).</summary>
    public decimal? To { get; private set; }

    /// <summary>The marginal rate.</summary>
    public decimal RatePercent { get; private set; }
}

/// <summary>A payroll run as it was posted (<c>hr.payroll_run</c>, brief §14 PR7): the period, totals, journal and who ran it.</summary>
public sealed class PayrollRunRecord : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PayrollRunRecord()
    {
        PeriodLabel = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records a run.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="employees">Employees paid.</param>
    /// <param name="gross">Gross earnings.</param>
    /// <param name="deductions">Deductions.</param>
    /// <param name="net">Net pay.</param>
    /// <param name="journalNumber">The journal.</param>
    /// <param name="kind">The kind of run.</param>
    /// <param name="documentNumber">The run's document number.</param>
    /// <param name="reason">Why a supplementary, backpay or reversal run was made.</param>
    /// <param name="payGroup">The pay group ("" for the default payroll).</param>
    public PayrollRunRecord(long companyId, string periodLabel, DateOnly payDate, int employees, decimal gross, decimal deductions, decimal net, string journalNumber,
        PayRunKind kind = PayRunKind.Regular, string documentNumber = "", string? reason = null, string payGroup = "")
    {
        PayGroup = payGroup;
        Kind = kind;
        DocumentNumber = documentNumber;
        Reason = reason;
        CompanyId = companyId;
        PeriodLabel = periodLabel;
        PayDate = payDate;
        Employees = employees;
        Gross = gross;
        Deductions = deductions;
        Net = net;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The period.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>The pay date.</summary>
    public DateOnly PayDate { get; private set; }

    /// <summary>Employees paid.</summary>
    public int Employees { get; private set; }

    /// <summary>Gross earnings.</summary>
    public decimal Gross { get; private set; }

    /// <summary>Deductions.</summary>
    public decimal Deductions { get; private set; }

    /// <summary>Net pay.</summary>
    public decimal Net { get; private set; }

    /// <summary>The journal.</summary>
    public string JournalNumber { get; private set; }

    /// <summary>The kind of run.</summary>
    public PayRunKind Kind { get; private set; }

    /// <summary>The run's document number (the payslips' run reference).</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>Why a supplementary, backpay or reversal run was made.</summary>
    public string? Reason { get; private set; }

    /// <summary>The pay group ("" for the default payroll).</summary>
    public string PayGroup { get; private set; } = string.Empty;

    /// <summary>When the run was reversed, if it was.</summary>
    public DateOnly? ReversedOn { get; private set; }

    /// <summary>The reversal run's document number.</summary>
    public string? ReversedBy { get; private set; }

    /// <summary>Who approved the reversal.</summary>
    public long? ReversalApprovedByUserId { get; private set; }

    /// <summary>Gets a value indicating whether the run has been reversed.</summary>
    public bool IsReversed => ReversedOn is not null;

    /// <summary>Marks the run reversed by an approved reversal run.</summary>
    /// <param name="date">The reversal date.</param>
    /// <param name="reversalDocument">The reversal run's document number.</param>
    /// <param name="approvedByUserId">Who approved it.</param>
    public void MarkReversed(DateOnly date, string reversalDocument, long approvedByUserId)
    {
        ReversedOn = date;
        ReversedBy = reversalDocument;
        ReversalApprovedByUserId = approvedByUserId;
    }
}
