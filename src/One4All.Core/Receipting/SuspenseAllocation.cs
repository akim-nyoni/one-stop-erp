using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>
/// A logged allocation of money out of the suspense control to a payer account (brief §RCP-16 RC30,
/// <c>rcp.suspense_allocation</c>). Allocating from suspense is a permissioned, audited act with a reason -
/// because it is also a fraud route - so it is recorded for the suspense-allocation report.
/// </summary>
public sealed class SuspenseAllocation : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SuspenseAllocation() => Reason = string.Empty;

    /// <summary>Creates a suspense-allocation log entry.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The account the money was allocated to.</param>
    /// <param name="amount">The amount moved out of suspense.</param>
    /// <param name="date">The allocation date.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="journalNumber">The GL journal number.</param>
    public SuspenseAllocation(long companyId, long customerId, decimal amount, DateOnly date, string reason, string journalNumber)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        Amount = amount;
        Date = date;
        Reason = reason;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The account the money was allocated to.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The amount moved out of suspense.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The allocation date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The reason.</summary>
    public string Reason { get; private set; }

    /// <summary>The GL journal number.</summary>
    public string JournalNumber { get; private set; } = string.Empty;
}
