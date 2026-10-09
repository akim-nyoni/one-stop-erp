using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>The approved establishment for a department and grade (<c>hr.establishment_post</c>).</summary>
public sealed class EstablishmentPost : Entity, ICompanyScoped
{
    private EstablishmentPost()
    {
        Department = string.Empty;
        Grade = string.Empty;
    }

    /// <summary>Sets an establishment line.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="department">The department.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="approvedPosts">The approved number of posts.</param>
    /// <param name="annualCostPerPost">The budgeted annual cost of one post.</param>
    public EstablishmentPost(long companyId, string department, string grade, int approvedPosts, decimal annualCostPerPost)
    {
        CompanyId = companyId;
        Department = department;
        Grade = grade;
        ApprovedPosts = approvedPosts;
        AnnualCostPerPost = annualCostPerPost;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the department.</summary>
    public string Department { get; private set; }

    /// <summary>Gets the grade.</summary>
    public string Grade { get; private set; }

    /// <summary>Gets or sets the approved posts.</summary>
    public int ApprovedPosts { get; set; }

    /// <summary>Gets or sets the annual cost of one post.</summary>
    public decimal AnnualCostPerPost { get; set; }
}

/// <summary>A document held on an employee's file (<c>hr.employee_document</c>): ID, contract, qualification, medical, permit.</summary>
public sealed class EmployeeDocument : Entity, ICompanyScoped
{
    private EmployeeDocument()
    {
        DocumentType = string.Empty;
    }

    /// <summary>Files a document.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="documentType">The type (ID, CONTRACT, MEDICAL…).</param>
    /// <param name="reference">The document number.</param>
    /// <param name="expiresOn">When it expires, if it does.</param>
    public EmployeeDocument(long companyId, long employeeId, string documentType, string? reference, DateOnly? expiresOn)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        DocumentType = documentType.Trim().ToUpperInvariant();
        Reference = reference;
        ExpiresOn = expiresOn;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the type.</summary>
    public string DocumentType { get; private set; }

    /// <summary>Gets the document number.</summary>
    public string? Reference { get; private set; }

    /// <summary>Gets when it expires.</summary>
    public DateOnly? ExpiresOn { get; private set; }
}

/// <summary>The sanction recorded for a disciplinary matter.</summary>
public enum DisciplinarySanction
{
    /// <summary>Verbal warning.</summary>
    VerbalWarning = 1,

    /// <summary>Written warning.</summary>
    WrittenWarning = 2,

    /// <summary>Final written warning.</summary>
    FinalWarning = 3,

    /// <summary>Suspension.</summary>
    Suspension = 4,

    /// <summary>Dismissal.</summary>
    Dismissal = 5,
}

/// <summary>A disciplinary record (<c>hr.disciplinary</c>).</summary>
public sealed class DisciplinaryRecord : Entity, ICompanyScoped
{
    private DisciplinaryRecord()
    {
        Offence = string.Empty;
    }

    /// <summary>Records a sanction.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">The hearing date.</param>
    /// <param name="offence">The offence.</param>
    /// <param name="sanction">The sanction.</param>
    /// <param name="expiresOn">When the warning lapses.</param>
    /// <param name="issuedByUserId">Who issued it.</param>
    public DisciplinaryRecord(long companyId, long employeeId, DateOnly date, string offence, DisciplinarySanction sanction, DateOnly? expiresOn, long? issuedByUserId)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Date = date;
        Offence = offence;
        Sanction = sanction;
        ExpiresOn = expiresOn;
        IssuedByUserId = issuedByUserId;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the hearing date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the offence.</summary>
    public string Offence { get; private set; }

    /// <summary>Gets the sanction.</summary>
    public DisciplinarySanction Sanction { get; private set; }

    /// <summary>Gets when the warning lapses (null: does not lapse).</summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Gets who issued it.</summary>
    public long? IssuedByUserId { get; private set; }
}

/// <summary>Training completed or a qualification held (<c>hr.training</c>).</summary>
public sealed class TrainingRecord : Entity, ICompanyScoped
{
    private TrainingRecord()
    {
        Course = string.Empty;
    }

    /// <summary>Records training.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="course">The course or qualification.</param>
    /// <param name="completedOn">When it was completed.</param>
    /// <param name="expiresOn">When it must be renewed, if ever.</param>
    /// <param name="provider">Who delivered it.</param>
    /// <param name="cost">What it cost.</param>
    public TrainingRecord(long companyId, long employeeId, string course, DateOnly completedOn, DateOnly? expiresOn, string? provider, decimal cost)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Course = course;
        CompletedOn = completedOn;
        ExpiresOn = expiresOn;
        Provider = provider;
        Cost = cost;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the course.</summary>
    public string Course { get; private set; }

    /// <summary>Gets when it was completed.</summary>
    public DateOnly CompletedOn { get; private set; }

    /// <summary>Gets when it must be renewed.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Gets the provider.</summary>
    public string? Provider { get; private set; }

    /// <summary>Gets the cost.</summary>
    public decimal Cost { get; private set; }
}

/// <summary>A performance appraisal (<c>hr.appraisal</c>) with when the next is due.</summary>
public sealed class EmployeeAppraisal : Entity, ICompanyScoped
{
    private EmployeeAppraisal()
    {
    }

    /// <summary>Records an appraisal.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">When it was held.</param>
    /// <param name="rating">The rating (1–5).</param>
    /// <param name="nextDue">When the next is due.</param>
    public EmployeeAppraisal(long companyId, long employeeId, DateOnly date, int rating, DateOnly nextDue)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Date = date;
        Rating = rating;
        NextDue = nextDue;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets when it was held.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the rating.</summary>
    public int Rating { get; private set; }

    /// <summary>Gets when the next is due.</summary>
    public DateOnly NextDue { get; private set; }
}

/// <summary>A garnishee / court order deducted from an employee's pay (<c>hr.garnishee_order</c>) through a pay element.</summary>
public sealed class GarnisheeOrder : Entity, ICompanyScoped
{
    private GarnisheeOrder()
    {
        Reference = string.Empty;
        Creditor = string.Empty;
        ElementCode = string.Empty;
    }

    /// <summary>Registers an order.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="reference">The court order reference.</param>
    /// <param name="creditor">Who is paid.</param>
    /// <param name="priority">The order's priority (1 first).</param>
    /// <param name="instalment">The amount per pay period.</param>
    /// <param name="totalAmount">The total ordered.</param>
    /// <param name="elementCode">The deduction pay element it is collected through.</param>
    /// <param name="issuedOn">When it was issued.</param>
    public GarnisheeOrder(long companyId, long employeeId, string reference, string creditor, int priority, decimal instalment, decimal totalAmount, string elementCode, DateOnly issuedOn)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Reference = reference.Trim().ToUpperInvariant();
        Creditor = creditor;
        Priority = priority;
        Instalment = instalment;
        TotalAmount = totalAmount;
        ElementCode = elementCode.Trim().ToUpperInvariant();
        IssuedOn = issuedOn;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the order reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets the creditor.</summary>
    public string Creditor { get; private set; }

    /// <summary>Gets the priority.</summary>
    public int Priority { get; private set; }

    /// <summary>Gets the instalment.</summary>
    public decimal Instalment { get; private set; }

    /// <summary>Gets the total ordered.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Gets the deduction element.</summary>
    public string ElementCode { get; private set; }

    /// <summary>Gets or sets the AP supplier the creditor is paid through (§4.15.5); runs raise its invoice when set.</summary>
    public long? SupplierId { get; set; }

    /// <summary>Gets when it was issued.</summary>
    public DateOnly IssuedOn { get; private set; }
}

/// <summary>Part of an employee's net pay sent to a second account or method (<c>hr.payment_split</c>); the rest goes to the primary account.</summary>
public sealed class EmployeePaymentSplit : Entity, ICompanyScoped
{
    private EmployeePaymentSplit()
    {
        Destination = string.Empty;
    }

    /// <summary>Creates a split.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="method">How this part is paid.</param>
    /// <param name="destination">The bank account or wallet.</param>
    /// <param name="amount">The fixed amount sent there each period (0 when a percentage is given).</param>
    /// <param name="percent">The share of net pay sent there instead of a fixed amount (brief §4.15.1: "60% to bank A, 40% to mobile money").</param>
    public EmployeePaymentSplit(long companyId, long employeeId, PayMethod method, string destination, decimal amount, decimal? percent = null)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Method = method;
        Destination = destination;
        Amount = amount;
        Percent = percent;
    }

    /// <summary>Gets the share of net pay sent there (null: the fixed amount).</summary>
    public decimal? Percent { get; private set; }

    /// <summary>What this split takes from a net pay: its percentage of it, or its fixed amount.</summary>
    /// <param name="netPay">The net pay.</param>
    /// <returns>The amount (before what is left limits it).</returns>
    public decimal AmountFrom(decimal netPay) => Percent is decimal percent ? decimal.Round(netPay * percent / 100m, 2, MidpointRounding.AwayFromZero) : Amount;

    /// <summary>Why a split cannot be set: neither or both of an amount and a percentage, or the employee's percentages above 100.</summary>
    /// <param name="amount">The fixed amount.</param>
    /// <param name="percent">The percentage.</param>
    /// <param name="otherPercents">The employee's other splits' percentages.</param>
    /// <returns>The problem, or null.</returns>
    public static string? Problem(decimal? amount, decimal? percent, decimal otherPercents)
        => (amount is null) == (percent is null) ? "Give either a fixed amount or a percentage of net pay."
            : amount is <= 0m ? "The amount must be above zero."
            : percent is <= 0m or > 100m ? "The percentage must be above 0 and at most 100."
            : percent is decimal p && otherPercents + p > 100m ? $"That takes the splits to {otherPercents + p:0.##}% of net pay." : null;

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets how this part is paid.</summary>
    public PayMethod Method { get; private set; }

    /// <summary>Gets the account or wallet.</summary>
    public string Destination { get; private set; }

    /// <summary>Gets the fixed amount.</summary>
    public decimal Amount { get; private set; }
}

/// <summary>A payslip printed or rendered (<c>hr.payslip_print</c>); the second and later prints of the same payslip are reprints.</summary>
public sealed class PayslipPrint : AuditableEntity, ICompanyScoped
{
    private PayslipPrint()
    {
        PeriodLabel = string.Empty;
    }

    /// <summary>Logs a print.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="payDate">The pay date.</param>
    public PayslipPrint(long companyId, long employeeId, string periodLabel, DateOnly payDate)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        PeriodLabel = periodLabel;
        PayDate = payDate;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the period.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>Gets the pay date.</summary>
    public DateOnly PayDate { get; private set; }
}
