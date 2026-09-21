using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>
/// A dishonoured cheque / reversed payment (brief §RCP-7, <c>rcp.dishonoured_payment</c>). When a cheque
/// bounces the original receipt is reversed (restoring the debt), a penalty may be raised, and the event is
/// recorded — the original receipt is never deleted — for the dishonour register (RC34).
/// </summary>
public sealed class DishonouredPayment : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DishonouredPayment()
    {
        ReceiptNumber = string.Empty;
        Reference = string.Empty;
        Drawer = string.Empty;
    }

    /// <summary>Creates a dishonour record.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="receiptId">The reversed receipt.</param>
    /// <param name="receiptNumber">The receipt number.</param>
    /// <param name="reference">The cheque/tender reference.</param>
    /// <param name="drawer">The drawer / payer description.</param>
    /// <param name="amount">The dishonoured amount.</param>
    /// <param name="penalty">The penalty raised (if any).</param>
    /// <param name="date">The dishonour date.</param>
    public DishonouredPayment(long companyId, long receiptId, string receiptNumber, string reference, string drawer, decimal amount, decimal penalty, DateOnly date)
    {
        CompanyId = companyId;
        ReceiptId = receiptId;
        ReceiptNumber = receiptNumber;
        Reference = reference;
        Drawer = drawer;
        Amount = amount;
        Penalty = penalty;
        Date = date;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The reversed receipt.</summary>
    public long ReceiptId { get; private set; }

    /// <summary>The receipt number.</summary>
    public string ReceiptNumber { get; private set; }

    /// <summary>The cheque/tender reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The drawer / payer description.</summary>
    public string Drawer { get; private set; }

    /// <summary>The dishonoured amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The penalty raised (if any).</summary>
    public decimal Penalty { get; private set; }

    /// <summary>The dishonour date.</summary>
    public DateOnly Date { get; private set; }
}
