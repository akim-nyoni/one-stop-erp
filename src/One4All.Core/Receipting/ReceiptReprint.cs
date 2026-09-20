using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>
/// A logged reprint of a receipt (brief §RCP-15, <c>rcp.receipt_reprint</c>). Reprints are permitted but
/// watermarked COPY, numbered and logged; this is the log the reprint report (RC16) reads.
/// </summary>
public sealed class ReceiptReprint : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReceiptReprint() => ReceiptNumber = string.Empty;

    /// <summary>Creates a reprint log entry.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="receiptId">The receipt.</param>
    /// <param name="receiptNumber">The receipt number.</param>
    /// <param name="copyNumber">The copy number (1-based, increments per receipt).</param>
    /// <param name="printedOn">The reprint date.</param>
    /// <param name="printedByUserId">The user who reprinted.</param>
    public ReceiptReprint(long companyId, long receiptId, string receiptNumber, int copyNumber, DateOnly printedOn, long printedByUserId)
    {
        CompanyId = companyId;
        ReceiptId = receiptId;
        ReceiptNumber = receiptNumber;
        CopyNumber = copyNumber;
        PrintedOn = printedOn;
        PrintedByUserId = printedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The receipt.</summary>
    public long ReceiptId { get; private set; }

    /// <summary>The receipt number.</summary>
    public string ReceiptNumber { get; private set; }

    /// <summary>The copy number (1-based).</summary>
    public int CopyNumber { get; private set; }

    /// <summary>The reprint date.</summary>
    public DateOnly PrintedOn { get; private set; }

    /// <summary>The user who reprinted.</summary>
    public long PrintedByUserId { get; private set; }
}
