using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Pos;

namespace One4All.Application.Pos;

/// <summary>
/// The context needed to turn a <see cref="PosTicket"/> into a <see cref="CompleteSaleRequest"/> — the
/// surrounding session and the GL accounts each tender posts to. Supplied by the terminal when the sale
/// is paid.
/// </summary>
/// <param name="CompanyId">The company.</param>
/// <param name="TerminalId">The terminal.</param>
/// <param name="TillSessionId">The open till session.</param>
/// <param name="CashierUserId">The signed-in cashier.</param>
/// <param name="SaleNumber">The receipt number.</param>
/// <param name="SaleDate">The sale date.</param>
/// <param name="WarehouseId">The warehouse stock is issued from (for stock-tracked lines).</param>
/// <param name="Currency">The base currency the ticket is priced and tendered in.</param>
/// <param name="TaxAccountCode">The output-tax account (required when the ticket carries tax).</param>
/// <param name="ChangeAccountCode">The cash account change is given from.</param>
/// <param name="TenderAccounts">The GL account each tender type posts to.</param>
public sealed record PosSaleContext(
    long CompanyId,
    long TerminalId,
    long TillSessionId,
    long CashierUserId,
    string SaleNumber,
    DateOnly SaleDate,
    long WarehouseId,
    CurrencyCode Currency,
    string? TaxAccountCode,
    string ChangeAccountCode,
    IReadOnlyDictionary<TenderType, string> TenderAccounts,
    bool AgeVerified = false,
    string? PrescriptionReference = null,
    string? DiscountAccountCode = null);

/// <summary>
/// One line on a POS ticket. Prices are tax-exclusive (a menu/item <c>Price</c> is a net price, matching
/// the rest of the domain), so the line's tax is added on top of net at the line's rate. Money is rounded
/// to two decimals per line, which is where a customer sees it.
/// </summary>
public sealed class PosTicketLine
{
    internal PosTicketLine(long itemId, string description, decimal unitPriceNet, decimal taxRate, string revenueAccountCode, string? taxCode)
    {
        ItemId = itemId;
        Description = description;
        UnitPriceNet = unitPriceNet;
        ListUnitPriceNet = unitPriceNet;
        TaxRate = taxRate;
        RevenueAccountCode = revenueAccountCode;
        TaxCode = taxCode;
        Quantity = 1m;
    }

    /// <summary>The inventory item id, or 0 for a non-stock sellable (e.g. a menu item).</summary>
    public long ItemId { get; }

    /// <summary>The line description shown on screen and the receipt.</summary>
    public string Description { get; }

    /// <summary>The tax-exclusive unit price charged (the list price unless overridden).</summary>
    public decimal UnitPriceNet { get; private set; }

    /// <summary>Gets the price the line was rung up at, before any override.</summary>
    public decimal ListUnitPriceNet { get; private set; }

    /// <summary>Gets a value indicating whether the price was overridden.</summary>
    public bool IsOverridden => UnitPriceNet != ListUnitPriceNet;

    /// <summary>Gets the discount as a percentage of the line, if one was given that way.</summary>
    public decimal? DiscountPercent { get; private set; }

    /// <summary>Gets the discount as a fixed amount, if one was given that way.</summary>
    public decimal? DiscountAmount { get; private set; }

    /// <summary>Gets the line value before its discount.</summary>
    public decimal LineNetBeforeDiscount => PosMoney.Round(Quantity * UnitPriceNet);

    /// <summary>Gets the net discount on the line (a percentage follows the quantity; a fixed amount never exceeds the line).</summary>
    public decimal Discount => DiscountPercent is decimal percent ? PosMoney.Round(LineNetBeforeDiscount * percent / 100m)
        : DiscountAmount is decimal amount ? Math.Min(amount, LineNetBeforeDiscount) : 0m;

    /// <summary>The tax rate as a fraction (e.g. 0.15 for 15%).</summary>
    public decimal TaxRate { get; }

    /// <summary>The revenue account credited.</summary>
    public string RevenueAccountCode { get; }

    /// <summary>The tax code (informational; drives the rate the caller resolved).</summary>
    public string? TaxCode { get; }

    /// <summary>The quantity sold (settable so the terminal can +/- a line).</summary>
    public decimal Quantity { get; set; }

    /// <summary>The net line value (quantity × unit price, less any discount), rounded to two decimals.</summary>
    public decimal LineNet => LineNetBeforeDiscount - Discount;

    /// <summary>The tax on the line, rounded to two decimals.</summary>
    public decimal TaxAmount => PosMoney.Round(LineNet * TaxRate);

    /// <summary>The gross line value (net + tax).</summary>
    public decimal LineGross => LineNet + TaxAmount;

    internal void SetDiscount(decimal? percent, decimal? amount)
    {
        DiscountPercent = percent;
        DiscountAmount = amount;
    }

    internal void SetPrice(decimal unitPriceNet) => UnitPriceNet = unitPriceNet;

    internal void Restore(decimal listUnitPriceNet, decimal discount)
    {
        ListUnitPriceNet = listUnitPriceNet;
        DiscountAmount = discount > 0m ? discount : null;
    }
}

/// <summary>A tender taken against a ticket.</summary>
/// <param name="Type">The tender type.</param>
/// <param name="Amount">The amount taken (in the ticket currency).</param>
public readonly record struct PosTicketTender(TenderType Type, decimal Amount, string? VoucherNumber = null);

/// <summary>Rounds POS money to two decimals (customer-facing minor units), half away from zero.</summary>
public static class PosMoney
{
    /// <summary>Rounds to two decimals, half away from zero.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The rounded value.</returns>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The in-progress sale a touch terminal builds up: the sold lines and the tenders taken. It owns the
/// cart maths (line totals, ticket net/tax/gross), the tender maths (amount tendered, balance due, change
/// due) and turns itself into a balanced <see cref="CompleteSaleRequest"/>. It has no dependency on WPF or
/// the database so it is unit-tested directly; the terminal view-model is a thin shell over it.
/// </summary>
public sealed class PosTicket
{
    private readonly List<PosTicketLine> _lines = [];
    private readonly List<PosTicketTender> _tenders = [];

    /// <summary>The sold lines.</summary>
    public IReadOnlyList<PosTicketLine> Lines => _lines;

    /// <summary>The tenders taken (change is not stored here; it is derived).</summary>
    public IReadOnlyList<PosTicketTender> Tenders => _tenders;

    /// <summary>The net total (sum of line nets).</summary>
    public decimal SubtotalNet => PosMoney.Round(_lines.Sum(l => l.LineNet));

    /// <summary>The tax total (sum of line taxes).</summary>
    public decimal TaxTotal => PosMoney.Round(_lines.Sum(l => l.TaxAmount));

    /// <summary>The gross total the customer pays.</summary>
    public decimal GrossTotal => SubtotalNet + TaxTotal;

    /// <summary>The amount tendered so far.</summary>
    public decimal Tendered => PosMoney.Round(_tenders.Sum(t => t.Amount));

    /// <summary>The outstanding balance (never negative).</summary>
    public decimal BalanceDue => Math.Max(0m, PosMoney.Round(GrossTotal - Tendered));

    /// <summary>The change owed to the customer (never negative).</summary>
    public decimal ChangeDue => Math.Max(0m, PosMoney.Round(Tendered - GrossTotal));

    /// <summary>True once the ticket has lines and is fully paid.</summary>
    public bool IsFullyTendered => _lines.Count > 0 && Tendered >= GrossTotal;

    /// <summary>True when the ticket has neither lines nor tenders.</summary>
    public bool IsEmpty => _lines.Count == 0 && _tenders.Count == 0;

    /// <summary>
    /// Adds a sellable to the ticket. Adding the same sellable again (same item, price, rate and revenue
    /// account) increments the existing line's quantity rather than adding a duplicate.
    /// </summary>
    /// <param name="itemId">The inventory item id, or 0 for a non-stock sellable.</param>
    /// <param name="description">The line description.</param>
    /// <param name="unitPriceNet">The tax-exclusive unit price.</param>
    /// <param name="taxRate">The tax rate as a fraction (e.g. 0.15).</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    /// <param name="taxCode">The tax code (informational).</param>
    /// <param name="quantity">The quantity to add (default 1).</param>
    /// <returns>The affected line.</returns>
    public PosTicketLine AddLine(long itemId, string description, decimal unitPriceNet, decimal taxRate, string revenueAccountCode, string? taxCode = null, decimal quantity = 1m)
    {
        PosTicketLine? existing = _lines.FirstOrDefault(l =>
            l.ItemId == itemId
            && l.UnitPriceNet == unitPriceNet
            && l.TaxRate == taxRate
            && string.Equals(l.RevenueAccountCode, revenueAccountCode, StringComparison.Ordinal)
            && string.Equals(l.Description, description, StringComparison.Ordinal));

        if (existing is not null)
        {
            existing.Quantity += quantity;
            return existing;
        }

        PosTicketLine line = new(itemId, description, unitPriceNet, taxRate, revenueAccountCode, taxCode) { Quantity = quantity };
        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Discounts a line by a percentage (0–100; 0 removes the discount). Discounts above <paramref name="unsupervisedLimitPercent"/> need a
    /// supervisor: the caller passes <paramref name="supervised"/> once one has authorised it.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="percent">The percentage.</param>
    /// <param name="unsupervisedLimitPercent">The most a cashier may give alone.</param>
    /// <param name="supervised">Whether a supervisor authorised it.</param>
    /// <returns>Success, or why not (<c>Pos.SupervisorRequired</c> asks for a supervisor).</returns>
    public Result DiscountLine(PosTicketLine line, decimal percent, decimal unsupervisedLimitPercent, bool supervised)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!_lines.Contains(line) || percent is < 0m or > 100m)
        {
            return Result.Failure(Error.Validation("Pos.Discount", "A discount is between 0% and 100% of a line on this ticket."));
        }

        if (percent > unsupervisedLimitPercent && !supervised)
        {
            return Result.Failure(Error.Rule("Pos.SupervisorRequired", $"A discount over {unsupervisedLimitPercent:0.##}% needs a supervisor."));
        }

        line.SetDiscount(percent == 0m ? null : percent, null);
        return Result.Success();
    }

    /// <summary>Discounts every line by the same percentage (a whole-sale discount), with the same supervisor rule.</summary>
    /// <param name="percent">The percentage.</param>
    /// <param name="unsupervisedLimitPercent">The most a cashier may give alone.</param>
    /// <param name="supervised">Whether a supervisor authorised it.</param>
    /// <returns>Success, or why not.</returns>
    public Result DiscountSale(decimal percent, decimal unsupervisedLimitPercent, bool supervised)
    {
        if (_lines.Count == 0)
        {
            return Result.Failure(Error.Validation("Pos.Empty", "The ticket has no lines."));
        }

        foreach (PosTicketLine line in _lines)
        {
            Result applied = DiscountLine(line, percent, unsupervisedLimitPercent, supervised);
            if (applied.IsFailure)
            {
                return applied;
            }
        }

        return Result.Success();
    }

    /// <summary>Overrides a line's unit price (always supervised: the caller passes <paramref name="supervised"/> once authorised).</summary>
    /// <param name="line">The line.</param>
    /// <param name="unitPriceNet">The new tax-exclusive unit price.</param>
    /// <param name="supervised">Whether a supervisor authorised it.</param>
    /// <returns>Success, or why not.</returns>
    public Result OverridePrice(PosTicketLine line, decimal unitPriceNet, bool supervised)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!_lines.Contains(line) || unitPriceNet < 0m)
        {
            return Result.Failure(Error.Validation("Pos.Price", "The price must not be negative, on a line of this ticket."));
        }

        if (!supervised)
        {
            return Result.Failure(Error.Rule("Pos.SupervisorRequired", "A price override needs a supervisor."));
        }

        line.SetPrice(PosMoney.Round(unitPriceNet));
        return Result.Success();
    }

    /// <summary>The ticket's lines as sale lines (for parking it, or posting it).</summary>
    /// <param name="discountAccountCode">The account discounts post to (needed when any line is discounted).</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<PosSaleLineRequest> SaleLines(string? discountAccountCode = null)
        => [.. _lines.Select(l => new PosSaleLineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPriceNet, l.LineNet, l.TaxAmount, l.RevenueAccountCode,
            l.Discount, l.Discount > 0m ? discountAccountCode : null, l.Discount > 0m ? (l.IsOverridden ? "OVERRIDE+DISC" : "MANUAL") : null))];

    /// <summary>Loads parked lines back onto an empty ticket (prices, discounts and quantities as parked; tax rates looked up again).</summary>
    /// <param name="lines">The parked lines.</param>
    /// <param name="taxRateFor">The tax rate for a line (by its revenue account and description).</param>
    /// <returns>Success, or why not.</returns>
    public Result Load(IEnumerable<PosSaleLineRequest> lines, Func<PosSaleLineRequest, decimal> taxRateFor)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(taxRateFor);
        if (!IsEmpty)
        {
            return Result.Failure(Error.Rule("Pos.NotEmpty", "Finish or park the current sale first."));
        }

        foreach (PosSaleLineRequest parked in lines)
        {
            PosTicketLine line = new(parked.ItemId, parked.Description, parked.UnitPrice, taxRateFor(parked), parked.RevenueAccountCode, null) { Quantity = parked.Quantity };
            line.Restore(parked.UnitPrice, parked.PromotionDiscount);
            _lines.Add(line);
        }

        return Result.Success();
    }

    /// <summary>Removes a line from the ticket.</summary>
    /// <param name="line">The line to remove.</param>
    public void RemoveLine(PosTicketLine line) => _lines.Remove(line);

    /// <summary>Records a tender against the ticket.</summary>
    /// <param name="type">The tender type.</param>
    /// <param name="amount">The amount (must be positive).</param>
    /// <returns>Success, or a validation error.</returns>
    /// <param name="voucherNumber">For a voucher tender, the gift card it is paid from.</param>
    public Result AddTender(TenderType type, decimal amount, string? voucherNumber = null)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation("Pos.Tender", "A tender amount must be positive."));
        }

        if (type == TenderType.Voucher && string.IsNullOrWhiteSpace(voucherNumber))
        {
            return Result.Failure(Error.Validation("Pos.VoucherTender", "A voucher tender needs the gift card number."));
        }

        _tenders.Add(new PosTicketTender(type, PosMoney.Round(amount), type == TenderType.Voucher ? voucherNumber!.Trim().ToUpperInvariant() : null));
        return Result.Success();
    }

    /// <summary>Clears all tenders (e.g. the cashier restarts payment) but keeps the sold lines.</summary>
    public void ClearTenders() => _tenders.Clear();

    /// <summary>Resets the ticket for the next customer.</summary>
    public void Clear()
    {
        _lines.Clear();
        _tenders.Clear();
    }

    /// <summary>
    /// Builds a balanced <see cref="CompleteSaleRequest"/> from the ticket. Any over-tender is emitted as a
    /// change tender against the change account, so the net tendered equals the gross total exactly (the
    /// invariant the POS service enforces before posting).
    /// </summary>
    /// <param name="context">The surrounding session and account mapping.</param>
    /// <returns>The request, or an error when the ticket is empty, underpaid, or a tender account is missing.</returns>
    public Result<CompleteSaleRequest> BuildSale(PosSaleContext context)
    {
        if (_lines.Count == 0)
        {
            return Error.Validation("Pos.Empty", "The ticket has no lines.");
        }

        if (!IsFullyTendered)
        {
            return Error.Rule("Pos.Underpaid", $"The ticket is short by {BalanceDue:0.00}.");
        }

        if (_lines.Any(l => l.Discount > 0m) && string.IsNullOrWhiteSpace(context.DiscountAccountCode))
        {
            return Error.Validation("Pos.DiscountAccount", "No discount account is configured for discounted lines.");
        }

        List<PosSaleLineRequest> lines = [.. SaleLines(context.DiscountAccountCode)];

        List<PosTenderRequest> tenders = [];
        foreach (PosTicketTender tender in _tenders)
        {
            if (!context.TenderAccounts.TryGetValue(tender.Type, out string? account) || string.IsNullOrWhiteSpace(account))
            {
                return Error.Validation("Pos.TenderAccount", $"No GL account is configured for {tender.Type} tenders.");
            }

            tenders.Add(new PosTenderRequest(tender.Type, context.Currency, tender.Amount, account, IsChange: false, VoucherNumber: tender.VoucherNumber));
        }

        if (ChangeDue > 0m)
        {
            if (string.IsNullOrWhiteSpace(context.ChangeAccountCode))
            {
                return Error.Validation("Pos.ChangeAccount", "No change account is configured.");
            }

            tenders.Add(new PosTenderRequest(TenderType.Cash, context.Currency, ChangeDue, context.ChangeAccountCode, IsChange: true));
        }

        return new CompleteSaleRequest
        {
            CompanyId = context.CompanyId,
            TerminalId = context.TerminalId,
            TillSessionId = context.TillSessionId,
            CashierUserId = context.CashierUserId,
            SaleNumber = context.SaleNumber,
            SaleDate = context.SaleDate,
            WarehouseId = context.WarehouseId,
            TaxAccountCode = context.TaxAccountCode,
            AgeVerified = context.AgeVerified,
            PrescriptionReference = context.PrescriptionReference,
            Lines = lines,
            Tenders = tenders,
        };
    }
}
