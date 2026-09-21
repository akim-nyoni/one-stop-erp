using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>The state of a tip allocation (Restaurant module).</summary>
public enum TipAllocationStatus
{
    /// <summary>Owed to the staff member (still in the tips-payable pool).</summary>
    Owed = 1,

    /// <summary>Paid out.</summary>
    Paid = 2,
}

/// <summary>
/// A staff member's share of the pooled tips (<c>res.tip_allocation</c>). Tips accumulate in the
/// tips-payable liability at billing; distributing the pool assigns who within that liability is owed what
/// (no GL — the liability already exists). Paying an allocation clears its share to the bank.
/// </summary>
public sealed class TipAllocation : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TipAllocation() => TipsPayableAccountCode = string.Empty;

    /// <summary>Creates a tip allocation.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="staffUserId">The staff member.</param>
    /// <param name="amount">The amount allocated.</param>
    /// <param name="date">The allocation date.</param>
    /// <param name="tipsPayableAccountCode">The tips-payable account the pool sits in.</param>
    public TipAllocation(long companyId, long staffUserId, decimal amount, DateOnly date, string tipsPayableAccountCode)
    {
        CompanyId = companyId;
        StaffUserId = staffUserId;
        Amount = amount;
        Date = date;
        TipsPayableAccountCode = tipsPayableAccountCode;
        Status = TipAllocationStatus.Owed;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The staff member.</summary>
    public long StaffUserId { get; private set; }

    /// <summary>The amount allocated.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The allocation date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The tips-payable account the pool sits in.</summary>
    public string TipsPayableAccountCode { get; private set; }

    /// <summary>The status.</summary>
    public TipAllocationStatus Status { get; private set; }

    /// <summary>The date the allocation was paid, if any.</summary>
    public DateOnly? PaidOn { get; private set; }

    /// <summary>Marks the allocation paid.</summary>
    /// <param name="paidOn">The payout date.</param>
    public void MarkPaid(DateOnly paidOn)
    {
        Status = TipAllocationStatus.Paid;
        PaidOn = paidOn;
    }
}
