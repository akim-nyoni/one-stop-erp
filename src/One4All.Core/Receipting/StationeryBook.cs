using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>The status of a pre-numbered stationery book (brief §RCP-11).</summary>
public enum StationeryBookStatus
{
    /// <summary>Issued to a named cashier, in use.</summary>
    Issued = 1,

    /// <summary>Returned — accounted for.</summary>
    Returned = 2,
}

/// <summary>
/// A pre-numbered manual receipt book (brief §RCP-11, <c>rcp.stationery_book</c>). Because the power goes
/// out and someone writes a manual receipt, books are registered by serial range, issued to a named
/// cashier, and every manual receipt must be captured with its original serial — so gaps and unreturned
/// books are provable. Real auditors test this.
/// </summary>
public sealed class StationeryBook : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<ManualReceiptCapture> _captures = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StationeryBook() => BookNumber = string.Empty;

    /// <summary>Issues a book.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bookNumber">The book number.</param>
    /// <param name="fromSerial">The first serial (inclusive).</param>
    /// <param name="toSerial">The last serial (inclusive).</param>
    /// <param name="issuedToUserId">The cashier the book is issued to.</param>
    /// <param name="issuedOn">The issue date.</param>
    public StationeryBook(long companyId, string bookNumber, long fromSerial, long toSerial, long issuedToUserId, DateOnly issuedOn)
    {
        if (toSerial < fromSerial)
        {
            throw new ArgumentException("The last serial cannot precede the first.", nameof(toSerial));
        }

        CompanyId = companyId;
        BookNumber = bookNumber.Trim().ToUpperInvariant();
        FromSerial = fromSerial;
        ToSerial = toSerial;
        IssuedToUserId = issuedToUserId;
        IssuedOn = issuedOn;
        Status = StationeryBookStatus.Issued;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The book number.</summary>
    public string BookNumber { get; private set; }

    /// <summary>The first serial (inclusive).</summary>
    public long FromSerial { get; private set; }

    /// <summary>The last serial (inclusive).</summary>
    public long ToSerial { get; private set; }

    /// <summary>The cashier the book is issued to.</summary>
    public long IssuedToUserId { get; private set; }

    /// <summary>The issue date.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>The status.</summary>
    public StationeryBookStatus Status { get; private set; }

    /// <summary>The captured manual receipts.</summary>
    public IReadOnlyList<ManualReceiptCapture> Captures => _captures.AsReadOnly();

    /// <summary>The count of serials in the book.</summary>
    public long SerialCount => ToSerial - FromSerial + 1;

    /// <summary>The serials in the book not yet captured (unaccounted-for serials).</summary>
    public IReadOnlyList<long> UnusedSerials()
    {
        HashSet<long> used = _captures.Select(c => c.Serial).ToHashSet();
        List<long> unused = [];
        for (long serial = FromSerial; serial <= ToSerial; serial++)
        {
            if (!used.Contains(serial))
            {
                unused.Add(serial);
            }
        }

        return unused;
    }

    /// <summary>Captures a manual receipt written from this book into the system.</summary>
    /// <param name="serial">The original serial from the paper receipt.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="receiptDate">The original receipt date.</param>
    /// <returns>The created capture.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the serial is out of range or already captured.</exception>
    public ManualReceiptCapture Capture(long serial, decimal amount, DateOnly receiptDate)
    {
        if (serial < FromSerial || serial > ToSerial)
        {
            throw new InvalidOperationException($"Serial {serial} is outside book {BookNumber} ({FromSerial}-{ToSerial}).");
        }

        if (_captures.Any(c => c.Serial == serial))
        {
            throw new InvalidOperationException($"Serial {serial} has already been captured for book {BookNumber}.");
        }

        ManualReceiptCapture capture = new(serial, amount, receiptDate);
        _captures.Add(capture);
        return capture;
    }

    /// <summary>Marks the book returned (all serials accounted for or explained).</summary>
    public void Return() => Status = StationeryBookStatus.Returned;
}

/// <summary>A manual receipt captured into the system (<c>rcp.manual_receipt_capture</c>).</summary>
public sealed class ManualReceiptCapture : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ManualReceiptCapture()
    {
    }

    /// <summary>Creates a capture.</summary>
    /// <param name="serial">The original serial.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="receiptDate">The original receipt date.</param>
    public ManualReceiptCapture(long serial, decimal amount, DateOnly receiptDate)
    {
        Serial = serial;
        Amount = amount;
        ReceiptDate = receiptDate;
        CapturedOn = DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>The owning book id.</summary>
    public long StationeryBookId { get; private set; }

    /// <summary>The original serial.</summary>
    public long Serial { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The original receipt date.</summary>
    public DateOnly ReceiptDate { get; private set; }

    /// <summary>The date the manual receipt was captured into the system.</summary>
    public DateOnly CapturedOn { get; private set; }
}
