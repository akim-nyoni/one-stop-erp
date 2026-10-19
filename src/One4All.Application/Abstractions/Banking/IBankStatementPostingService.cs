using One4All.Core.Common;

namespace One4All.Application.Abstractions.Banking;

/// <summary>What posting a statement line produced.</summary>
/// <param name="JournalNumber">The cash-book journal posted.</param>
/// <param name="JournalLineId">The bank leg the statement line is now matched to.</param>
/// <param name="OpenItemId">The AR / AP open item created, or null for a direct-to-account posting.</param>
public readonly record struct StatementLinePosting(string JournalNumber, long JournalLineId, long? OpenItemId);

/// <summary>
/// Posts the cash-book entry for an unreconciled bank statement line and matches the two in one step
/// (brief §4.7) - the receipt, payment or charge the bank already knows about but the books do not. Each
/// posting and its match commit together or not at all.
/// </summary>
public interface IBankStatementPostingService
{
    /// <summary>
    /// Posts a money-in statement line as a receipt on a customer's account (Dr bank / Cr AR control, creating
    /// an unallocated receipt open item) and matches it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="statementLineId">The statement line (must be unreconciled and positive).</param>
    /// <param name="customerId">The paying customer.</param>
    /// <param name="documentNumber">The receipt number, or null to use the bank reference (else <c>BNK-{line}</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting.</returns>
    Task<Result<StatementLinePosting>> PostCustomerReceiptAsync(long companyId, long bankAccountId, long statementLineId, long customerId, string? documentNumber = null, CancellationToken ct = default);

    /// <summary>
    /// Posts a money-out statement line as a payment to a supplier (Dr AP control / Cr bank, creating an
    /// unallocated payment open item) and matches it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="statementLineId">The statement line (must be unreconciled and negative).</param>
    /// <param name="supplierId">The supplier paid.</param>
    /// <param name="documentNumber">The payment number, or null to use the bank reference (else <c>BNK-{line}</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting.</returns>
    Task<Result<StatementLinePosting>> PostSupplierPaymentAsync(long companyId, long bankAccountId, long statementLineId, long supplierId, string? documentNumber = null, CancellationToken ct = default);

    /// <summary>
    /// Posts a statement line straight to a nominal account - bank charges, interest received, a standing
    /// order - as a cash-book journal (either direction) and matches it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="statementLineId">The statement line (must be unreconciled).</param>
    /// <param name="accountCode">The contra account (e.g. bank charges expense).</param>
    /// <param name="narrative">An optional narrative (defaults to the bank's description).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="taxCode">The tax code when the amount includes VAT (bank charges, utilities): the net posts to the account, the VAT to the code's input (money out) or output (money in) account.</param>
    /// <returns>The posting.</returns>
    Task<Result<StatementLinePosting>> PostToAccountAsync(long companyId, long bankAccountId, long statementLineId, string accountCode, string? narrative = null, CancellationToken ct = default,
        string? taxCode = null);
}
