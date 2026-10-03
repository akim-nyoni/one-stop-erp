using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Where a customer's promise to pay stands.</summary>
public enum PromiseStatus
{
    /// <summary>Made and not yet due or settled.</summary>
    Open = 0,

    /// <summary>The customer paid the amount by the date.</summary>
    Kept = 1,

    /// <summary>The date passed without the amount being paid.</summary>
    Broken = 2,
}

/// <summary>
/// A promise to pay (<c>ar.payment_promise</c>, brief §4.5 credit control workbench): a customer's commitment, recorded
/// by the credit controller, to pay an amount by a date. While open it holds the customer out of dunning; a review marks
/// it kept once receipts since it was made cover the amount, or broken once the date passes without them.
/// </summary>
public sealed class PaymentPromise : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PaymentPromise()
    {
    }

    /// <summary>Records a promise.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="madeOn">When the promise was made.</param>
    /// <param name="payBy">The date the customer promised to pay by.</param>
    /// <param name="amount">The amount promised.</param>
    /// <param name="note">The controller's note, if any.</param>
    /// <param name="recordedByUserId">Who recorded it.</param>
    public PaymentPromise(long companyId, long customerId, DateOnly madeOn, DateOnly payBy, decimal amount, string? note, long recordedByUserId)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        MadeOn = madeOn;
        PayBy = payBy;
        Amount = amount;
        Note = note;
        RecordedByUserId = recordedByUserId;
        Status = PromiseStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>When the promise was made.</summary>
    public DateOnly MadeOn { get; private set; }

    /// <summary>The date promised.</summary>
    public DateOnly PayBy { get; private set; }

    /// <summary>The amount promised (base currency).</summary>
    public decimal Amount { get; private set; }

    /// <summary>The controller's note.</summary>
    public string? Note { get; private set; }

    /// <summary>Who recorded it.</summary>
    public long RecordedByUserId { get; private set; }

    /// <summary>Where it stands.</summary>
    public PromiseStatus Status { get; private set; }

    /// <summary>When it was found kept or broken.</summary>
    public DateOnly? ResolvedOn { get; private set; }

    /// <summary>Settles the promise against what has been received since it was made.</summary>
    /// <param name="receivedSinceMade">Receipts from the customer dated from the promise to the review date (no later than the promised date).</param>
    /// <param name="asAt">The review date.</param>
    /// <returns>Whether the promise was resolved by this review.</returns>
    public bool Review(decimal receivedSinceMade, DateOnly asAt)
    {
        if (Status != PromiseStatus.Open)
        {
            return false;
        }

        if (receivedSinceMade >= Amount)
        {
            Status = PromiseStatus.Kept;
        }
        else if (asAt > PayBy)
        {
            Status = PromiseStatus.Broken;
        }
        else
        {
            return false;
        }

        ResolvedOn = asAt;
        return true;
    }
}
