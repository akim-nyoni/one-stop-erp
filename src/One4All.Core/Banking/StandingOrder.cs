using One4All.Core.Common;

namespace One4All.Core.Banking;

/// <summary>How often a standing order or direct debit falls due.</summary>
public enum RecurrenceFrequency
{
    /// <summary>Every week from the start date.</summary>
    Weekly = 1,

    /// <summary>Every month on the start date's day (the month's last day when shorter).</summary>
    Monthly = 2,

    /// <summary>Every three months.</summary>
    Quarterly = 3,

    /// <summary>Every year.</summary>
    Annually = 4,
}

/// <summary>
/// A standing order (<c>cb.standing_order</c>): a fixed payment out of a bank account on a schedule, either to a supplier (through the
/// purchase ledger) or to a GL account (rent, loan repayment, transfer to savings).
/// </summary>
public sealed class StandingOrder : Entity, ICompanyScoped
{
    private StandingOrder()
    {
        Reference = string.Empty;
        BankAccountCode = string.Empty;
        Narrative = string.Empty;
    }

    /// <summary>Creates the standing order.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The reference (unique per company).</param>
    /// <param name="bankAccountCode">The bank paid from.</param>
    /// <param name="amount">The amount each time.</param>
    /// <param name="frequency">How often.</param>
    /// <param name="startDate">The first payment.</param>
    /// <param name="endDate">The last date it may pay (null: until cancelled).</param>
    /// <param name="supplierId">The supplier paid, or null.</param>
    /// <param name="debitAccountCode">The GL account debited when not a supplier.</param>
    /// <param name="narrative">What it is for.</param>
    public StandingOrder(long companyId, string reference, string bankAccountCode, decimal amount, RecurrenceFrequency frequency, DateOnly startDate, DateOnly? endDate, long? supplierId,
        string? debitAccountCode, string narrative)
    {
        CompanyId = companyId;
        Reference = reference;
        BankAccountCode = bankAccountCode;
        Amount = amount;
        Frequency = frequency;
        StartDate = startDate;
        EndDate = endDate;
        SupplierId = supplierId;
        DebitAccountCode = debitAccountCode;
        Narrative = narrative;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets the bank paid from.</summary>
    public string BankAccountCode { get; private set; }

    /// <summary>Gets the amount each time.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets how often.</summary>
    public RecurrenceFrequency Frequency { get; private set; }

    /// <summary>Gets the first payment date.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Gets the last date it may pay.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>Gets the supplier paid.</summary>
    public long? SupplierId { get; private set; }

    /// <summary>Gets the GL account debited.</summary>
    public string? DebitAccountCode { get; private set; }

    /// <summary>Gets what it is for.</summary>
    public string Narrative { get; private set; }

    /// <summary>Gets or sets the last occurrence paid.</summary>
    public DateOnly? LastRunOn { get; set; }

    /// <summary>Gets or sets a value indicating whether it still runs.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// A direct debit mandate (<c>cb.direct_debit_mandate</c>): the customer's authority to collect from their bank on a schedule, a fixed
/// amount or what they owe (capped), receipted to the sales ledger.
/// </summary>
public sealed class DirectDebitMandate : Entity, ICompanyScoped
{
    private DirectDebitMandate()
    {
        Reference = string.Empty;
        BankAccountCode = string.Empty;
        CustomerBankAccount = string.Empty;
    }

    /// <summary>Creates the mandate.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The mandate reference (unique per company).</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="customerBankAccount">The account collected from.</param>
    /// <param name="bankAccountCode">The bank collected into.</param>
    /// <param name="frequency">How often.</param>
    /// <param name="startDate">The first collection.</param>
    /// <param name="endDate">The last date it may collect.</param>
    /// <param name="fixedAmount">A fixed amount, or null to collect what is owed.</param>
    /// <param name="maximumAmount">The most one collection may take.</param>
    public DirectDebitMandate(long companyId, string reference, long customerId, string customerBankAccount, string bankAccountCode, RecurrenceFrequency frequency, DateOnly startDate,
        DateOnly? endDate, decimal? fixedAmount, decimal? maximumAmount)
    {
        CompanyId = companyId;
        Reference = reference;
        CustomerId = customerId;
        CustomerBankAccount = customerBankAccount;
        BankAccountCode = bankAccountCode;
        Frequency = frequency;
        StartDate = startDate;
        EndDate = endDate;
        FixedAmount = fixedAmount;
        MaximumAmount = maximumAmount;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the mandate reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets the customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>Gets the account collected from.</summary>
    public string CustomerBankAccount { get; private set; }

    /// <summary>Gets the bank collected into.</summary>
    public string BankAccountCode { get; private set; }

    /// <summary>Gets how often.</summary>
    public RecurrenceFrequency Frequency { get; private set; }

    /// <summary>Gets the first collection date.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Gets the last date it may collect.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>Gets the fixed amount, or null to collect what is owed.</summary>
    public decimal? FixedAmount { get; private set; }

    /// <summary>Gets the most one collection may take.</summary>
    public decimal? MaximumAmount { get; private set; }

    /// <summary>Gets or sets the last occurrence collected (or passed over with nothing owed).</summary>
    public DateOnly? LastRunOn { get; set; }

    /// <summary>Gets or sets a value indicating whether it is still in force.</summary>
    public bool IsActive { get; set; }
}
