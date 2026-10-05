using One4All.Core.Common;

namespace One4All.Core.Banking;

/// <summary>Where a cheque stands.</summary>
public enum ChequeStatus
{
    /// <summary>Written and handed over; not yet through the bank.</summary>
    Issued = 1,

    /// <summary>Cleared by the bank.</summary>
    Presented = 2,

    /// <summary>Stopped or cancelled after issue.</summary>
    Cancelled = 3,

    /// <summary>A spoiled leaf, never issued.</summary>
    Voided = 4,
}

/// <summary>A cheque drawn on a bank account (<c>bank.cheque</c>), tracked from issue to presentation.</summary>
public sealed class Cheque : AuditableEntity, ICompanyScoped
{
    private Cheque()
    {
        ChequeNumber = string.Empty;
        Payee = string.Empty;
    }

    /// <summary>Records a cheque.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="chequeNumber">The cheque number.</param>
    /// <param name="issuedOn">The date written.</param>
    /// <param name="payee">The payee.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="documentNumber">The payment it settles.</param>
    /// <param name="status">Issued, or Voided for a spoiled leaf.</param>
    public Cheque(long companyId, long bankAccountId, string chequeNumber, DateOnly issuedOn, string payee, decimal amount, string? documentNumber, ChequeStatus status)
    {
        CompanyId = companyId;
        BankAccountId = bankAccountId;
        ChequeNumber = chequeNumber;
        IssuedOn = issuedOn;
        Payee = payee;
        Amount = amount;
        DocumentNumber = documentNumber;
        Status = status;
        StatusDate = issuedOn;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the bank account.</summary>
    public long BankAccountId { get; private set; }

    /// <summary>Gets the cheque number.</summary>
    public string ChequeNumber { get; private set; }

    /// <summary>Gets the date written.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>Gets the payee.</summary>
    public string Payee { get; private set; }

    /// <summary>Gets the amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets the payment document it settles.</summary>
    public string? DocumentNumber { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public ChequeStatus Status { get; private set; }

    /// <summary>Gets when it last changed status.</summary>
    public DateOnly StatusDate { get; private set; }

    /// <summary>Gets why it was cancelled or voided.</summary>
    public string? Reason { get; private set; }

    /// <summary>Moves the cheque on.</summary>
    /// <param name="status">The new status.</param>
    /// <param name="date">When.</param>
    /// <param name="reason">Why (cancellations).</param>
    public void Move(ChequeStatus status, DateOnly date, string? reason)
    {
        Status = status;
        StatusDate = date;
        Reason = reason;
    }
}

/// <summary>A supplier's own statement as received (<c>bank.supplier_statement</c>), for reconciling to our ledger.</summary>
public sealed class SupplierStatement : AuditableEntity, ICompanyScoped
{
    private readonly List<SupplierStatementLine> _lines = [];

    private SupplierStatement()
    {
    }

    /// <summary>Captures a statement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="statementDate">The statement date.</param>
    /// <param name="closingBalance">The balance they say we owe.</param>
    public SupplierStatement(long companyId, long supplierId, DateOnly statementDate, decimal closingBalance)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        StatementDate = statementDate;
        ClosingBalance = closingBalance;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>Gets the statement date.</summary>
    public DateOnly StatementDate { get; private set; }

    /// <summary>Gets the balance they say we owe.</summary>
    public decimal ClosingBalance { get; private set; }

    /// <summary>Gets the open items they list.</summary>
    public IReadOnlyList<SupplierStatementLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds an item they list as open.</summary>
    /// <param name="documentNumber">Their document number (our supplier invoice / payment reference).</param>
    /// <param name="documentDate">Its date.</param>
    /// <param name="amount">The amount they show open (+ we owe, − in our favour).</param>
    public void AddLine(string documentNumber, DateOnly documentDate, decimal amount) => _lines.Add(new SupplierStatementLine(documentNumber, documentDate, amount));
}

/// <summary>One open item on a supplier's statement.</summary>
public sealed class SupplierStatementLine : Entity
{
    private SupplierStatementLine()
    {
        DocumentNumber = string.Empty;
    }

    /// <summary>Creates a line.</summary>
    /// <param name="documentNumber">The document.</param>
    /// <param name="documentDate">Its date.</param>
    /// <param name="amount">The amount open.</param>
    public SupplierStatementLine(string documentNumber, DateOnly documentDate, decimal amount)
    {
        DocumentNumber = documentNumber;
        DocumentDate = documentDate;
        Amount = amount;
    }

    /// <summary>Gets the statement.</summary>
    public long SupplierStatementId { get; private set; }

    /// <summary>Gets the document.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>Gets its date.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>Gets the amount open.</summary>
    public decimal Amount { get; private set; }
}
