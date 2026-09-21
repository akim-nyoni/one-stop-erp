using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>The state of a payment arrangement (brief §RCP-16 RC42).</summary>
public enum ArrangementStatus
{
    /// <summary>Active — instalments being paid.</summary>
    Active = 1,

    /// <summary>Honoured — fully paid.</summary>
    Honoured = 2,

    /// <summary>Defaulted — instalments missed.</summary>
    Defaulted = 3,
}

/// <summary>
/// A payment arrangement captured at the counter (brief §RCP-16 RC42, <c>rcp.payment_arrangement</c>): a
/// promise to clear a balance in instalments. Not a GL posting — the receipts against it settle the debt;
/// the arrangement tracks the promise and its status for credit control.
/// </summary>
public sealed class PaymentArrangement : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PaymentArrangement()
    {
    }

    /// <summary>Creates an arrangement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The payer account.</param>
    /// <param name="totalAmount">The balance to clear.</param>
    /// <param name="instalmentAmount">The instalment amount.</param>
    /// <param name="startDate">The start date.</param>
    public PaymentArrangement(long companyId, long customerId, decimal totalAmount, decimal instalmentAmount, DateOnly startDate)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        TotalAmount = totalAmount;
        InstalmentAmount = instalmentAmount;
        StartDate = startDate;
        Status = ArrangementStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The payer account.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The balance to clear.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>The instalment amount.</summary>
    public decimal InstalmentAmount { get; private set; }

    /// <summary>The start date.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>The status.</summary>
    public ArrangementStatus Status { get; private set; }

    /// <summary>Marks the arrangement honoured (fully paid).</summary>
    public void MarkHonoured() => Status = ArrangementStatus.Honoured;

    /// <summary>Marks the arrangement defaulted.</summary>
    public void MarkDefaulted() => Status = ArrangementStatus.Defaulted;
}
