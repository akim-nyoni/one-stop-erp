using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Pos;

namespace One4All.Application.Api;

/// <summary>A customer as the REST API creates or changes it (brief §11: master-data CRUD). Currency and codes are plain text.</summary>
/// <param name="Code">The customer code (create).</param>
/// <param name="Name">The name.</param>
/// <param name="Currency">The ISO currency (create).</param>
/// <param name="CreditLimit">The credit limit (create).</param>
/// <param name="Email">The e-mail address.</param>
/// <param name="MobileNumber">The mobile number.</param>
/// <param name="TaxRegistration">The tax registration number.</param>
/// <param name="OnHold">Whether the account is on hold (change).</param>
/// <param name="DefaultTaxCode">The default tax code.</param>
public sealed record CustomerApi(string Code, string Name, string Currency, decimal CreditLimit = 0m, string? Email = null, string? MobileNumber = null, string? TaxRegistration = null, bool OnHold = false,
    string? DefaultTaxCode = null);

/// <summary>A supplier as the REST API creates or changes it.</summary>
/// <param name="Code">The supplier code (create).</param>
/// <param name="Name">The name.</param>
/// <param name="Currency">The ISO currency (create).</param>
/// <param name="TaxRegistration">The tax registration number.</param>
/// <param name="OnHold">Whether the account is on hold.</param>
/// <param name="DefaultTaxCode">The default tax code.</param>
public sealed record SupplierApi(string Code, string Name, string Currency, string? TaxRegistration = null, bool OnHold = false, string? DefaultTaxCode = null);

/// <summary>An item as the REST API creates or changes it.</summary>
/// <param name="Code">The item code (create).</param>
/// <param name="Description">The description.</param>
/// <param name="Type">Stock, NonStock, Service or Kit (create).</param>
/// <param name="RevenueAccountCode">The revenue account.</param>
/// <param name="CostOfSalesAccountCode">The cost of sales account.</param>
/// <param name="Category">The item category.</param>
/// <param name="TaxCode">The tax code.</param>
/// <param name="Costing">WeightedAverage, Fifo, Standard or SpecificIdentification (create; blank takes the category's or the default).</param>
/// <param name="StandardCost">The standard cost (standard costing).</param>
/// <param name="PriceVarianceAccountCode">The price variance account (standard costing).</param>
/// <param name="IsActive">Whether the item is active (change).</param>
public sealed record ItemApi(string Code, string Description, string Type = "Stock", string? RevenueAccountCode = null, string? CostOfSalesAccountCode = null, string? Category = null, string? TaxCode = null,
    string? Costing = null, decimal? StandardCost = null, string? PriceVarianceAccountCode = null, bool IsActive = true);

/// <summary>A price on a price list as the REST API sets it.</summary>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="MinimumQuantity">The quantity from which it applies (1 for the base price).</param>
/// <param name="EffectiveFrom">A future date the price takes effect, or null for now.</param>
public sealed record PriceApi(decimal UnitPrice, decimal MinimumQuantity = 1m, DateOnly? EffectiveFrom = null);

/// <summary>A sales invoice or credit note as the REST API posts it.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="DocumentNumber">The document number.</param>
/// <param name="DocumentDate">The document date.</param>
/// <param name="Currency">The ISO currency.</param>
/// <param name="NetAmount">The net amount.</param>
/// <param name="SalesAccountCode">The revenue account.</param>
/// <param name="TaxAmount">The tax amount (0 with a tax code: the code's rate applies).</param>
/// <param name="TaxAccountCode">The tax account, when tax is given.</param>
/// <param name="TaxCode">The tax code.</param>
/// <param name="ExchangeRate">A keyed rate, when allowed.</param>
/// <param name="Narrative">The narrative.</param>
/// <param name="CostCentre">The cost centre.</param>
/// <param name="Department">The department.</param>
/// <param name="ReasonCode">The reason code (credit note).</param>
public sealed record SalesDocumentApi(long CustomerId, string DocumentNumber, DateOnly DocumentDate, string Currency, decimal NetAmount, string SalesAccountCode, decimal TaxAmount = 0m, string? TaxAccountCode = null,
    string? TaxCode = null, decimal? ExchangeRate = null, string? Narrative = null, string? CostCentre = null, string? Department = null, string? ReasonCode = null);

/// <summary>A customer receipt as the REST API posts it.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="DocumentNumber">The receipt number.</param>
/// <param name="ReceiptDate">The receipt date.</param>
/// <param name="Currency">The ISO currency.</param>
/// <param name="Amount">The amount received.</param>
/// <param name="BankAccountCode">The bank or cash account.</param>
/// <param name="ExchangeRate">A keyed rate, when allowed.</param>
/// <param name="Narrative">The narrative.</param>
public sealed record ReceiptApi(long CustomerId, string DocumentNumber, DateOnly ReceiptDate, string Currency, decimal Amount, string BankAccountCode, decimal? ExchangeRate = null, string? Narrative = null);

/// <summary>A supplier invoice or credit note as the REST API posts it.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="DocumentNumber">The supplier's document number.</param>
/// <param name="DocumentDate">The document date.</param>
/// <param name="Currency">The ISO currency.</param>
/// <param name="NetAmount">The net amount.</param>
/// <param name="ExpenseAccountCode">The expense account.</param>
/// <param name="TaxAmount">The tax amount (0 with a tax code: the code's rate applies).</param>
/// <param name="TaxAccountCode">The tax account, when tax is given.</param>
/// <param name="TaxCode">The tax code.</param>
/// <param name="ExchangeRate">A keyed rate, when allowed.</param>
/// <param name="Narrative">The narrative.</param>
/// <param name="CostCentre">The cost centre.</param>
/// <param name="Department">The department.</param>
public sealed record PurchaseDocumentApi(long SupplierId, string DocumentNumber, DateOnly DocumentDate, string Currency, decimal NetAmount, string ExpenseAccountCode, decimal TaxAmount = 0m, string? TaxAccountCode = null,
    string? TaxCode = null, decimal? ExchangeRate = null, string? Narrative = null, string? CostCentre = null, string? Department = null);

/// <summary>A supplier payment as the REST API posts it.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="DocumentNumber">The payment number.</param>
/// <param name="PaymentDate">The payment date.</param>
/// <param name="Currency">The ISO currency.</param>
/// <param name="Amount">The amount paid.</param>
/// <param name="BankAccountCode">The bank account.</param>
/// <param name="ExchangeRate">A keyed rate, when allowed.</param>
/// <param name="Narrative">The narrative.</param>
/// <param name="WithholdingTaxCode">The withholding tax code, when tax is withheld.</param>
public sealed record SupplierPaymentApi(long SupplierId, string DocumentNumber, DateOnly PaymentDate, string Currency, decimal Amount, string BankAccountCode, decimal? ExchangeRate = null, string? Narrative = null,
    string? WithholdingTaxCode = null);

/// <summary>A line of a till sale as the REST API posts it.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Description">The description.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price (net).</param>
/// <param name="LineNet">The line net.</param>
/// <param name="TaxAmount">The tax on the line.</param>
/// <param name="RevenueAccountCode">The revenue account.</param>
/// <param name="Note">A note on the line.</param>
/// <param name="SerialNumber">The serial number, for a serial-tracked item.</param>
/// <param name="GiftWrap">Whether to gift-wrap.</param>
public sealed record PosSaleLineApi(long ItemId, string Description, decimal Quantity, decimal UnitPrice, decimal LineNet, decimal TaxAmount, string RevenueAccountCode, string? Note = null, string? SerialNumber = null,
    bool GiftWrap = false);

/// <summary>A tender of a till sale as the REST API posts it.</summary>
/// <param name="TenderType">Cash, Card, MobileMoney, Voucher, Account…</param>
/// <param name="Currency">The ISO currency.</param>
/// <param name="Amount">The amount.</param>
/// <param name="AccountCode">The ledger account the tender posts to.</param>
/// <param name="IsChange">Whether this is change given back.</param>
public sealed record PosTenderApi(string TenderType, string Currency, decimal Amount, string AccountCode, bool IsChange = false);

/// <summary>A till sale as the REST API posts it (a self-service kiosk, an e-commerce front, an integration).</summary>
/// <param name="TerminalId">The terminal.</param>
/// <param name="TillSessionId">The open till session.</param>
/// <param name="CashierUserId">The cashier.</param>
/// <param name="SaleNumber">The sale number.</param>
/// <param name="SaleDate">The sale date.</param>
/// <param name="WarehouseId">The warehouse stock leaves.</param>
/// <param name="Lines">The lines.</param>
/// <param name="Tenders">The tenders.</param>
/// <param name="TaxAccountCode">The output tax account.</param>
/// <param name="CustomerId">The account customer, when any.</param>
public sealed record PosSaleApi(long TerminalId, long TillSessionId, long CashierUserId, string SaleNumber, DateOnly SaleDate, long WarehouseId, IReadOnlyList<PosSaleLineApi> Lines, IReadOnlyList<PosTenderApi> Tenders,
    string? TaxAccountCode = null, long? CustomerId = null);

/// <summary>Turns the REST API's documents into the application's requests (currencies and enums parsed, the company stamped on).</summary>
public static class TradingApiMapping
{
    /// <summary>Parses an ISO currency code.</summary>
    /// <param name="text">The code.</param>
    /// <returns>The currency, or a validation error.</returns>
    public static Result<CurrencyCode> Currency(string? text)
        => CurrencyCode.TryParse(text, out CurrencyCode code) ? code : Error.Validation("Api.Currency", $"'{text}' is not an ISO currency code.");

    /// <summary>A sales invoice request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The document as posted.</param>
    /// <returns>The request, or a validation error.</returns>
    public static Result<SalesInvoiceRequest> SalesInvoice(long companyId, SalesDocumentApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<CurrencyCode> currency = Currency(api.Currency);
        return currency.IsFailure ? currency.Error : new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = api.CustomerId, DocumentNumber = api.DocumentNumber, DocumentDate = api.DocumentDate, Currency = currency.Value, NetAmount = api.NetAmount, TaxAmount = api.TaxAmount,
            SalesAccountCode = api.SalesAccountCode, TaxAccountCode = api.TaxAccountCode, TaxCode = api.TaxCode, ExchangeRate = api.ExchangeRate, Narrative = api.Narrative, CostCentre = api.CostCentre, Department = api.Department,
        };
    }

    /// <summary>A sales credit note request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The document as posted.</param>
    /// <returns>The request, or a validation error.</returns>
    public static Result<SalesCreditNoteRequest> SalesCreditNote(long companyId, SalesDocumentApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<CurrencyCode> currency = Currency(api.Currency);
        return currency.IsFailure ? currency.Error : new SalesCreditNoteRequest
        {
            CompanyId = companyId, CustomerId = api.CustomerId, DocumentNumber = api.DocumentNumber, DocumentDate = api.DocumentDate, Currency = currency.Value, NetAmount = api.NetAmount, TaxAmount = api.TaxAmount,
            SalesAccountCode = api.SalesAccountCode, TaxAccountCode = api.TaxAccountCode, ExchangeRate = api.ExchangeRate, Narrative = api.Narrative, CostCentre = api.CostCentre, Department = api.Department, ReasonCode = api.ReasonCode,
        };
    }

    /// <summary>A customer receipt request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The receipt as posted.</param>
    /// <returns>The request, or a validation error.</returns>
    public static Result<ReceiptRequest> Receipt(long companyId, ReceiptApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<CurrencyCode> currency = Currency(api.Currency);
        return currency.IsFailure ? currency.Error : new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = api.CustomerId, DocumentNumber = api.DocumentNumber, ReceiptDate = api.ReceiptDate, Currency = currency.Value, Amount = api.Amount, BankAccountCode = api.BankAccountCode,
            ExchangeRate = api.ExchangeRate, Narrative = api.Narrative,
        };
    }

    /// <summary>A purchase invoice request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The document as posted.</param>
    /// <returns>The request, or a validation error.</returns>
    public static Result<PurchaseInvoiceRequest> PurchaseInvoice(long companyId, PurchaseDocumentApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<CurrencyCode> currency = Currency(api.Currency);
        return currency.IsFailure ? currency.Error : new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = api.SupplierId, DocumentNumber = api.DocumentNumber, DocumentDate = api.DocumentDate, Currency = currency.Value, NetAmount = api.NetAmount, TaxAmount = api.TaxAmount,
            ExpenseAccountCode = api.ExpenseAccountCode, TaxAccountCode = api.TaxAccountCode, TaxCode = api.TaxCode, ExchangeRate = api.ExchangeRate, Narrative = api.Narrative, CostCentre = api.CostCentre, Department = api.Department,
        };
    }

    /// <summary>A purchase credit note request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The document as posted.</param>
    /// <returns>The request, or a validation error.</returns>
    public static Result<PurchaseCreditNoteRequest> PurchaseCreditNote(long companyId, PurchaseDocumentApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<CurrencyCode> currency = Currency(api.Currency);
        return currency.IsFailure ? currency.Error : new PurchaseCreditNoteRequest
        {
            CompanyId = companyId, SupplierId = api.SupplierId, DocumentNumber = api.DocumentNumber, DocumentDate = api.DocumentDate, Currency = currency.Value, NetAmount = api.NetAmount, TaxAmount = api.TaxAmount,
            ExpenseAccountCode = api.ExpenseAccountCode, TaxAccountCode = api.TaxAccountCode, ExchangeRate = api.ExchangeRate, Narrative = api.Narrative, CostCentre = api.CostCentre, Department = api.Department,
        };
    }

    /// <summary>A supplier payment request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The payment as posted.</param>
    /// <returns>The request, or a validation error.</returns>
    public static Result<PaymentRequest> SupplierPayment(long companyId, SupplierPaymentApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<CurrencyCode> currency = Currency(api.Currency);
        return currency.IsFailure ? currency.Error : new PaymentRequest
        {
            CompanyId = companyId, SupplierId = api.SupplierId, DocumentNumber = api.DocumentNumber, PaymentDate = api.PaymentDate, Currency = currency.Value, Amount = api.Amount, BankAccountCode = api.BankAccountCode,
            ExchangeRate = api.ExchangeRate, Narrative = api.Narrative, WithholdingTaxCode = api.WithholdingTaxCode,
        };
    }

    /// <summary>A till sale request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The sale as posted.</param>
    /// <returns>The request, or a validation error.</returns>
    public static Result<CompleteSaleRequest> PosSale(long companyId, PosSaleApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (api.Lines is null || api.Lines.Count == 0 || api.Tenders is null || api.Tenders.Count == 0)
        {
            return Error.Validation("Api.Sale", "A sale needs at least one line and one tender.");
        }

        List<PosTenderRequest> tenders = [];
        foreach (PosTenderApi tender in api.Tenders)
        {
            if (!Enum.TryParse(tender.TenderType, ignoreCase: true, out TenderType type))
            {
                return Error.Validation("Api.TenderType", $"'{tender.TenderType}' is not a tender type ({string.Join(", ", Enum.GetNames<TenderType>())}).");
            }

            Result<CurrencyCode> currency = Currency(tender.Currency);
            if (currency.IsFailure)
            {
                return currency.Error;
            }

            tenders.Add(new PosTenderRequest(type, currency.Value, tender.Amount, tender.AccountCode, tender.IsChange));
        }

        return new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = api.TerminalId, TillSessionId = api.TillSessionId, CashierUserId = api.CashierUserId, SaleNumber = api.SaleNumber, SaleDate = api.SaleDate, WarehouseId = api.WarehouseId,
            CustomerId = api.CustomerId, TaxAccountCode = api.TaxAccountCode,
            Lines = [.. api.Lines.Select(l => new PosSaleLineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.LineNet, l.TaxAmount, l.RevenueAccountCode, Note: l.Note, SerialNumber: l.SerialNumber, GiftWrap: l.GiftWrap))],
            Tenders = tenders,
        };
    }
}
