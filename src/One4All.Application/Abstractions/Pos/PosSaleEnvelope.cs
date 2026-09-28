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
public sealed record PosSaleLineEnvelope(long ItemId, string Description, decimal Quantity, decimal UnitPrice, decimal LineNet, decimal TaxAmount, string RevenueAccountCode);

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
    IReadOnlyList<PosTenderEnvelope> Tenders)
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
        Lines = Lines
            .Select(l => new PosSaleLineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.LineNet, l.TaxAmount, l.RevenueAccountCode))
            .ToList(),
        Tenders = Tenders
            .Select(t => new PosTenderRequest(t.TenderType, CurrencyCode.Parse(t.Currency), t.Amount, t.AccountCode, t.IsChange))
            .ToList(),
    };
}
