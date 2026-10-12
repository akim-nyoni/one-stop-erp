using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The tenure an instalment agreement buys (brief §4.10.5).</summary>
public enum InstalmentTenure
{
    /// <summary>A sale on instalments.</summary>
    InstalmentSale = 1,

    /// <summary>Rent-to-buy.</summary>
    RentToBuy = 2,

    /// <summary>A lease with an option to purchase.</summary>
    LeaseWithOption = 3,
}

/// <summary>How interest is charged on an instalment sale (brief §4.10.5).</summary>
public enum InterestMethod
{
    /// <summary>No interest: the price is simply spread over the term.</summary>
    Zero = 0,

    /// <summary>Flat: interest on the original amount financed for the whole term, spread evenly.</summary>
    Flat = 1,

    /// <summary>Reducing balance: an amortising annuity, interest on the balance still owed each month.</summary>
    ReducingBalance = 2,
}

/// <summary>Where an instalment agreement stands.</summary>
public enum InstalmentAgreementStatus
{
    /// <summary>Being paid off.</summary>
    Active = 0,

    /// <summary>Settled early; later interest rebated.</summary>
    SettledEarly = 1,

    /// <summary>Every instalment has fallen due.</summary>
    Completed = 2,

    /// <summary>The stand was repossessed for default.</summary>
    Repossessed = 3,
}

/// <summary>
/// A sale of a stand on instalments (<c>hse.instalment_agreement</c>, brief §4.10.5 and the §4.10.9 ruling): the sale is
/// recognised at the cash price when control passes, the full receivable raised and the deposit receipted; the
/// instalment plan is a financing component whose interest is raised as income as each instalment falls due.
/// </summary>
public sealed class InstalmentAgreement : AuditableEntity, ICompanyScoped
{
    private readonly List<InstalmentScheduleLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InstalmentAgreement()
    {
        AgreementNumber = string.Empty;
        InterestAccountCode = string.Empty;
    }

    /// <summary>Records an agreement.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement number.</param>
    /// <param name="standId">The stand sold.</param>
    /// <param name="buyerCustomerId">The buyer.</param>
    /// <param name="cashPrice">The cash price.</param>
    /// <param name="deposit">The deposit paid on signature.</param>
    /// <param name="annualRatePercent">The annual interest rate.</param>
    /// <param name="method">How interest is charged.</param>
    /// <param name="termMonths">The term in months.</param>
    /// <param name="signedOn">When it was signed (control passes).</param>
    /// <param name="interestAccountCode">The interest income account.</param>
    public InstalmentAgreement(long companyId, string agreementNumber, long standId, long buyerCustomerId, decimal cashPrice, decimal deposit,
        decimal annualRatePercent, InterestMethod method, int termMonths, DateOnly signedOn, string interestAccountCode)
    {
        CompanyId = companyId;
        AgreementNumber = agreementNumber;
        StandId = standId;
        BuyerCustomerId = buyerCustomerId;
        CashPrice = cashPrice;
        Deposit = deposit;
        AnnualRatePercent = annualRatePercent;
        Method = method;
        TermMonths = termMonths;
        SignedOn = signedOn;
        InterestAccountCode = interestAccountCode;
        Status = InstalmentAgreementStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The agreement number.</summary>
    public string AgreementNumber { get; private set; }

    /// <summary>The stand sold.</summary>
    public long StandId { get; private set; }

    /// <summary>The buyer.</summary>
    public long BuyerCustomerId { get; private set; }

    /// <summary>The cash price.</summary>
    public decimal CashPrice { get; private set; }

    /// <summary>The deposit paid on signature.</summary>
    public decimal Deposit { get; private set; }

    /// <summary>The amount financed.</summary>
    public decimal Financed => CashPrice - Deposit;

    /// <summary>The annual interest rate.</summary>
    public decimal AnnualRatePercent { get; private set; }

    /// <summary>How interest is charged.</summary>
    public InterestMethod Method { get; private set; }

    /// <summary>The term in months.</summary>
    public int TermMonths { get; private set; }

    /// <summary>When it was signed.</summary>
    public DateOnly SignedOn { get; private set; }

    /// <summary>The interest income account.</summary>
    public string InterestAccountCode { get; private set; }

    /// <summary>Gets the discount for the financing component held back from sale revenue (0: none).</summary>
    public decimal FinancingDiscount { get; private set; }

    /// <summary>Gets the account the unearned financing discount is held in.</summary>
    public string? DeferredFinancingAccountCode { get; private set; }

    /// <summary>Records the financing component's discount.</summary>
    /// <param name="discount">The discount.</param>
    /// <param name="accountCode">The account it is deferred in.</param>
    public void DeferFinancing(decimal discount, string accountCode)
    {
        FinancingDiscount = discount;
        DeferredFinancingAccountCode = accountCode;
    }

    /// <summary>The tenure the instalments buy (brief §4.10.5): an instalment sale, rent-to-buy or a lease with an option to purchase.</summary>
    public InstalmentTenure Tenure { get; set; } = InstalmentTenure.InstalmentSale;

    /// <summary>The annual penalty interest rate charged on arrears (%), or 0 for none (brief §4.10.5 "penalty interest on arrears").</summary>
    public decimal PenaltyRatePercent { get; set; }

    /// <summary>Gets or sets the yearly step-up of the instalment (%), or 0 for level instalments.</summary>
    public decimal EscalationPercent { get; set; }

    /// <summary>Where it stands.</summary>
    public InstalmentAgreementStatus Status { get; private set; }

    /// <summary>When it was settled early.</summary>
    public DateOnly? SettledOn { get; private set; }

    /// <summary>The schedule.</summary>
    public IReadOnlyList<InstalmentScheduleLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a scheduled instalment.</summary>
    /// <param name="line">The instalment.</param>
    public void AddLine(InstalmentScheduleLine line) => _lines.Add(line);

    /// <summary>Marks every instalment raised: the agreement has run its term.</summary>
    public void Complete() => Status = InstalmentAgreementStatus.Completed;

    /// <summary>Closes it on repossession: instalments not yet raised are never charged.</summary>
    /// <param name="date">When.</param>
    public void Repossess(DateOnly date)
    {
        Status = InstalmentAgreementStatus.Repossessed;
        SettledOn = date;
        foreach (InstalmentScheduleLine line in _lines.Where(l => !l.Raised))
        {
            line.Rebate();
        }
    }

    /// <summary>Settles it early, rebating the interest of instalments not yet raised.</summary>
    /// <param name="date">When.</param>
    public void SettleEarly(DateOnly date)
    {
        Status = InstalmentAgreementStatus.SettledEarly;
        SettledOn = date;
        foreach (InstalmentScheduleLine line in _lines.Where(l => !l.Raised))
        {
            line.Rebate();
        }
    }
}

/// <summary>A scheduled instalment (<c>hse.instalment_schedule_line</c>).</summary>
public sealed class InstalmentScheduleLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InstalmentScheduleLine()
    {
    }

    /// <summary>Schedules an instalment.</summary>
    /// <param name="number">The instalment number.</param>
    /// <param name="dueDate">When it falls due.</param>
    /// <param name="amount">The instalment.</param>
    /// <param name="interest">The interest part.</param>
    /// <param name="principal">The principal part.</param>
    /// <param name="balanceAfter">The principal owed after it.</param>
    public InstalmentScheduleLine(int number, DateOnly dueDate, decimal amount, decimal interest, decimal principal, decimal balanceAfter)
    {
        Number = number;
        DueDate = dueDate;
        Amount = amount;
        Interest = interest;
        Principal = principal;
        BalanceAfter = balanceAfter;
    }

    /// <summary>The agreement.</summary>
    public long InstalmentAgreementId { get; private set; }

    /// <summary>The instalment number.</summary>
    public int Number { get; private set; }

    /// <summary>When it falls due.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>The instalment.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The interest part.</summary>
    public decimal Interest { get; private set; }

    /// <summary>The principal part.</summary>
    public decimal Principal { get; private set; }

    /// <summary>The principal owed after it.</summary>
    public decimal BalanceAfter { get; private set; }

    /// <summary>Whether it has fallen due and its interest been raised.</summary>
    public bool Raised { get; private set; }

    /// <summary>Whether its interest was rebated on early settlement.</summary>
    public bool Rebated { get; private set; }

    /// <summary>Marks it raised.</summary>
    public void MarkRaised() => Raised = true;

    /// <summary>Marks its interest rebated.</summary>
    public void Rebate() => Rebated = true;

    /// <summary>Gets or sets the financing discount earned when this instalment falls due.</summary>
    public decimal FinancingUnwind { get; set; }

    /// <summary>Gets whether its financing discount has been earned.</summary>
    public bool FinancingEarned { get; private set; }

    /// <summary>Marks its financing discount earned.</summary>
    public void EarnFinancing() => FinancingEarned = true;
}

/// <summary>The stages of default on an instalment agreement (brief §4.10.5 "arrears stages → reminder → demand letter → notice of breach → notice of cancellation").</summary>
public enum DefaultStage
{
    /// <summary>A friendly reminder.</summary>
    Reminder = 1,

    /// <summary>A letter of demand.</summary>
    Demand = 2,

    /// <summary>A notice of breach.</summary>
    Breach = 3,

    /// <summary>A notice of cancellation (the cancellation itself follows, with its refund computation).</summary>
    Cancellation = 4,
}

/// <summary>A dated default notice on an instalment agreement, with proof of service (<c>hse.instalment_notice</c>).</summary>
public sealed class InstalmentNotice : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InstalmentNotice()
    {
        NoticeNumber = string.Empty;
        Text = string.Empty;
    }

    /// <summary>Issues a notice.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementId">The agreement.</param>
    /// <param name="noticeNumber">The notice number.</param>
    /// <param name="stage">The stage.</param>
    /// <param name="issuedOn">When it was issued.</param>
    /// <param name="arrears">The arrears it demands.</param>
    /// <param name="daysOverdue">How long the oldest unpaid instalment had been due.</param>
    /// <param name="text">The notice as issued.</param>
    public InstalmentNotice(long companyId, long agreementId, string noticeNumber, DefaultStage stage, DateOnly issuedOn, decimal arrears, int daysOverdue, string text)
    {
        CompanyId = companyId;
        AgreementId = agreementId;
        NoticeNumber = noticeNumber;
        Stage = stage;
        IssuedOn = issuedOn;
        Arrears = arrears;
        DaysOverdue = daysOverdue;
        Text = text;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The agreement.</summary>
    public long AgreementId { get; private set; }

    /// <summary>The notice number.</summary>
    public string NoticeNumber { get; private set; }

    /// <summary>The stage.</summary>
    public DefaultStage Stage { get; private set; }

    /// <summary>When it was issued.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>The arrears it demands.</summary>
    public decimal Arrears { get; private set; }

    /// <summary>How long the oldest unpaid instalment had been due.</summary>
    public int DaysOverdue { get; private set; }

    /// <summary>The notice as issued.</summary>
    public string Text { get; private set; }

    /// <summary>When it was served, once it has been.</summary>
    public DateOnly? ServedOn { get; private set; }

    /// <summary>How it was served (hand, registered post, email…).</summary>
    public string? ServiceMethod { get; private set; }

    /// <summary>Who served it.</summary>
    public string? ServedBy { get; private set; }

    /// <summary>Records proof of service.</summary>
    /// <param name="servedOn">When.</param>
    /// <param name="method">How.</param>
    /// <param name="servedBy">Who.</param>
    public void Serve(DateOnly servedOn, string method, string servedBy)
    {
        ServedOn = servedOn;
        ServiceMethod = method;
        ServedBy = servedBy;
    }
}
