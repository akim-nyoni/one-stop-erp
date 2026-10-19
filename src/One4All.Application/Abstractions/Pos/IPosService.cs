using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Pos;

namespace One4All.Application.Abstractions.Pos;

/// <summary>A sold line in a POS sale request (amounts in base currency).</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Description">The description.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="LineNet">The net line value.</param>
/// <param name="TaxAmount">The tax on the line.</param>
/// <param name="RevenueAccountCode">The revenue account.</param>
/// <param name="PromotionDiscount">The promotion discount taken off the line (LineNet is after it).</param>
/// <param name="DiscountAccountCode">The account the discount posts to.</param>
/// <param name="PromotionCode">The promotion(s) that gave it.</param>
public readonly record struct PosSaleLineRequest(long ItemId, string Description, decimal Quantity, decimal UnitPrice, decimal LineNet, decimal TaxAmount, string RevenueAccountCode,
    decimal PromotionDiscount = 0m, string? DiscountAccountCode = null, string? PromotionCode = null, string? GiftCardNumber = null, string? Note = null, string? SerialNumber = null, bool GiftWrap = false);

/// <summary>A tender in a POS sale request.</summary>
/// <param name="TenderType">The tender type.</param>
/// <param name="Currency">The tender currency.</param>
/// <param name="Amount">The tender amount in its currency.</param>
/// <param name="AccountCode">The GL account for the tender.</param>
/// <param name="IsChange">True when this is change given.</param>
/// <param name="ExchangeRate">Optional explicit rate (else the daily rate / 1 for base).</param>
/// <param name="StaffEmployeeId">For a staff purchase, the employee charged.</param>
/// <param name="VoucherNumber">The tender's reference: the gift card (voucher), the member number (loyalty points) or the cheque number.</param>
public readonly record struct PosTenderRequest(TenderType TenderType, CurrencyCode Currency, decimal Amount, string AccountCode, bool IsChange, decimal? ExchangeRate = null, long? StaffEmployeeId = null,
    string? VoucherNumber = null);

/// <summary>A request to complete a POS sale.</summary>
public sealed class CompleteSaleRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The terminal.</summary>
    public long TerminalId { get; init; }

    /// <summary>The till session.</summary>
    public long TillSessionId { get; init; }

    /// <summary>The cashier.</summary>
    public long CashierUserId { get; init; }

    /// <summary>The sale/receipt number.</summary>
    public string SaleNumber { get; init; } = string.Empty;

    /// <summary>The sale date.</summary>
    public DateOnly SaleDate { get; init; }

    /// <summary>The warehouse stock is issued from.</summary>
    public long WarehouseId { get; init; }

    /// <summary>The customer: an on-account (credit) tender charges their account as an invoice, within their credit limit.</summary>
    public long? CustomerId { get; init; }

    /// <summary>The sales rep (agent) credited with the sale: it counts on the rep's commission statement.</summary>
    public long? SalesRepId { get; init; }

    /// <summary>The output-tax account (credited).</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>The sold lines.</summary>
    public IReadOnlyList<PosSaleLineRequest> Lines { get; init; } = [];

    /// <summary>Gets a value indicating whether the cashier checked the buyer is old enough for the sale's age-restricted items.</summary>
    public bool AgeVerified { get; init; }

    /// <summary>Gets the prescription the sale's prescription-only items are sold against.</summary>
    public string? PrescriptionReference { get; init; }

    /// <summary>The tenders (including any change as IsChange); they cover the sale and any tip.</summary>
    public IReadOnlyList<PosTenderRequest> Tenders { get; init; } = [];

    /// <summary>Gets the tip the customer adds (base currency): paid with the tenders, credited to the tips account, not revenue.</summary>
    public decimal Tip { get; init; }

    /// <summary>Gets the tips-payable account a tip is credited to.</summary>
    public string? TipsAccountCode { get; init; }
}

/// <summary>The result of completing a sale.</summary>
/// <param name="SaleId">The sale id.</param>
/// <param name="JournalNumber">The sale GL journal number.</param>
/// <param name="TotalGross">The gross total.</param>
public readonly record struct CompleteSaleResult(long SaleId, string JournalNumber, decimal TotalGross);

/// <summary>A currency the till can take, with the units of base currency one unit of it buys today.</summary>
/// <param name="Code">The currency.</param>
/// <param name="Rate">Its rate to base.</param>
public sealed record TillCurrency(CurrencyCode Code, decimal Rate);

/// <summary>
/// One basket split into several sales, each paid on its own (brief §4.1 split bill). The basket and its checks are as for
/// <see cref="CompleteSaleRequest"/>; each part says which lines (or how much of them) it takes and how it is paid. The parts are
/// numbered <c>{SaleNumber}-1</c>, <c>-2</c>, ... and either all complete or none do.
/// </summary>
public sealed class SplitBillRequest
{
    /// <summary>The basket and everything but the tenders (its <see cref="CompleteSaleRequest.Tenders"/> are ignored).</summary>
    public required CompleteSaleRequest Basket { get; init; }

    /// <summary>Each part's shares of the basket's lines (see <see cref="Application.Pos.SplitBill.Evenly"/> for an even split).</summary>
    public IReadOnlyList<IReadOnlyList<Application.Pos.SplitShare>> Parts { get; init; } = [];

    /// <summary>Each part's tenders, in the same order as <see cref="Parts"/>.</summary>
    public IReadOnlyList<IReadOnlyList<PosTenderRequest>> Tenders { get; init; } = [];
}

/// <summary>A till read (X mid-shift or Z end-of-shift).</summary>
/// <param name="SaleCount">Number of sales.</param>
/// <param name="TotalGross">Gross sales.</param>
/// <param name="TenderTotals">Gross per tender type.</param>
/// <param name="ZReadNumber">The Z number (only on a Z-read/close).</param>
public sealed record TillRead(int SaleCount, decimal TotalGross, IReadOnlyDictionary<TenderType, decimal> TenderTotals, int? ZReadNumber)
{
    /// <summary>Gets the tips taken with the shift's sales (in the tender totals, owed to staff).</summary>
    public decimal Tips { get; init; }
}

/// <summary>A request to close a till with a cash-up (declared cash count).</summary>
public sealed class CashUpRequest
{
    /// <summary>The till session to close.</summary>
    public long TillSessionId { get; init; }

    /// <summary>The cash physically counted in the drawer (base currency; used when <see cref="Counts"/> is empty).</summary>
    public decimal CountedCash { get; init; }

    /// <summary>The cash counted per currency (a currency not listed counts as none). Empty for a blind cash-up (the declared count is used).</summary>
    public IReadOnlyList<One4All.Application.Pos.CashCount> Counts { get; init; } = [];

    /// <summary>Who counted (recorded with the count).</summary>
    public long CountedByUserId { get; init; }

    /// <summary>The posting date for any variance journal.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The till's cash GL account (the drawer).</summary>
    public string CashAccountCode { get; init; } = string.Empty;

    /// <summary>The cash over/short GL account the variance posts to.</summary>
    public string OverShortAccountCode { get; init; } = string.Empty;

    /// <summary>The supervisor signing off a variance over the till's limit (<see cref="Core.Pos.PosTerminal.CashUpSignOffVariance"/>); not the cashier.</summary>
    public long? SupervisorUserId { get; init; }
}

/// <summary>The result of a cash-up close.</summary>
/// <param name="ZReadNumber">The Z-read number assigned on close.</param>
/// <param name="OpeningFloat">The opening float.</param>
/// <param name="ExpectedCash">The cash the drawer should hold (float + cash received − change).</param>
/// <param name="CountedCash">The cash counted.</param>
/// <param name="Variance">Counted − expected (positive over, negative short).</param>
/// <param name="VarianceJournalNumber">The variance journal number, or null when it balanced.</param>
/// <param name="Read">The underlying Z-read totals.</param>
public sealed record CashUpResult(
    int ZReadNumber,
    decimal OpeningFloat,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Variance,
    string? VarianceJournalNumber,
    TillRead Read)
{
    /// <summary>The cash-up per currency (expected, counted, variance); totals above are in base.</summary>
    public IReadOnlyList<One4All.Application.Pos.CurrencyCashUp> ByCurrency { get; init; } = [];
}

/// <summary>The Point of Sale service (brief §4.1): till sessions, sales with multi-currency tendering, and X/Z reads.</summary>
public interface IPosService
{
    /// <summary>Opens a till session with a float.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The terminal.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="openingFloat">The opening float.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The opened session id.</returns>
    Task<Result<long>> OpenTillAsync(long companyId, long terminalId, long cashierUserId, decimal openingFloat, CancellationToken ct = default);

    /// <summary>
    /// Opens the till a terminal opened offline, at the time it was opened there, under its offline key; replaying the same key again returns the
    /// session already opened.
    /// </summary>
    Task<Result<long>> OpenTillOfflineAsync(long companyId, long terminalId, long cashierUserId, decimal openingFloat, DateTimeOffset openedUtc, string offlineKey, CancellationToken ct = default);

    /// <summary>Completes a sale: posts the tender/revenue/tax journal and issues stock (cost of sales).</summary>
    /// <param name="request">The sale request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sale result, or an error.</returns>
    Task<Result<CompleteSaleResult>> CompleteSaleAsync(CompleteSaleRequest request, CancellationToken ct = default);

    /// <summary>Splits one basket into several sales, each with its own tenders, all in one unit of work.</summary>
    /// <param name="request">The basket, the parts and their tenders.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each part's sale.</returns>
    Task<Result<IReadOnlyList<CompleteSaleResult>>> SplitBillAsync(SplitBillRequest request, CancellationToken ct = default);

    /// <summary>Sends every sale still waiting for the fiscal device to it again (oldest first).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many were signed and how many still wait.</returns>
    Task<(int Fiscalised, int StillQueued)> RetryFiscalAsync(long companyId, CancellationToken ct = default);

    /// <summary>The currencies the till can take on a date: base, and every currency with a daily rate to it (direct, inverse or through another).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The day.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The currencies, base first.</returns>
    Task<IReadOnlyList<TillCurrency>> TenderCurrenciesAsync(long companyId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Sets how the till gives change (brief §4.1): the change currency (null: base), the base-currency cash rounding increment and the rounding account
    /// the differences go to.
    /// </summary>
    Task<Result> ConfigureChangeAsync(long companyId, string? changeCurrency, decimal? roundingIncrement, string? roundingAccountCode, CancellationToken ct = default);

    /// <summary>The change owed (in base) as handed over in the configured change currency at the day's rate, rounded to its smallest cash unit; null when change is given in base.</summary>
    Task<Result<One4All.Application.Pos.PosChangeQuote?>> ChangeQuoteAsync(long companyId, decimal changeDueBase, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Voids/refunds a completed sale: posts the exact inverse of the sale's tender/revenue/tax journal
    /// (refunding the tenders) and reverses each cost-of-sales stock issue back into inventory at its original
    /// cost. Requires a reason; refuses a sale that is not completed. Returns the reversal journal number.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="saleId">The sale to void.</param>
    /// <param name="date">The void date.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="taxAccountCode">The output-tax account (required when the sale had tax).</param>
    /// <param name="authorisedByUserId">The supervisor who authorised the void (kept on the till event).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reversal journal number.</returns>
    Task<Result<string>> VoidSaleAsync(long companyId, long saleId, DateOnly date, string reason, string? taxAccountCode = null, long? authorisedByUserId = null, CancellationToken ct = default);

    /// <summary>Produces an X-read (mid-shift, non-resetting) for a session.</summary>
    /// <param name="tillSessionId">The session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The read.</returns>
    Task<Result<TillRead>> XReadAsync(long tillSessionId, CancellationToken ct = default);

    /// <summary>Closes a session with a Z-read (end of shift, sequential and immutable).</summary>
    /// <param name="tillSessionId">The session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Z-read.</returns>
    Task<Result<TillRead>> CloseTillAsync(long tillSessionId, CancellationToken ct = default);

    /// <summary>
    /// Closes a session with a cash-up: reconciles the counted cash against the expected drawer (float +
    /// cash received − change) and posts the over/short variance to the cash-over/short account before
    /// taking the Z-read. A zero variance posts no journal.
    /// </summary>
    /// <param name="request">The cash-up request (counted cash and accounts).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cash-up result (Z-read, expected/counted/variance, variance journal).</returns>
    Task<Result<CashUpResult>> CashUpAndCloseAsync(CashUpRequest request, CancellationToken ct = default);

    /// <summary>
    /// Hands the till to another cashier mid-shift (brief §4.1 cashier switch): the outgoing cashier's session is cashed up on their count and
    /// closed, and a session opens on the same terminal for the incoming cashier with the counted cash as its float - one unit, so each
    /// cashier answers for their own drawer.
    /// </summary>
    /// <param name="request">The hand-over.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outgoing cash-up and the incoming session, or why not.</returns>
    Task<Result<CashierHandover>> HandOverAsync(CashierHandoverRequest request, CancellationToken ct = default);

    /// <summary>
    /// The quick switch (brief §4.1 "fast cashier switch without closing the sale"): the open session passes to the incoming cashier with no count and
    /// no cash-up, the sale on the screen stays, and the switch is recorded as a till event. The drawer is then shared by both until the next
    /// hand-over or Z-read, which is why the hand-over with a count is the way to end a shift.
    /// </summary>
    /// <param name="tillSessionId">The open session.</param>
    /// <param name="incomingCashierUserId">The cashier taking over.</param>
    /// <param name="date">The date, for the event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SwitchCashierAsync(long tillSessionId, long incomingCashierUserId, DateOnly date, CancellationToken ct = default);

    /// <summary>Declares a till's cash count before the expected cash is seen (blind cash-up); final once declared.</summary>
    /// <param name="tillSessionId">The till session.</param>
    /// <param name="counts">The cash counted per currency.</param>
    /// <param name="userId">Who counted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> DeclareCountAsync(long tillSessionId, IReadOnlyList<One4All.Application.Pos.CashCount> counts, long userId, CancellationToken ct = default);

    /// <summary>The cash a till should hold per currency, with what was declared (refused for a blind till until the count is declared).</summary>
    /// <param name="tillSessionId">The till session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The per-currency position.</returns>
    Task<Result<IReadOnlyList<One4All.Application.Pos.CurrencyCashUp>>> ExpectedCashAsync(long tillSessionId, CancellationToken ct = default);
}

/// <summary>A till cash movement to record.</summary>
/// <param name="TillSessionId">The open till session.</param>
/// <param name="Kind">Pickup, paid-out or float top-up.</param>
/// <param name="Amount">The amount.</param>
/// <param name="CashAccountCode">The till's cash account.</param>
/// <param name="OtherAccountCode">The safe / bank (pickup), the expense (paid-out) or the source (top-up).</param>
/// <param name="Reason">Why.</param>
/// <param name="UserId">Who did it.</param>
/// <param name="AuthorisedByUserId">The supervisor (required for paid-outs; never the same person).</param>
/// <param name="Date">The date.</param>
public sealed record TillCashRequest(long TillSessionId, TillCashKind Kind, decimal Amount, string CashAccountCode, string OtherAccountCode, string Reason, long UserId, long? AuthorisedByUserId,
    DateOnly Date);

/// <summary>
/// Cash in and out of a till drawer other than by sales (brief §4.1): pickups to the safe, paid-outs (authorised by a supervisor) and
/// float top-ups, each posted and counted in the cash expected at cash-up.
/// </summary>
public interface ITillCashService
{
    /// <summary>
    /// Records and posts a movement; refused on a closed session (<c>Pos.SessionClosed</c>), without a reason, or for a paid-out without a
    /// different supervisor (<c>Pos.AuthorisationRequired</c>).
    /// </summary>
    /// <param name="request">The movement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> RecordAsync(TillCashRequest request, CancellationToken ct = default);
}

/// <summary>A till handed from one cashier to another.</summary>
public sealed class CashierHandoverRequest
{
    /// <summary>Gets the outgoing cashier's session.</summary>
    public long TillSessionId { get; init; }

    /// <summary>Gets the incoming cashier.</summary>
    public long IncomingCashierUserId { get; init; }

    /// <summary>Gets the cash counted in the drawer (base currency; used when <see cref="Counts"/> is empty).</summary>
    public decimal CountedCash { get; init; }

    /// <summary>Gets the cash counted per currency (empty: the counted cash).</summary>
    public IReadOnlyList<One4All.Application.Pos.CashCount> Counts { get; init; } = [];

    /// <summary>The supervisor signing off the outgoing cash-up when its variance is over the till's limit.</summary>
    public long? SupervisorUserId { get; init; }

    /// <summary>Gets who counted.</summary>
    public long CountedByUserId { get; init; }

    /// <summary>Gets the posting date for any variance.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the till's cash account.</summary>
    public string CashAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the cash over/short account.</summary>
    public string OverShortAccountCode { get; init; } = string.Empty;
}

/// <summary>A completed hand-over.</summary>
/// <param name="OutgoingCashUp">The outgoing cashier's cash-up.</param>
/// <param name="IncomingSessionId">The incoming cashier's session.</param>
public sealed record CashierHandover(CashUpResult OutgoingCashUp, long IncomingSessionId);
