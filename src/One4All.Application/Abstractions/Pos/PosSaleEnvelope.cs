using One4All.Core.Accounting;
using One4All.Core.Pos;

namespace One4All.Application.Abstractions.Pos;

/// <summary>A sold line captured offline (a serializable mirror of <see cref="PosSaleLineRequest"/>).</summary>
/// <param name="ItemId">The item id (0 for a non-stock sellable).</param>
/// <param name="Description">The description.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="LineNet">The net line value.</param>
/// <param name="TaxAmount">The tax on the line.</param>
/// <param name="RevenueAccountCode">The revenue account.</param>
public sealed record PosSaleLineEnvelope(long ItemId, string Description, decimal Quantity, decimal UnitPrice, decimal LineNet, decimal TaxAmount, string RevenueAccountCode, string? GiftCardNumber = null);

/// <summary>A tender captured offline (a serializable mirror of <see cref="PosTenderRequest"/>).</summary>
/// <param name="TenderType">The tender type.</param>
/// <param name="Currency">The tender currency code.</param>
/// <param name="Amount">The amount in its currency.</param>
/// <param name="AccountCode">The GL account.</param>
/// <param name="IsChange">True when this is change given.</param>
public sealed record PosTenderEnvelope(TenderType TenderType, string Currency, decimal Amount, string AccountCode, bool IsChange);

/// <summary>
/// A self-describing, serializable snapshot of a POS sale captured while the terminal was offline. It uses
/// only primitives and enums (currency as a code string) so it round-trips cleanly through JSON, and it
/// rebuilds the exact <see cref="CompleteSaleRequest"/> the online path would have posted. The
/// <see cref="SaleNumber"/> — generated on the terminal — is the natural idempotency key: replaying the
/// same sale number must not post twice.
/// </summary>
/// <param name="CompanyId">The company.</param>
/// <param name="TerminalId">The terminal.</param>
/// <param name="TillSessionId">The till session.</param>
/// <param name="CashierUserId">The cashier.</param>
/// <param name="SaleNumber">The receipt number (idempotency key).</param>
/// <param name="SaleDate">The sale date.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Currency">The base currency code.</param>
/// <param name="TaxAccountCode">The output-tax account, if any.</param>
/// <param name="Lines">The sold lines.</param>
/// <param name="Tenders">The tenders (including any change as IsChange).</param>
/// <param name="AgeVerified">Whether the cashier confirmed the buyer's age.</param>
/// <param name="PrescriptionReference">The prescription prescription-only items were sold against.</param>
public sealed record PosSaleEnvelope(
    long CompanyId,
    long TerminalId,
    long TillSessionId,
    long CashierUserId,
    string SaleNumber,
    DateOnly SaleDate,
    long WarehouseId,
    string Currency,
    string? TaxAccountCode,
    IReadOnlyList<PosSaleLineEnvelope> Lines,
    IReadOnlyList<PosTenderEnvelope> Tenders,
    bool AgeVerified = false,
    string? PrescriptionReference = null,
    string? OfflineSessionKey = null,
    decimal Tip = 0m,
    string? TipsAccountCode = null)
{
    /// <summary>The operation kind used to route this envelope to its replay handler.</summary>
    public const string Kind = "PosSale";

    /// <summary>Rebuilds the <see cref="CompleteSaleRequest"/> this envelope captured.</summary>
    /// <returns>The sale request.</returns>
    public CompleteSaleRequest ToRequest() => new()
    {
        CompanyId = CompanyId,
        TerminalId = TerminalId,
        TillSessionId = TillSessionId,
        CashierUserId = CashierUserId,
        SaleNumber = SaleNumber,
        SaleDate = SaleDate,
        WarehouseId = WarehouseId,
        TaxAccountCode = TaxAccountCode,
        AgeVerified = AgeVerified,
        PrescriptionReference = PrescriptionReference,
        Tip = Tip,
        TipsAccountCode = TipsAccountCode,
        Lines = Lines
            .Select(l => new PosSaleLineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.LineNet, l.TaxAmount, l.RevenueAccountCode, GiftCardNumber: l.GiftCardNumber))
            .ToList(),
        Tenders = Tenders
            .Select(t => new PosTenderRequest(t.TenderType, CurrencyCode.Parse(t.Currency), t.Amount, t.AccountCode, t.IsChange))
            .ToList(),
    };
}

/// <summary>
/// A till opened while the terminal could not reach the central database (brief §4.1 offline mode): replayed before the sales captured in it,
/// which carry its <see cref="OfflineKey"/> in place of a session id.
/// </summary>
/// <param name="CompanyId">The company.</param>
/// <param name="TerminalId">The terminal.</param>
/// <param name="CashierUserId">The cashier.</param>
/// <param name="OpeningFloat">The opening float.</param>
/// <param name="OpenedUtc">When it was opened at the terminal.</param>
/// <param name="OfflineKey">The key the terminal gave the session.</param>
public sealed record PosOpenTillEnvelope(long CompanyId, long TerminalId, long CashierUserId, decimal OpeningFloat, DateTimeOffset OpenedUtc, string OfflineKey)
{
    /// <summary>The operation kind.</summary>
    public const string Kind = "PosOpenTill";
}

/// <summary>A sale voided while the till was offline: replayed against the sale once it has synced (the sale and its stock are reversed).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="SaleNumber">The sale (by number: an offline sale has no id until it syncs).</param>
/// <param name="Date">The void date.</param>
/// <param name="Reason">Why.</param>
/// <param name="TaxAccountCode">The output-tax account.</param>
/// <param name="AuthorisedByUserId">The supervisor who authorised it (verified against the sealed snapshot).</param>
public sealed record PosVoidEnvelope(long CompanyId, string SaleNumber, DateOnly Date, string Reason, string? TaxAccountCode, long AuthorisedByUserId)
{
    /// <summary>The operation kind.</summary>
    public const string Kind = "PosVoid";
}

/// <summary>
/// A sale refunded in full, in cash, while the till was offline: replayed against the sale's lines once it has synced. The reason carries the terminal's
/// offline reference, which keeps a redelivered refund from paying out twice.
/// </summary>
/// <param name="CompanyId">The company.</param>
/// <param name="SaleNumber">The sale (by number).</param>
/// <param name="TerminalId">The till.</param>
/// <param name="CashierUserId">The cashier.</param>
/// <param name="AuthorisedByUserId">The supervisor.</param>
/// <param name="Date">The refund date.</param>
/// <param name="RefundAccountCode">The cash account the money is paid from.</param>
/// <param name="TaxAccountCode">The output-tax account.</param>
/// <param name="Reason">Why, with the offline reference.</param>
/// <param name="TillSessionId">The session (0 when the till itself was opened offline).</param>
/// <param name="OfflineSessionKey">The key of a till opened offline.</param>
public sealed record PosRefundEnvelope(long CompanyId, string SaleNumber, long TerminalId, long CashierUserId, long? AuthorisedByUserId, DateOnly Date, string RefundAccountCode,
    string? TaxAccountCode, string Reason, long TillSessionId, string? OfflineSessionKey)
{
    /// <summary>The operation kind.</summary>
    public const string Kind = "PosRefund";
}

/// <summary>One currency's declared count in an offline cash-up.</summary>
/// <param name="Currency">The currency.</param>
/// <param name="Amount">The amount counted.</param>
public sealed record PosCashCountEnvelope(string Currency, decimal Amount);

/// <summary>
/// A till closed while offline: the count declared on the terminal, cashed up and closed centrally once the till (and the sales rung up in it) have synced;
/// with no count the session is closed with a Z-read only.
/// </summary>
/// <param name="CompanyId">The company.</param>
/// <param name="TillSessionId">The session (0 when the till was opened offline).</param>
/// <param name="OfflineSessionKey">The key of a till opened offline.</param>
/// <param name="Counts">The declared counts (empty: no cash-up).</param>
/// <param name="CountedByUserId">Who counted.</param>
/// <param name="Date">The cash-up date.</param>
/// <param name="CashAccountCode">The cash account.</param>
/// <param name="OverShortAccountCode">The over/short account.</param>
public sealed record PosCashUpEnvelope(long CompanyId, long TillSessionId, string? OfflineSessionKey, IReadOnlyList<PosCashCountEnvelope> Counts, long CountedByUserId, DateOnly Date,
    string? CashAccountCode, string? OverShortAccountCode)
{
    /// <summary>The operation kind.</summary>
    public const string Kind = "PosCashUp";
}
