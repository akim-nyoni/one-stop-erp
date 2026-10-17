using One4All.Core.Common;

namespace One4All.Application.Abstractions.Banking;

/// <summary>A supplier statement line as captured.</summary>
/// <param name="DocumentNumber">The document.</param>
/// <param name="DocumentDate">Its date.</param>
/// <param name="Amount">The amount they show open.</param>
public readonly record struct StatementLineInput(string DocumentNumber, DateOnly DocumentDate, decimal Amount);

/// <summary>Cheques and supplier statements (brief §14 C 61, D 75).</summary>
public interface IChequeService
{
    /// <summary>Records a cheque written on a bank account (cheque numbers are unique per account).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="chequeNumber">The number.</param>
    /// <param name="issuedOn">The date written.</param>
    /// <param name="payee">The payee.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="documentNumber">The payment it settles.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cheque id, or an error.</returns>
    Task<Result<long>> IssueAsync(long companyId, long bankAccountId, string chequeNumber, DateOnly issuedOn, string payee, decimal amount, string? documentNumber, CancellationToken ct = default);

    /// <summary>Records a spoiled cheque leaf so the number sequence stays complete.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="chequeNumber">The number.</param>
    /// <param name="date">The date.</param>
    /// <param name="reason">Why it was spoiled.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cheque id, or an error.</returns>
    Task<Result<long>> VoidLeafAsync(long companyId, long bankAccountId, string chequeNumber, DateOnly date, string reason, CancellationToken ct = default);

    /// <summary>Marks an issued cheque as cleared by the bank.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="chequeId">The cheque.</param>
    /// <param name="date">The clearing date (not before issue).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> PresentAsync(long companyId, long chequeId, DateOnly date, CancellationToken ct = default);

    /// <summary>Cancels (stops) an issued cheque that has not been presented.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="chequeId">The cheque.</param>
    /// <param name="date">The date.</param>
    /// <param name="reason">Why.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> CancelAsync(long companyId, long chequeId, DateOnly date, string reason, CancellationToken ct = default);

    /// <summary>Captures a supplier's statement - the balance they show and the items they list as open - for reconciliation.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="statementDate">The statement date.</param>
    /// <param name="closingBalance">The balance they show.</param>
    /// <param name="lines">The items they list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The statement id, or an error.</returns>
    Task<Result<long>> CaptureSupplierStatementAsync(long companyId, long supplierId, DateOnly statementDate, decimal closingBalance, IReadOnlyList<StatementLineInput> lines, CancellationToken ct = default);
}
