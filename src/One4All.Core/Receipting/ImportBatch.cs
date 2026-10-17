using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>The source of an import batch (brief §RCP-8 import sources).</summary>
public enum ImportSource
{
    /// <summary>A bank statement (CSV/OFX/MT940/CAMT.053).</summary>
    BankStatement = 1,

    /// <summary>A mobile money / wallet settlement file.</summary>
    MobileMoney = 2,

    /// <summary>A card acquirer settlement file.</summary>
    CardSettlement = 3,

    /// <summary>An employer deduction schedule.</summary>
    EmployerSchedule = 4,

    /// <summary>An agent collection file.</summary>
    AgentCollection = 5,
}

/// <summary>How an import line was matched to an account (brief §RCP-8 auto-matching).</summary>
public enum ImportLineStatus
{
    /// <summary>Matched to exactly one account and receipted against it.</summary>
    Matched = 1,

    /// <summary>Not matched - parked in the suspense control for the workbench.</summary>
    Unmatched = 2,
}

/// <summary>
/// An import batch of bulk/file-based receipts (brief §RCP-8, <c>rcp.import_batch</c>). Money councils
/// receive without the payer visiting a counter - matched by the account in the payment reference, with
/// unmatched money parked in suspense. Duplicate files are detected by hash and rejected.
/// </summary>
public sealed class ImportBatch : AuditableEntity, ICompanyScoped
{
    private readonly List<ImportLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ImportBatch()
    {
        FileReference = string.Empty;
        FileHash = string.Empty;
        BankAccountCode = string.Empty;
    }

    /// <summary>Creates an import batch.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fileReference">The file name/reference.</param>
    /// <param name="fileHash">The file content hash (for duplicate detection).</param>
    /// <param name="source">The import source.</param>
    /// <param name="importDate">The import date.</param>
    /// <param name="bankAccountCode">The bank account the money arrived in.</param>
    public ImportBatch(long companyId, string fileReference, string fileHash, ImportSource source, DateOnly importDate, string bankAccountCode)
    {
        CompanyId = companyId;
        FileReference = fileReference;
        FileHash = fileHash;
        Source = source;
        ImportDate = importDate;
        BankAccountCode = bankAccountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The file name/reference.</summary>
    public string FileReference { get; private set; }

    /// <summary>The file content hash (for duplicate detection).</summary>
    public string FileHash { get; private set; }

    /// <summary>The import source.</summary>
    public ImportSource Source { get; private set; }

    /// <summary>The import date.</summary>
    public DateOnly ImportDate { get; private set; }

    /// <summary>The bank account the money arrived in.</summary>
    public string BankAccountCode { get; private set; }

    /// <summary>The import lines.</summary>
    public IReadOnlyList<ImportLine> Lines => _lines.AsReadOnly();

    /// <summary>The number of matched lines.</summary>
    public int MatchedCount => _lines.Count(l => l.Status == ImportLineStatus.Matched);

    /// <summary>The number of unmatched lines.</summary>
    public int UnmatchedCount => _lines.Count(l => l.Status == ImportLineStatus.Unmatched);

    /// <summary>The total amount imported.</summary>
    public decimal TotalAmount => _lines.Sum(l => l.Amount);

    /// <summary>Adds a line to the batch.</summary>
    /// <param name="payerReference">The payer reference from the file.</param>
    /// <param name="payerName">The payer name from the file.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="valueDate">The value date.</param>
    /// <returns>The created line.</returns>
    public ImportLine AddLine(string payerReference, string payerName, decimal amount, DateOnly valueDate)
    {
        ImportLine line = new(payerReference, payerName, amount, valueDate);
        _lines.Add(line);
        return line;
    }
}

/// <summary>One line of an import batch (<c>rcp.import_line</c>).</summary>
public sealed class ImportLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ImportLine()
    {
        PayerReference = string.Empty;
        PayerName = string.Empty;
    }

    /// <summary>Creates a line.</summary>
    /// <param name="payerReference">The payer reference from the file.</param>
    /// <param name="payerName">The payer name from the file.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="valueDate">The value date.</param>
    public ImportLine(string payerReference, string payerName, decimal amount, DateOnly valueDate)
    {
        PayerReference = payerReference;
        PayerName = payerName;
        Amount = amount;
        ValueDate = valueDate;
        Status = ImportLineStatus.Unmatched;
    }

    /// <summary>The owning batch id.</summary>
    public long ImportBatchId { get; private set; }

    /// <summary>The payer reference from the file.</summary>
    public string PayerReference { get; private set; }

    /// <summary>The payer name from the file.</summary>
    public string PayerName { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The value date.</summary>
    public DateOnly ValueDate { get; private set; }

    /// <summary>The match status.</summary>
    public ImportLineStatus Status { get; private set; }

    /// <summary>The matched customer, when matched.</summary>
    public long? MatchedCustomerId { get; private set; }

    /// <summary>The open item raised (the receipt credit, or the suspense/AR item).</summary>
    public long? OpenItemId { get; set; }

    /// <summary>Records a successful match to a customer.</summary>
    /// <param name="customerId">The matched customer.</param>
    public void MatchTo(long customerId)
    {
        MatchedCustomerId = customerId;
        Status = ImportLineStatus.Matched;
    }
}
