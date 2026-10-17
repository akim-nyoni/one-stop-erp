using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>Where a sale stands with the fiscal device.</summary>
public enum FiscalStatus
{
    /// <summary>No fiscal device is in use.</summary>
    NotRequired = 0,

    /// <summary>Signed by the device.</summary>
    Fiscalised = 1,

    /// <summary>Waiting for the device (offline or refused); retried until signed.</summary>
    Queued = 2,
}

/// <summary>
/// A completed POS sale (brief §4.1, <c>pos.sale</c>). Holds the sold lines and the tenders. On completion
/// the sale posts through the central engine: Dr the tender accounts, Cr revenue by line, Cr output tax,
/// and (via the inventory issue) Dr cost of sales / Cr stock. Change is a negative tender.
/// </summary>
public sealed class Sale : AuditableEntity, ICompanyScoped
{
    private readonly List<SaleLine> _lines = [];
    private readonly List<SaleTender> _tenders = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Sale() => SaleNumber = string.Empty;

    /// <summary>Creates a sale.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The terminal.</param>
    /// <param name="tillSessionId">The till session.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="saleNumber">The sale/receipt number.</param>
    /// <param name="saleDate">The sale date.</param>
    /// <param name="baseCurrency">The company base currency.</param>
    public Sale(long companyId, long terminalId, long tillSessionId, long cashierUserId, string saleNumber, DateOnly saleDate, CurrencyCode baseCurrency)
    {
        CompanyId = companyId;
        TerminalId = terminalId;
        TillSessionId = tillSessionId;
        CashierUserId = cashierUserId;
        SaleNumber = saleNumber;
        SaleDate = saleDate;
        BaseCurrency = baseCurrency;
        Status = SaleStatus.Completed;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The terminal.</summary>
    public long TerminalId { get; private set; }

    /// <summary>The till session.</summary>
    public long TillSessionId { get; private set; }

    /// <summary>The cashier.</summary>
    public long CashierUserId { get; private set; }

    /// <summary>The sale/receipt number.</summary>
    public string SaleNumber { get; private set; }

    /// <summary>The sale date.</summary>
    public DateOnly SaleDate { get; private set; }

    /// <summary>The company base currency.</summary>
    public CurrencyCode BaseCurrency { get; private set; }

    /// <summary>The customer an on-account sale is charged to (brief §4.1).</summary>
    public long? CustomerId { get; set; }

    /// <summary>The status.</summary>
    public SaleStatus Status { get; private set; }

    /// <summary>The GL journal that posted the sale.</summary>
    public long? JournalEntryId { get; set; }

    /// <summary>The sold lines.</summary>
    public IReadOnlyList<SaleLine> Lines => _lines.AsReadOnly();

    /// <summary>The tenders.</summary>
    public IReadOnlyList<SaleTender> Tenders => _tenders.AsReadOnly();

    /// <summary>The net (tax-exclusive) total in base currency.</summary>
    public decimal TotalNet => _lines.Sum(l => l.LineNet);

    /// <summary>The tax total in base currency.</summary>
    public decimal TotalTax => _lines.Sum(l => l.TaxAmount);

    /// <summary>The gross total in base currency.</summary>
    public decimal TotalGross => TotalNet + TotalTax;

    /// <summary>The net tender in base currency (tenders in, less change).</summary>
    public decimal NetTenderedBase => _tenders.Sum(t => t.IsChange ? -t.BaseAmount : t.BaseAmount);

    /// <summary>Adds a sale line.</summary>
    /// <param name="line">The line.</param>
    public void AddLine(SaleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        line.Stamp(CompanyId, SaleDate);
        _lines.Add(line);
    }

    /// <summary>Adds a tender.</summary>
    /// <param name="tender">The tender.</param>
    /// <summary>Gets the cash rounding on the sale (tendered less the exact total).</summary>
    public decimal CashRounding { get; private set; }

    /// <summary>Records the cash rounding.</summary>
    /// <param name="amount">Tendered less the exact total.</param>
    public void RecordCashRounding(decimal amount) => CashRounding = amount;

    /// <summary>Gets the tip the customer added at the till (paid with the tenders, owed to staff; not revenue).</summary>
    public decimal Tip { get; private set; }

    /// <summary>Records the tip.</summary>
    /// <param name="amount">The tip.</param>
    public void RecordTip(decimal amount) => Tip = amount;

    /// <summary>Where the sale stands with the fiscal device (brief §3.5).</summary>
    public FiscalStatus FiscalStatus { get; private set; }

    /// <summary>The fiscal code the device gave the receipt.</summary>
    public string? FiscalCode { get; private set; }

    /// <summary>The device's signature or verification link (what the receipt's QR carries).</summary>
    public string? FiscalVerification { get; private set; }

    /// <summary>The fiscal day the receipt was signed in.</summary>
    public int? FiscalDayNumber { get; private set; }

    /// <summary>How many times fiscalisation has been tried.</summary>
    public int FiscalAttempts { get; private set; }

    /// <summary>Why the last attempt did not sign it.</summary>
    public string? FiscalError { get; private set; }

    /// <summary>Records the device's signature.</summary>
    /// <param name="code">The fiscal code.</param>
    /// <param name="verification">The signature or verification link.</param>
    /// <param name="dayNumber">The fiscal day.</param>
    public void RecordFiscal(string code, string? verification, int? dayNumber)
    {
        FiscalStatus = FiscalStatus.Fiscalised;
        FiscalCode = code;
        FiscalVerification = verification;
        FiscalDayNumber = dayNumber;
        FiscalAttempts++;
        FiscalError = null;
    }

    /// <summary>Queues the receipt for the device (unreachable or refused), to be retried.</summary>
    /// <param name="reason">Why.</param>
    public void QueueFiscal(string reason)
    {
        FiscalStatus = FiscalStatus.Queued;
        FiscalAttempts++;
        FiscalError = reason.Length > 300 ? reason[..300] : reason;
    }

    /// <summary>Gets or sets a value indicating whether the cashier confirmed the buyer's age for age-restricted items.</summary>
    public bool AgeVerified { get; set; }

    /// <summary>Gets or sets the prescription the sale's prescription items were sold against.</summary>
    public string? PrescriptionReference { get; set; }

    public void AddTender(SaleTender tender) => _tenders.Add(tender);

    /// <summary>Voids the sale.</summary>
    public void Void() => Status = SaleStatus.Voided;
}

/// <summary>A line of a POS <see cref="Sale"/> (<c>pos.sale_line</c>). Amounts are base currency.</summary>
public sealed class SaleLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SaleLine()
    {
        Description = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>The company, as on the sale: the line table is partitioned by year and read by company without a join.</summary>
    public long CompanyId { get; private set; }

    /// <summary>The sale date, as on the sale: the partition key of the line table.</summary>
    public DateOnly SaleDate { get; private set; }

    /// <summary>Stamps the sale's company and date on the line (called by the sale when the line is added).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="saleDate">The sale date.</param>
    internal void Stamp(long companyId, DateOnly saleDate)
    {
        CompanyId = companyId;
        SaleDate = saleDate;
    }

    /// <summary>Creates a sale line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="description">The description.</param>
    /// <param name="warehouseId">The warehouse issued from.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="lineNet">The net line value.</param>
    /// <param name="taxAmount">The tax on the line.</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    /// <param name="promotionDiscount">The promotion discount taken off the line.</param>
    /// <param name="discountAccountCode">The account the discount posts to.</param>
    /// <param name="promotionCode">The promotion(s) that gave it.</param>
    public SaleLine(long itemId, string description, long warehouseId, decimal quantity, decimal unitPrice, decimal lineNet, decimal taxAmount, string revenueAccountCode,
        decimal promotionDiscount = 0m, string? discountAccountCode = null, string? promotionCode = null, string? giftCardNumber = null, string? note = null, string? serialNumber = null, bool giftWrap = false)
    {
        GiftCardNumber = string.IsNullOrWhiteSpace(giftCardNumber) ? null : giftCardNumber.Trim().ToUpperInvariant();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim().ToUpperInvariant();
        GiftWrap = giftWrap;
        PromotionDiscount = promotionDiscount;
        DiscountAccountCode = discountAccountCode;
        PromotionCode = promotionCode;
        ItemId = itemId;
        Description = description;
        WarehouseId = warehouseId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineNet = lineNet;
        TaxAmount = taxAmount;
        RevenueAccountCode = revenueAccountCode;
    }

    /// <summary>The owning sale id.</summary>
    public long SaleId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The warehouse issued from.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>The quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The unit price.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>The net line value.</summary>
    public decimal LineNet { get; private set; }

    /// <summary>The tax on the line.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>The revenue account.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The promotion discount taken off the line (so revenue is gross and the discount analysable).</summary>
    public decimal PromotionDiscount { get; private set; }

    /// <summary>The account the discount posts to.</summary>
    public string? DiscountAccountCode { get; private set; }

    /// <summary>The promotion(s) that gave the discount.</summary>
    public string? PromotionCode { get; private set; }

    /// <summary>The gift card this line sold or topped up (its value went to the gift card liability), or null for goods.</summary>
    public string? GiftCardNumber { get; private set; }

    /// <summary>A note on the line (a kitchen instruction, an engraving, a customer's request).</summary>
    public string? Note { get; private set; }

    /// <summary>The serial number sold, for a serial-tracked item.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>Whether the line is to be gift-wrapped.</summary>
    public bool GiftWrap { get; private set; }
}

/// <summary>A tender of a POS <see cref="Sale"/> (<c>pos.sale_tender</c>).</summary>
public sealed class SaleTender : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SaleTender() => AccountCode = string.Empty;

    /// <summary>Creates a tender.</summary>
    /// <param name="tenderType">The tender type.</param>
    /// <param name="currency">The tender currency.</param>
    /// <param name="amount">The tender amount in its currency.</param>
    /// <param name="exchangeRate">The rate to base.</param>
    /// <param name="baseAmount">The base-currency value.</param>
    /// <param name="accountCode">The GL account for the tender.</param>
    /// <param name="isChange">True when this tender is change given (money out).</param>
    /// <param name="staffEmployeeId">The employee whose staff account is charged (a staff purchase).</param>
    public SaleTender(TenderType tenderType, CurrencyCode currency, decimal amount, decimal exchangeRate, decimal baseAmount, string accountCode, bool isChange, long? staffEmployeeId = null,
        string? voucherNumber = null)
    {
        VoucherNumber = voucherNumber;
        StaffEmployeeId = staffEmployeeId;
        TenderType = tenderType;
        Currency = currency;
        Amount = amount;
        ExchangeRate = exchangeRate;
        BaseAmount = baseAmount;
        AccountCode = accountCode;
        IsChange = isChange;
    }

    /// <summary>The owning sale id.</summary>
    public long SaleId { get; private set; }

    /// <summary>The tender type.</summary>
    public TenderType TenderType { get; private set; }

    /// <summary>The tender currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The tender amount in its currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The rate to base.</summary>
    public decimal ExchangeRate { get; private set; }

    /// <summary>The base-currency value.</summary>
    public decimal BaseAmount { get; private set; }

    /// <summary>The GL account for the tender.</summary>
    public string AccountCode { get; private set; }

    /// <summary>True when this tender is change given (money out).</summary>
    public bool IsChange { get; private set; }

    /// <summary>The employee whose staff account is charged, for a staff purchase (recovered through payroll).</summary>
    public long? StaffEmployeeId { get; private set; }

    /// <summary>For a voucher tender: the gift card it was paid from.</summary>
    public string? VoucherNumber { get; private set; }
}
