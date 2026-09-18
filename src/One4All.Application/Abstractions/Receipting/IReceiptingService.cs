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

/// <summary>A line on a receipt request.</summary>
/// <param name="Type">The line type.</param>
/// <param name="Amount">The amount.</param>
/// <param name="AccountCode">The income/deposit/suspense account (null for a debtor payment).</param>
/// <param name="Narrative">A narrative.</param>
public readonly record struct ReceiptLineRequest(ReceiptLineType Type, decimal Amount, string? AccountCode = null, string? Narrative = null);

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

    /// <summary>The cash/bank control account debited with the money received.</summary>
    public string CashAccountCode { get; init; } = string.Empty;

    /// <summary>The credit-on-account (overpayment) liability account, when a debtor overpays.</summary>
    public string? CreditOnAccountCode { get; init; }

    /// <summary>The lines (what the money is for).</summary>
    public IReadOnlyList<ReceiptLineRequest> Lines { get; init; } = [];

    /// <summary>The tenders (how it was paid).</summary>
    public IReadOnlyList<ReceiptTenderRequest> Tenders { get; init; } = [];
}

/// <summary>The result of posting a receipt.</summary>
/// <param name="ReceiptId">The receipt id.</param>
/// <param name="ReceiptNumber">The gapless receipt number.</param>
/// <param name="Total">The receipt total.</param>
/// <param name="AllocatedToDebt">The amount allocated to the payer's outstanding debt.</param>
/// <param name="CreditOnAccount">The overpayment left as credit on account.</param>
public readonly record struct ReceiptResult(long ReceiptId, string ReceiptNumber, decimal Total, decimal AllocatedToDebt, decimal CreditOnAccount);

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
    /// Posts a receipt: debtor-payment lines credit the debtor control (via AR, allocated oldest-first),
    /// sundry lines credit the income vote, deposits credit the deposit liability, and unidentified money
    /// credits the suspense control — the money is debited to the cash/bank control.
    /// </summary>
    /// <param name="request">The receipt request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The receipt result.</returns>
    Task<Result<ReceiptResult>> PostReceiptAsync(ReceiptRequest request, CancellationToken ct = default);

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

    /// <summary>Verifies a closed session by a supervisor (who must not be the cashier).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="supervisorUserId">The verifying supervisor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> VerifySessionAsync(long companyId, long sessionId, long supervisorUserId, CancellationToken ct = default);
}
