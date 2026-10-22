using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Receipting;

namespace One4All.Application.Abstractions.Receipting;

/// <summary>A resolved payer account across any register (brief §RCP-2 payer abstraction).</summary>
/// <param name="Kind">The register.</param>
/// <param name="AccountId">The account id in that register.</param>
/// <param name="Name">The display name.</param>
/// <param name="OwnerUserId">The user who owns this account, if any (for the related-party block).</param>
public readonly record struct PayerAccount(PayerKind Kind, long AccountId, string Name, long? OwnerUserId);

/// <summary>
/// Resolves a payer account within a single register (brief §RCP-2). One implementation per register (AR,
/// municipal, housing, schools) so a new vertical plugs in without touching the receipting screen.
/// </summary>
public interface IPayerAccountProvider
{
    /// <summary>The register this provider serves.</summary>
    PayerKind Kind { get; }

    /// <summary>Resolves a payer account by its id in this register.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="accountId">The account id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved account, or null if not found.</returns>
    Task<PayerAccount?> ResolveAsync(long companyId, long accountId, CancellationToken ct = default);
}

/// <summary>An opening float for a session (currency + amount).</summary>
/// <param name="Currency">The currency.</param>
/// <param name="Amount">The float amount.</param>
public readonly record struct FloatDeclaration(CurrencyCode Currency, decimal Amount);

/// <summary>How a debtor payment is spread across the account's outstanding items (brief §RCP-5).</summary>
public enum ReceiptAllocationStrategy
{
    /// <summary>Oldest charge first (FIFO by document date).</summary>
    OldestFirst = 0,

    /// <summary>Newest / current charge first (keeps the payer out of further arrears).</summary>
    NewestFirst = 1,

    /// <summary>Largest outstanding item first.</summary>
    LargestFirst = 2,

    /// <summary>The cashier directs the allocation explicitly (permissioned, with a reason).</summary>
    UserDirected = 3,
}

/// <summary>An explicit allocation instruction (for <see cref="ReceiptAllocationStrategy.UserDirected"/>).</summary>
/// <param name="OpenItemId">The debit open item (invoice/charge) to settle.</param>
/// <param name="Amount">The amount to apply to it.</param>
public readonly record struct DirectedAllocation(long OpenItemId, decimal Amount);

/// <summary>One line of a proposed allocation (shown before the receipt is committed, brief §RCP-5).</summary>
/// <param name="OpenItemId">The debit open item.</param>
/// <param name="DocumentNumber">Its document number.</param>
/// <param name="DocumentDate">Its document date.</param>
/// <param name="Outstanding">Its outstanding amount.</param>
/// <param name="Applied">The amount this receipt would apply to it.</param>
public readonly record struct AllocationPreviewLine(long OpenItemId, string DocumentNumber, DateOnly DocumentDate, decimal Outstanding, decimal Applied);

/// <summary>A proposed allocation of a payment across an account's outstanding items.</summary>
/// <param name="Lines">The per-item applications.</param>
/// <param name="Allocated">The total that would be applied to debt.</param>
/// <param name="CreditOnAccount">The overpayment that would remain as credit on account.</param>
public readonly record struct AllocationPreview(IReadOnlyList<AllocationPreviewLine> Lines, decimal Allocated, decimal CreditOnAccount);

/// <summary>A line on a receipt request.</summary>
/// <param name="Type">The line type.</param>
/// <param name="Amount">The amount.</param>
/// <param name="AccountCode">The income/deposit/suspense account (null for a debtor payment).</param>
/// <param name="Narrative">A narrative.</param>
/// <param name="Service">The service the money is for (water, rates, refuse…), for collection-by-service reporting.</param>
public readonly record struct ReceiptLineRequest(ReceiptLineType Type, decimal Amount, string? AccountCode = null, string? Narrative = null, string? Service = null);

/// <summary>A tender on a receipt request.</summary>
/// <param name="Type">The tender type.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Reference">A reference.</param>
public readonly record struct ReceiptTenderRequest(TenderType Type, decimal Amount, string? Reference = null);

/// <summary>A request to post a receipt.</summary>
public sealed class ReceiptRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The cashier session.</summary>
    public long SessionId { get; init; }

    /// <summary>The payer register.</summary>
    public PayerKind PayerKind { get; init; } = PayerKind.Anonymous;

    /// <summary>The payer account id (null for anonymous/sundry).</summary>
    public long? PayerAccountId { get; init; }

    /// <summary>The receipt date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>
    /// The receipt currency. Defaults to the company base currency; a foreign currency lets a debtor pay a
    /// base-currency debt in another currency, realising FX on allocation (brief §RCP-6).
    /// </summary>
    public CurrencyCode? Currency { get; init; }

    /// <summary>The explicit exchange rate to base, when the receipt is in a foreign currency (else looked up).</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>The cash/bank control account debited with the money received.</summary>
    public string CashAccountCode { get; init; } = string.Empty;

    /// <summary>Whether the tender is pending (a claimed bank transfer/cheque not yet confirmed).</summary>
    public bool Pending { get; init; }

    /// <summary>The uncleared-effects control account a pending receipt posts to until confirmed.</summary>
    public string? PendingAccountCode { get; init; }

    /// <summary>The credit-on-account (overpayment) liability account, when a debtor overpays.</summary>
    public string? CreditOnAccountCode { get; init; }

    /// <summary>The strategy for spreading a debtor payment across outstanding items.</summary>
    public ReceiptAllocationStrategy AllocationStrategy { get; init; } = ReceiptAllocationStrategy.OldestFirst;

    /// <summary>Why the payment is allocated other than oldest first - required then, and listed on the allocation override report (RC19).</summary>
    public string? AllocationReason { get; init; }

    /// <summary>Explicit allocations, when <see cref="AllocationStrategy"/> is user-directed.</summary>
    public IReadOnlyList<DirectedAllocation> DirectedAllocations { get; init; } = [];

    /// <summary>The lines (what the money is for).</summary>
    public IReadOnlyList<ReceiptLineRequest> Lines { get; init; } = [];

    /// <summary>The tenders (how it was paid).</summary>
    public IReadOnlyList<ReceiptTenderRequest> Tenders { get; init; } = [];

    /// <summary>When the cashier started the receipt (the payer chosen or the first amount keyed), so RC10 can show the handling time; null when not timed.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }
}

/// <summary>The result of posting a receipt.</summary>
/// <param name="ReceiptId">The receipt id.</param>
/// <param name="ReceiptNumber">The gapless receipt number.</param>
/// <param name="Total">The receipt total.</param>
/// <param name="AllocatedToDebt">The amount allocated to the payer's outstanding debt.</param>
/// <param name="CreditOnAccount">The overpayment left as credit on account.</param>
public readonly record struct ReceiptResult(long ReceiptId, string ReceiptNumber, decimal Total, decimal AllocatedToDebt, decimal CreditOnAccount);

/// <summary>The result of reversing a receipt (brief §RCP-7).</summary>
/// <param name="ReceiptId">The reversed receipt.</param>
/// <param name="Amount">The receipt total reversed.</param>
/// <param name="DebtorReversalJournal">The AR reversal journal number, when there was a debtor portion.</param>
/// <param name="CodedReversalJournal">The sundry/deposit/suspense reversal journal number, when there were coded lines.</param>
public readonly record struct ReceiptReversalResult(long ReceiptId, decimal Amount, string? DebtorReversalJournal, string? CodedReversalJournal);

/// <summary>The result of a refund (brief §RCP-9).</summary>
/// <param name="Amount">The amount refunded.</param>
/// <param name="JournalNumber">The GL journal number.</param>
public readonly record struct RefundResult(decimal Amount, string JournalNumber);

/// <summary>The result of closing a session (blind cash-up).</summary>
/// <param name="ExpectedCashBase">The expected cash in base currency (float + cash tenders).</param>
/// <param name="CountedCashBase">The counted cash in base currency.</param>
/// <param name="VarianceBase">The variance (counted − expected); negative is a shortage.</param>
/// <param name="VarianceJournalNumber">The variance journal number, or null when nil.</param>
public readonly record struct CashUpResult(decimal ExpectedCashBase, decimal CountedCashBase, decimal VarianceBase, string? VarianceJournalNumber);

/// <summary>
/// Cashiering &amp; Receipting (brief §RCP). Collects money against billed accounts (never crediting income),
/// as sundry income, deposits or suspense, through the central posting engine; manages the cashier session
/// and blind cash-up. See <c>docs/modules/RECEIPTING.md</c>.
/// </summary>
public interface IReceiptingService
{
    /// <summary>Opens a cashier session with an opening float (one open session per cashier per station).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stationId">The station.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="date">The open date.</param>
    /// <param name="floats">The opening floats per currency.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created session id.</returns>
    Task<Result<long>> OpenSessionAsync(long companyId, long stationId, long cashierUserId, DateOnly date, IReadOnlyList<FloatDeclaration> floats, CancellationToken ct = default);

    /// <summary>
    /// Previews how a debtor payment would be spread across the account's outstanding items under a
    /// strategy - shown to the payer before the receipt is committed (brief §RCP-5), never writing.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The debtor account.</param>
    /// <param name="amount">The payment amount.</param>
    /// <param name="strategy">The allocation strategy.</param>
    /// <param name="directed">Explicit allocations, when the strategy is user-directed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The proposed allocation.</returns>
    Task<Result<AllocationPreview>> PreviewAllocationAsync(long companyId, long customerId, decimal amount, ReceiptAllocationStrategy strategy, IReadOnlyList<DirectedAllocation>? directed = null, CancellationToken ct = default);

    /// <summary>
    /// Posts a receipt: debtor-payment lines credit the debtor control (via AR, allocated by strategy),
    /// sundry lines credit the income vote, deposits credit the deposit liability, and unidentified money
    /// credits the suspense control - the money is debited to the cash/bank control.
    /// </summary>
    /// <param name="request">The receipt request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The receipt result.</returns>
    Task<Result<ReceiptResult>> PostReceiptAsync(ReceiptRequest request, CancellationToken ct = default);

    /// <summary>
    /// Allocates money out of the suspense/unallocated control to a debtor account (brief §RCP-6 suspense
    /// workbench): Dr suspense control, Cr debtor control, then applies it to the account's outstanding
    /// items by strategy. Never guesses income. A permissioned, audited act with a mandatory reason: the account must be a suspense control
    /// account (<c>Rcp.NotSuspense</c>) whose unallocated balance covers the amount (<c>Rcp.SuspenseBalance</c>); when the money came in on
    /// an unidentified receipt, the allocation names it and cannot take more than that receipt left in suspense.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The debtor account the money belongs to.</param>
    /// <param name="amount">The amount to move out of suspense.</param>
    /// <param name="date">The allocation date.</param>
    /// <param name="suspenseAccountCode">The suspense/unallocated control account (debited).</param>
    /// <param name="strategy">The allocation strategy.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="userId">The user moving the money out of suspense (recorded on the allocation).</param>
    /// <param name="receiptNumber">The unidentified receipt the money came in on, where there is one (null for a bank-import line).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The amounts allocated to debt and left on account.</returns>
    Task<Result<ReceiptResult>> AllocateFromSuspenseAsync(long companyId, long customerId, decimal amount, DateOnly date, string suspenseAccountCode, ReceiptAllocationStrategy strategy, string reason,
        long userId, string? receiptNumber = null, CancellationToken ct = default);

    /// <summary>
    /// Closes a session with a blind cash-up: computes expected cash (float + cash tenders) per currency,
    /// records the variance, and posts any shortage/overage to the shortages account.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="date">The close date.</param>
    /// <param name="countedCash">The counted cash per currency.</param>
    /// <param name="cashAccountCode">The cash control account.</param>
    /// <param name="shortagesAccountCode">The cash shortages &amp; overages account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cash-up result.</returns>
    Task<Result<CashUpResult>> CloseSessionAsync(long companyId, long sessionId, DateOnly date, IReadOnlyList<FloatDeclaration> countedCash, string cashAccountCode, string shortagesAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Reverses a committed receipt (brief §RCP-7): un-allocates the debtor portion (restoring the settled
    /// charges), posts the exact opposite GL of every line (Dr debtor control / Cr cash for the debtor
    /// portion; Dr income/deposit/suspense / Cr cash for coded lines), and marks the receipt cancelled -
    /// nothing is deleted. A cashier cannot reverse their own receipt. Requires a reason.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="receiptId">The receipt to reverse.</param>
    /// <param name="date">The reversal (accounting) date.</param>
    /// <param name="reverserUserId">The user performing the reversal (must not be the original cashier).</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reversal result.</returns>
    Task<Result<ReceiptReversalResult>> ReverseReceiptAsync(long companyId, long receiptId, DateOnly date, long reverserUserId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Asks for a payer's credit balance (overpayment / credit on account, brief §RCP-9) to be refunded, in one currency: only the payer's
    /// credits in that currency count (<c>Rcp.InsufficientCredit</c>), and the bank account it is paid from must be kept in that currency
    /// (<c>Rcp.RefundAccountCurrency</c>). Nothing is paid until another user approves it with <see cref="ApproveCreditRefundAsync"/>.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The payer whose credit is refunded.</param>
    /// <param name="currency">The currency of the credit refunded and of the payment.</param>
    /// <param name="amount">The amount to refund, in that currency.</param>
    /// <param name="date">The refund date.</param>
    /// <param name="bankAccountCode">The bank/safe account the refund is paid from (credited).</param>
    /// <param name="requestedByUserId">The user asking for it.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The request id.</returns>
    Task<Result<long>> RequestCreditRefundAsync(long companyId, long customerId, CurrencyCode currency, decimal amount, DateOnly date, string bankAccountCode, long requestedByUserId, string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Approves and pays a pending credit refund: the approver must be someone other than who asked (<c>Rcp.SelfApproval</c>) and hold the
    /// receipts approval right, Sales.Receipt Approve (<c>Rcp.RefundApprover</c>). Posts Dr debtor control, Cr bank in the request's currency
    /// - paid through the bank/safe, never the till drawer - and clears the payer's credit items in that currency, oldest first; refused if
    /// the credit has since fallen below the amount.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requestId">The pending request.</param>
    /// <param name="approverUserId">The approving user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refund result.</returns>
    Task<Result<RefundResult>> ApproveCreditRefundAsync(long companyId, long requestId, long approverUserId, CancellationToken ct = default);

    /// <summary>Turns down a pending credit refund, with the reason; nothing is paid.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requestId">The pending request.</param>
    /// <param name="userId">The user turning it down.</param>
    /// <param name="reason">Why.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it could not be turned down.</returns>
    Task<Result> RejectCreditRefundAsync(long companyId, long requestId, long userId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Refunds a deposit (brief §RCP-9): Dr deposit liability for the full amount, Cr bank for the net, and
    /// Cr the forfeit account for any amount withheld. Releases the deposit liability. Approved, with a reason.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="amount">The deposit amount held.</param>
    /// <param name="forfeitAmount">The amount forfeited/withheld (0 for a full refund).</param>
    /// <param name="date">The refund date.</param>
    /// <param name="depositLiabilityAccountCode">The deposit-liability account (debited).</param>
    /// <param name="bankAccountCode">The bank account the net is paid from (credited).</param>
    /// <param name="forfeitAccountCode">The income account credited with any forfeit (required when forfeiting).</param>
    /// <param name="approverUserId">The approving user.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refund result.</returns>
    Task<Result<RefundResult>> RefundDepositAsync(long companyId, decimal amount, decimal forfeitAmount, DateOnly date, string depositLiabilityAccountCode, string bankAccountCode, string? forfeitAccountCode, long approverUserId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Records a mid-shift pickup of cash to the safe (brief §RCP-3): Dr safe, Cr cash on hand. Reduces the
    /// drawer, so the blind cash-up expects correspondingly less at close. Requires a reason.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sessionId">The open session.</param>
    /// <param name="amount">The amount picked up.</param>
    /// <param name="date">The date.</param>
    /// <param name="cashAccountCode">The cash-on-hand account (credited).</param>
    /// <param name="safeAccountCode">The safe account (debited).</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GL journal number.</returns>
    Task<Result<string>> PickupAsync(long companyId, long sessionId, decimal amount, DateOnly date, string cashAccountCode, string safeAccountCode, string reason, CancellationToken ct = default);

    /// <summary>
    /// Records a petty-cash pay-out at the counter (brief §RCP-3): Dr expense, Cr cash on hand. Reduces the
    /// drawer. A distinct permission and a mandatory reason.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sessionId">The open session.</param>
    /// <param name="amount">The amount paid out.</param>
    /// <param name="date">The date.</param>
    /// <param name="cashAccountCode">The cash-on-hand account (credited).</param>
    /// <param name="expenseAccountCode">The expense account (debited).</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GL journal number.</returns>
    Task<Result<string>> PettyCashPayoutAsync(long companyId, long sessionId, decimal amount, DateOnly date, string cashAccountCode, string expenseAccountCode, string reason, CancellationToken ct = default);

    /// <summary>
    /// Logs a reprint of a receipt (brief §RCP-15): reprints are watermarked COPY, numbered and logged.
    /// Returns the copy number (1 for the first reprint).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="receiptId">The receipt.</param>
    /// <param name="date">The reprint date.</param>
    /// <param name="byUserId">The user reprinting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The copy number.</returns>
    Task<Result<int>> ReprintReceiptAsync(long companyId, long receiptId, DateOnly date, long byUserId, CancellationToken ct = default);

    /// <summary>
    /// Captures a payment arrangement at the counter (brief §RCP-16 RC42): a promise to clear a balance in
    /// instalments. Not a GL posting - receipts against the account settle the debt; the arrangement tracks
    /// the promise for credit control.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The payer account.</param>
    /// <param name="totalAmount">The balance to clear.</param>
    /// <param name="instalmentAmount">The instalment amount.</param>
    /// <param name="startDate">The start date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created arrangement id.</returns>
    Task<Result<long>> CaptureArrangementAsync(long companyId, long customerId, decimal totalAmount, decimal instalmentAmount, DateOnly startDate, CancellationToken ct = default);

    /// <summary>
    /// Records a dishonoured cheque / reversed payment (brief §RCP-7): reverses the original receipt
    /// (restoring the debt), raises a penalty on the payer's account when one is charged, and logs the event
    /// for the dishonour register (RC34). Requires a reason and a user who is not the original cashier.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="receiptId">The receipt whose cheque bounced.</param>
    /// <param name="date">The dishonour date.</param>
    /// <param name="penaltyAmount">The penalty to charge (0 for none).</param>
    /// <param name="penaltyAccountCode">The penalty-income account (when a penalty is charged).</param>
    /// <param name="penaltyInvoiceNumber">The penalty invoice number (when a penalty is charged).</param>
    /// <param name="reverserUserId">The user recording the dishonour (not the original cashier).</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> RecordDishonourAsync(long companyId, long receiptId, DateOnly date, decimal penaltyAmount, string? penaltyAccountCode, string? penaltyInvoiceNumber, long reverserUserId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Confirms a pending tender on the bank statement (brief §RCP-7): Dr bank, Cr the uncleared-effects
    /// control the pending receipt posted to. Clears it from the pending pool.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="pendingTenderId">The pending tender.</param>
    /// <param name="date">The confirmation date.</param>
    /// <param name="bankAccountCode">The bank account the funds cleared into (debited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GL journal number.</returns>
    Task<Result<string>> ConfirmPendingTenderAsync(long companyId, long pendingTenderId, DateOnly date, string bankAccountCode, CancellationToken ct = default);

    /// <summary>Verifies a closed session by a supervisor (who must not be the cashier).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="supervisorUserId">The verifying supervisor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> VerifySessionAsync(long companyId, long sessionId, long supervisorUserId, CancellationToken ct = default);
}
