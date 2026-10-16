using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>The lifecycle status of a journal (brief §3.2).</summary>
public enum JournalStatus
{
    /// <summary>Captured but not yet posted.</summary>
    Draft = 0,

    /// <summary>Queued for controlled batch posting (deferred posting).</summary>
    Deferred = 1,

    /// <summary>Posted to the ledger.</summary>
    Posted = 2,

    /// <summary>Reversed by a later journal.</summary>
    Reversed = 3,

    /// <summary>Rejected while awaiting approval: never posted, its number kept so the sequence has no gap.</summary>
    Cancelled = 4,
}

/// <summary>
/// A single balanced double-entry transaction in the nominal ledger (<c>gl.journal</c>). Every posting
/// path in the system produces one of these through the central posting engine — the engine guarantees
/// Dr = Cr in base currency (and per transaction currency), an open period, valid accounts, a gapless
/// number and an audit trail. A posted journal is never edited or deleted; corrections are reversals.
/// </summary>
public sealed class JournalEntry : AuditableEntity, ICompanyScoped
{
    private readonly List<JournalLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private JournalEntry()
    {
        JournalNumber = string.Empty;
        SourceModule = string.Empty;
    }

    /// <summary>Creates a draft journal (the engine populates numbers, period and status on posting).</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="sourceModule">The module raising the posting (e.g. GL, AR, POS).</param>
    /// <param name="postingDate">The accounting date.</param>
    /// <param name="baseCurrency">The company base currency at posting time.</param>
    /// <param name="transactionCurrency">The document's transaction currency.</param>
    public JournalEntry(long companyId, string sourceModule, DateOnly postingDate, CurrencyCode baseCurrency, CurrencyCode transactionCurrency)
    {
        CompanyId = companyId;
        SourceModule = sourceModule;
        PostingDate = postingDate;
        BaseCurrency = baseCurrency;
        TransactionCurrency = transactionCurrency;
        JournalNumber = string.Empty;
        Status = JournalStatus.Draft;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The allocated journal number (gapless), set on posting.</summary>
    public string JournalNumber { get; private set; }

    /// <summary>The batch number if posted as part of a batch.</summary>
    public string? BatchNumber { get; private set; }

    /// <summary>The module that raised the posting.</summary>
    public string SourceModule { get; private set; }

    /// <summary>The source document type (e.g. "Journal", "SalesInvoice").</summary>
    public string? DocumentType { get; set; }

    /// <summary>The source document number.</summary>
    public string? DocumentNumber { get; set; }

    /// <summary>Gets or sets the terminal or network address the journal was entered from.</summary>
    public string? Terminal { get; set; }

    /// <summary>A user reference.</summary>
    public string? Reference { get; set; }

    /// <summary>A narrative/description.</summary>
    public string? Narrative { get; set; }

    /// <summary>The idempotency key it was posted with: the same key posts nothing new (brief §2.4).</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Whether this is a manual (user-keyed) journal. Manual journals may not touch control accounts — checked by the
    /// posting engine and, independently, by a database trigger on the journal lines (brief §3.1).
    /// </summary>
    public bool IsManual { get; set; }

    /// <summary>The accounting date.</summary>
    public DateOnly PostingDate { get; private set; }

    /// <summary>Who prepared the journal, when it was submitted for a second user's approval (maker-checker).</summary>
    public long? PreparedByUserId { get; private set; }

    /// <summary>Who approved the journal for posting; never the preparer.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>Whether the journal is held, numbered but unposted, until a second user approves it.</summary>
    public bool AwaitingApproval { get; private set; }

    /// <summary>Why the approver rejected the journal.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>The company base currency at posting time.</summary>
    public CurrencyCode BaseCurrency { get; private set; }

    /// <summary>The document transaction currency.</summary>
    public CurrencyCode TransactionCurrency { get; private set; }

    /// <summary>The fiscal year the posting date falls in (set on posting).</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>The fiscal period the posting date falls in (set on posting).</summary>
    public long FiscalPeriodId { get; private set; }

    /// <summary>The journal status.</summary>
    public JournalStatus Status { get; private set; }

    /// <summary>When true, this journal is itself a reversing entry.</summary>
    public bool IsReversal { get; private set; }

    /// <summary>The id of the journal this one reverses, if any.</summary>
    public long? ReversalOfJournalId { get; private set; }

    /// <summary>The posting lines.</summary>
    public IReadOnlyList<JournalLine> Lines => _lines.AsReadOnly();

    /// <summary>Total of base-currency debits across all lines.</summary>
    public decimal TotalBaseDebit => _lines.Sum(l => l.BaseDebit);

    /// <summary>Total of base-currency credits across all lines.</summary>
    public decimal TotalBaseCredit => _lines.Sum(l => l.BaseCredit);

    /// <summary>True when base debits equal base credits (the fundamental double-entry invariant).</summary>
    public bool IsBalancedInBase => TotalBaseDebit == TotalBaseCredit;

    /// <summary>Adds a posting line.</summary>
    /// <param name="line">The line to add.</param>
    public void AddLine(JournalLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        line.Stamp(CompanyId, PostingDate);
        _lines.Add(line);
    }

    /// <summary>Marks the journal posted, stamping its number, period and status. Called only by the posting engine.</summary>
    /// <param name="journalNumber">The gapless journal number.</param>
    /// <param name="fiscalYearId">The fiscal year id.</param>
    /// <param name="fiscalPeriodId">The fiscal period id.</param>
    /// <param name="entryDateUtc">The system entry timestamp.</param>
    /// <param name="batchNumber">Optional batch number.</param>
    public void MarkPosted(string journalNumber, long fiscalYearId, long fiscalPeriodId, DateTimeOffset entryDateUtc, string? batchNumber = null)
    {
        JournalNumber = journalNumber;
        FiscalYearId = fiscalYearId;
        FiscalPeriodId = fiscalPeriodId;
        CreatedOnUtc = entryDateUtc;
        BatchNumber = batchNumber;
        Status = JournalStatus.Posted;
    }

    /// <summary>Marks the journal as deferred (queued for a later controlled batch post), stamping its number and period.</summary>
    /// <param name="journalNumber">The gapless journal number.</param>
    /// <param name="fiscalYearId">The fiscal year id.</param>
    /// <param name="fiscalPeriodId">The fiscal period id.</param>
    /// <param name="entryDateUtc">The system entry timestamp.</param>
    public void MarkDeferred(string journalNumber, long fiscalYearId, long fiscalPeriodId, DateTimeOffset entryDateUtc)
    {
        JournalNumber = journalNumber;
        FiscalYearId = fiscalYearId;
        FiscalPeriodId = fiscalPeriodId;
        CreatedOnUtc = entryDateUtc;
        Status = JournalStatus.Deferred;
    }

    /// <summary>Holds this deferred journal for a second user's approval. Called only by the posting engine.</summary>
    /// <param name="preparedByUserId">The preparer.</param>
    public void HoldForApproval(long preparedByUserId)
    {
        PreparedByUserId = preparedByUserId;
        AwaitingApproval = true;
    }

    /// <summary>Records the approval, freeing the journal to be released. Refuses self-approval.</summary>
    /// <param name="approverUserId">The approver.</param>
    /// <returns>Whether the approval was recorded.</returns>
    public bool Approve(long approverUserId)
    {
        if (!AwaitingApproval || approverUserId == PreparedByUserId)
        {
            return false;
        }

        ApprovedByUserId = approverUserId;
        AwaitingApproval = false;
        return true;
    }

    /// <summary>Rejects the journal awaiting approval; it is cancelled and never posts.</summary>
    /// <param name="reason">Why.</param>
    public void Reject(string reason)
    {
        AwaitingApproval = false;
        RejectionReason = reason;
        Status = JournalStatus.Cancelled;
    }

    /// <summary>Marks this journal as a reversal of another.</summary>
    /// <param name="originalJournalId">The id of the journal being reversed.</param>
    public void MarkAsReversalOf(long originalJournalId)
    {
        IsReversal = true;
        ReversalOfJournalId = originalJournalId;
    }

    /// <summary>Marks this (original) journal as having been reversed.</summary>
    public void MarkReversed() => Status = JournalStatus.Reversed;
}
