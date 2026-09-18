using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>The register a payer account belongs to (brief §RCP-2 payer abstraction).</summary>
public enum PayerKind
{
    /// <summary>An AR trade-debtor customer.</summary>
    ArCustomer = 1,

    /// <summary>A municipal consumer account.</summary>
    Municipal = 2,

    /// <summary>A housing beneficiary or tenant.</summary>
    Housing = 3,

    /// <summary>A student / guardian / sponsor.</summary>
    Student = 4,

    /// <summary>No account — a sundry / anonymous receipt.</summary>
    Anonymous = 99,
}

/// <summary>What a receipt line's money is for (brief §RCP-14 posting patterns).</summary>
public enum ReceiptLineType
{
    /// <summary>Payment against a billed debtor account → Cr debtor control (never income).</summary>
    DebtorPayment = 1,

    /// <summary>Sundry income never previously billed → Cr the income vote.</summary>
    Sundry = 2,

    /// <summary>A deposit received → Cr deposit liability (never income).</summary>
    Deposit = 3,

    /// <summary>Unidentified money → Cr suspense control (never income, never a guess).</summary>
    Suspense = 4,
}

/// <summary>How a receipt was tendered (brief §RCP-7).</summary>
public enum TenderType
{
    /// <summary>Cash.</summary>
    Cash = 1,

    /// <summary>Card.</summary>
    Card = 2,

    /// <summary>Mobile money / wallet.</summary>
    MobileMoney = 3,

    /// <summary>Bank transfer / direct deposit.</summary>
    BankTransfer = 4,

    /// <summary>Cheque.</summary>
    Cheque = 5,
}

/// <summary>The status of a receipt.</summary>
public enum ReceiptStatus
{
    /// <summary>Committed (posted).</summary>
    Committed = 1,

    /// <summary>Cancelled (retained, never deleted).</summary>
    Cancelled = 2,
}

/// <summary>
/// An official receipt (brief §RCP-2, <c>rcp.receipt</c>) — an immutable document collecting money against
/// an account or as sundry income. One receipt has one gapless number, one or more lines (what the money is
/// for) and one or more tenders (how it was paid).
/// </summary>
public sealed class Receipt : AuditableEntity, ICompanyScoped
{
    private readonly List<ReceiptLine> _lines = [];
    private readonly List<ReceiptTender> _tenders = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Receipt() => ReceiptNumber = string.Empty;

    /// <summary>Creates a receipt.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sessionId">The cashier session.</param>
    /// <param name="stationId">The station.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="receiptNumber">The gapless receipt number.</param>
    /// <param name="date">The receipt date.</param>
    /// <param name="payerKind">The payer register.</param>
    /// <param name="payerAccountId">The payer account id (null for anonymous/sundry).</param>
    /// <param name="currency">The receipt currency.</param>
    public Receipt(long companyId, long sessionId, long stationId, long cashierUserId, string receiptNumber, DateOnly date, PayerKind payerKind, long? payerAccountId, CurrencyCode currency)
    {
        CompanyId = companyId;
        CashierSessionId = sessionId;
        StationId = stationId;
        CashierUserId = cashierUserId;
        ReceiptNumber = receiptNumber;
        Date = date;
        PayerKind = payerKind;
        PayerAccountId = payerAccountId;
        Currency = currency;
        Status = ReceiptStatus.Committed;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The cashier session.</summary>
    public long CashierSessionId { get; private set; }

    /// <summary>The station.</summary>
    public long StationId { get; private set; }

    /// <summary>The cashier.</summary>
    public long CashierUserId { get; private set; }

    /// <summary>The gapless receipt number.</summary>
    public string ReceiptNumber { get; private set; }

    /// <summary>The receipt date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The payer register.</summary>
    public PayerKind PayerKind { get; private set; }

    /// <summary>The payer account id (null for anonymous/sundry).</summary>
    public long? PayerAccountId { get; private set; }

    /// <summary>The receipt currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The status.</summary>
    public ReceiptStatus Status { get; private set; }

    /// <summary>The GL journal number.</summary>
    public string? JournalNumber { get; set; }

    /// <summary>The lines (what the money is for).</summary>
    public IReadOnlyList<ReceiptLine> Lines => _lines.AsReadOnly();

    /// <summary>The tenders (how it was paid).</summary>
    public IReadOnlyList<ReceiptTender> Tenders => _tenders.AsReadOnly();

    /// <summary>The total of all lines.</summary>
    public decimal LineTotal => _lines.Sum(l => l.Amount);

    /// <summary>The total of all tenders.</summary>
    public decimal TenderTotal => _tenders.Sum(t => t.Amount);

    /// <summary>Adds a line.</summary>
    /// <param name="type">The line type.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="accountCode">The income/deposit/suspense account (null for a debtor payment).</param>
    /// <param name="narrative">A narrative.</param>
    /// <returns>The created line.</returns>
    public ReceiptLine AddLine(ReceiptLineType type, decimal amount, string? accountCode, string? narrative)
    {
        ReceiptLine line = new(type, amount, accountCode, narrative);
        _lines.Add(line);
        return line;
    }

    /// <summary>Adds a tender.</summary>
    /// <param name="type">The tender type.</param>
    /// <param name="currency">The tender currency.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="reference">A reference.</param>
    /// <returns>The created tender.</returns>
    public ReceiptTender AddTender(TenderType type, CurrencyCode currency, decimal amount, string? reference)
    {
        ReceiptTender tender = new(type, currency, amount, reference);
        _tenders.Add(tender);
        return tender;
    }

    /// <summary>Cancels the receipt (retained, never deleted).</summary>
    public void Cancel() => Status = ReceiptStatus.Cancelled;
}

/// <summary>One line of a receipt — what the money is for (<c>rcp.receipt_line</c>).</summary>
public sealed class ReceiptLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReceiptLine()
    {
    }

    /// <summary>Creates a line.</summary>
    /// <param name="type">The line type.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="accountCode">The income/deposit/suspense account (null for a debtor payment).</param>
    /// <param name="narrative">A narrative.</param>
    public ReceiptLine(ReceiptLineType type, decimal amount, string? accountCode, string? narrative)
    {
        Type = type;
        Amount = amount;
        AccountCode = accountCode;
        Narrative = narrative;
    }

    /// <summary>The owning receipt id.</summary>
    public long ReceiptId { get; private set; }

    /// <summary>The line type.</summary>
    public ReceiptLineType Type { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The income/deposit/suspense account (null for a debtor payment).</summary>
    public string? AccountCode { get; private set; }

    /// <summary>A narrative.</summary>
    public string? Narrative { get; private set; }
}

/// <summary>One tender of a receipt — how it was paid (<c>rcp.receipt_tender</c>).</summary>
public sealed class ReceiptTender : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReceiptTender()
    {
    }

    /// <summary>Creates a tender.</summary>
    /// <param name="type">The tender type.</param>
    /// <param name="currency">The tender currency.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="reference">A reference.</param>
    public ReceiptTender(TenderType type, CurrencyCode currency, decimal amount, string? reference)
    {
        Type = type;
        Currency = currency;
        Amount = amount;
        Reference = reference;
    }

    /// <summary>The owning receipt id.</summary>
    public long ReceiptId { get; private set; }

    /// <summary>The tender type.</summary>
    public TenderType Type { get; private set; }

    /// <summary>The tender currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>A reference (card auth, mobile-money ref, cheque number …).</summary>
    public string? Reference { get; private set; }
}
