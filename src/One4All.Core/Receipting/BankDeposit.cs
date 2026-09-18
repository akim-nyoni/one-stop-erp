using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>The lifecycle of a banking deposit (brief §RCP-10).</summary>
public enum BankDepositStatus
{
    /// <summary>Cash handed to treasury / on the deposit slip, sitting in cash-in-transit.</summary>
    InTransit = 1,

    /// <summary>Confirmed on the bank statement — cash-in-transit cleared.</summary>
    Banked = 2,
}

/// <summary>
/// A banking deposit (brief §RCP-10, <c>rcp.bank_deposit</c>): takings moved from the till/safe into
/// cash-in-transit, then confirmed to the bank. Cash-in-transit is its own control account that must clear.
/// </summary>
public sealed class BankDeposit : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BankDeposit()
    {
        Reference = string.Empty;
    }

    /// <summary>Creates a deposit in transit.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The deposit-slip reference.</param>
    /// <param name="currency">The currency.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="depositDate">The deposit date.</param>
    public BankDeposit(long companyId, string reference, CurrencyCode currency, decimal amount, DateOnly depositDate)
    {
        CompanyId = companyId;
        Reference = reference;
        Currency = currency;
        Amount = amount;
        DepositDate = depositDate;
        Status = BankDepositStatus.InTransit;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The deposit-slip reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The deposit date.</summary>
    public DateOnly DepositDate { get; private set; }

    /// <summary>The status.</summary>
    public BankDepositStatus Status { get; private set; }

    /// <summary>The date the deposit was confirmed on the statement, if any.</summary>
    public DateOnly? BankedOn { get; private set; }

    /// <summary>Marks the deposit confirmed on the bank statement.</summary>
    /// <param name="bankedOn">The confirmation date.</param>
    public void MarkBanked(DateOnly bankedOn)
    {
        Status = BankDepositStatus.Banked;
        BankedOn = bankedOn;
    }
}
