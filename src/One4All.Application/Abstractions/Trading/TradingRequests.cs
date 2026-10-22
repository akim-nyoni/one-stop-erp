using One4All.Core.Accounting;

namespace One4All.Application.Abstractions.Trading;

/// <summary>The result of posting a trading document.</summary>
/// <param name="OpenItemId">The created open-item id.</param>
/// <param name="JournalNumber">The GL journal number.</param>
/// <param name="BaseAmount">The gross amount in base currency.</param>
/// <param name="WithheldBase">Withholding tax deducted from a supplier payment (base currency; 0 when none).</param>
public readonly record struct TradingPostingResult(long OpenItemId, string JournalNumber, decimal BaseAmount, decimal WithheldBase = 0m)
{
    /// <summary>Gets a value indicating whether the invoice took the customer over the credit limit under a warn policy.</summary>
    public bool OverCreditLimit { get; init; }
}

/// <summary>A request to post a sales invoice (AR).</summary>
public sealed record SalesInvoiceRequest : ITradingAnalysis
{
    /// <summary>Gets the cost centre the revenue or expense posts to.</summary>
    public string? CostCentre { get; init; }

    /// <summary>Gets the department the revenue or expense posts to.</summary>
    public string? Department { get; init; }

    /// <summary>Gets the project the revenue or expense posts to.</summary>
    public string? Project { get; init; }

    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; init; }

    /// <summary>The invoice number (blank: the next number from the company's "SINV" sequence, "SDN" for a debit note). A number already used by another invoice is refused.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>The invoice date.</summary>
    public DateOnly DocumentDate { get; init; }

    /// <summary>The transaction currency.</summary>
    public CurrencyCode Currency { get; init; }

    /// <summary>The net (tax-exclusive) amount.</summary>
    public decimal NetAmount { get; init; }

    /// <summary>The tax amount.</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>The supervisor allowing this invoice over the customer's credit limit (needs Sales › Customers credit-limit override).</summary>
    public long? CreditLimitOverrideByUserId { get; init; }

    /// <summary>The user raising it (refused as their own credit-limit override; checked for back-dating).</summary>
    public long? EnteredByUserId { get; init; }

    /// <summary>The revenue account code (credited) - used when <see cref="RevenueLines"/> is empty.</summary>
    public string SalesAccountCode { get; init; } = string.Empty;

    /// <summary>
    /// Optional split of the net amount across revenue accounts (e.g. an order whose items post to different revenue
    /// accounts). When given, the amounts must add up to <see cref="NetAmount"/>, and each account is credited its share.
    /// </summary>
    public IReadOnlyList<RevenueLine> RevenueLines { get; init; } = [];

    /// <summary>The output-tax account code (credited), when there is tax.</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>The tax code (e.g. "VAT15") tagged on the tax line for the VAT breakdown by rate/code.</summary>
    public string? TaxCode { get; init; }

    /// <summary>Optional explicit exchange rate; otherwise the daily rate is used.</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Who keyed <see cref="ExchangeRate"/> (needs System › Exchange Rates override when the company controls rate overrides).</summary>
    public long? RateOverrideByUserId { get; init; }

    /// <summary>Optional narrative.</summary>
    /// <summary>Gets when the goods or service were delivered (sets the tax point; null: the invoice date).</summary>
    public DateOnly? DeliveryDate { get; init; }

    public string? Narrative { get; init; }
}

/// <summary>One revenue account's share of a sales invoice's net amount.</summary>
/// <param name="AccountCode">The revenue account (credited).</param>
/// <param name="NetAmount">Its net amount.</param>
/// <param name="CostCentre">The cost centre.</param>
/// <param name="Department">The department.</param>
/// <param name="TaxCode">
/// The line's own tax code (brief §3.5 mixed-rate invoices): when lines carry codes, every line needs one, each code's tax is worked out on its lines and
/// posted to its own accounts, and the invoice's tax is their total.
/// </param>
public readonly record struct RevenueLine(string AccountCode, decimal NetAmount, string? CostCentre = null, string? Department = null, string? TaxCode = null);

/// <summary>A request to post a receipt against a customer (AR).</summary>
public sealed class ReceiptRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; init; }

    /// <summary>The receipt number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>The receipt date.</summary>
    public DateOnly ReceiptDate { get; init; }

    /// <summary>The transaction currency.</summary>
    public CurrencyCode Currency { get; init; }

    /// <summary>The amount received.</summary>
    public decimal Amount { get; init; }

    /// <summary>The bank/cash account code (debited).</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>Optional explicit exchange rate.</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Who keyed <see cref="ExchangeRate"/> (needs System › Exchange Rates override when the company controls rate overrides).</summary>
    public long? RateOverrideByUserId { get; init; }

    /// <summary>Optional narrative.</summary>
    public string? Narrative { get; init; }
}

/// <summary>A request to post a supplier invoice (AP).</summary>
public sealed record PurchaseInvoiceRequest : ITradingAnalysis
{
    /// <summary>Gets the cost centre the revenue or expense posts to.</summary>
    public string? CostCentre { get; init; }

    /// <summary>Gets the department the revenue or expense posts to.</summary>
    public string? Department { get; init; }

    /// <summary>Gets the project the revenue or expense posts to.</summary>
    public string? Project { get; init; }

    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; init; }

    /// <summary>The invoice number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>The invoice date.</summary>
    public DateOnly DocumentDate { get; init; }

    /// <summary>The transaction currency.</summary>
    public CurrencyCode Currency { get; init; }

    /// <summary>The net amount.</summary>
    public decimal NetAmount { get; init; }

    /// <summary>The tax amount.</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>The expense/stock account code (debited).</summary>
    public string ExpenseAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the expense lines when the invoice spreads over several accounts (they add up to the net; each may carry its own cost centre and department).</summary>
    public IReadOnlyList<RevenueLine> ExpenseLines { get; init; } = [];

    /// <summary>The input-tax account code (debited), when there is recoverable tax.</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>The tax code (e.g. "VAT15") tagged on the tax line for the VAT breakdown by rate/code.</summary>
    public string? TaxCode { get; init; }

    /// <summary>Optional explicit exchange rate.</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Who keyed <see cref="ExchangeRate"/> (needs System › Exchange Rates override when the company controls rate overrides).</summary>
    public long? RateOverrideByUserId { get; init; }

    /// <summary>Optional narrative.</summary>
    public string? Narrative { get; init; }

    /// <summary>The job the invoice is charged to (its net base amount is captured as job cost).</summary>
    public long? JobContractId { get; init; }

    /// <summary>The job cost head.</summary>
    public string? JobCostHead { get; init; }

    /// <summary>Gets the user approving a budget overrun, where the budget rule asks for approval rather than blocking.</summary>
    public long? BudgetApprovedByUserId { get; init; }
}

/// <summary>A request to post a credit note to a customer (AR) - reduces the debtor balance.</summary>
public sealed record SalesCreditNoteRequest : ITradingAnalysis
{
    /// <summary>Gets the cost centre the revenue or expense posts to.</summary>
    public string? CostCentre { get; init; }

    /// <summary>Gets the department the revenue or expense posts to.</summary>
    public string? Department { get; init; }

    /// <summary>Gets the project the revenue or expense posts to.</summary>
    public string? Project { get; init; }

    /// <summary>Gets why the credit was given (RETURN, PRICE, DAMAGED…), for the credit notes analysis.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; init; }

    /// <summary>The credit-note number (blank: the next number from the company's "SCN" sequence).</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>The document date.</summary>
    public DateOnly DocumentDate { get; init; }

    /// <summary>The transaction currency.</summary>
    public CurrencyCode Currency { get; init; }

    /// <summary>The net amount credited.</summary>
    public decimal NetAmount { get; init; }

    /// <summary>The tax amount credited.</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>The revenue/returns account code (debited).</summary>
    public string SalesAccountCode { get; init; } = string.Empty;

    /// <summary>The output-tax account code (debited), when there is tax.</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>
    /// The tax code (e.g. "VAT15") tagged on the tax line for the VAT breakdown by code; with no tax amount the tax is worked out at the code's rate
    /// on the document date, to the code's output account (else the VAT output control).
    /// </summary>
    public string? TaxCode { get; init; }

    /// <summary>
    /// The invoice being credited, when the credit note reverses one: it must be the same customer's, in the same currency, and the credit cannot take
    /// more than the invoice's gross less the credit notes already raised against it. The credit is allocated to the invoice and the fiscal device gets it as the reference.
    /// </summary>
    public string? OriginalInvoiceNumber { get; init; }

    /// <summary>Optional explicit exchange rate.</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Who keyed <see cref="ExchangeRate"/> (needs System › Exchange Rates override when the company controls rate overrides).</summary>
    public long? RateOverrideByUserId { get; init; }

    /// <summary>Optional narrative.</summary>
    public string? Narrative { get; init; }
}

/// <summary>A request to post a credit note from a supplier (AP) - reduces the creditor balance.</summary>
public sealed class PurchaseCreditNoteRequest : ITradingAnalysis
{
    /// <summary>Gets the cost centre the revenue or expense posts to.</summary>
    public string? CostCentre { get; init; }

    /// <summary>Gets the department the revenue or expense posts to.</summary>
    public string? Department { get; init; }

    /// <summary>Gets the project the revenue or expense posts to.</summary>
    public string? Project { get; init; }

    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; init; }

    /// <summary>The credit-note number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>The document date.</summary>
    public DateOnly DocumentDate { get; init; }

    /// <summary>The transaction currency.</summary>
    public CurrencyCode Currency { get; init; }

    /// <summary>The net amount.</summary>
    public decimal NetAmount { get; init; }

    /// <summary>The tax amount.</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>The expense/stock account code (credited).</summary>
    public string ExpenseAccountCode { get; init; } = string.Empty;

    /// <summary>The input-tax account code (credited), when there is tax.</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>Optional explicit exchange rate.</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Who keyed <see cref="ExchangeRate"/> (needs System › Exchange Rates override when the company controls rate overrides).</summary>
    public long? RateOverrideByUserId { get; init; }

    /// <summary>Optional narrative.</summary>
    public string? Narrative { get; init; }
}

/// <summary>A request to post a payment to a supplier (AP).</summary>
public sealed class PaymentRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; init; }

    /// <summary>The payment number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>The payment date.</summary>
    public DateOnly PaymentDate { get; init; }

    /// <summary>The transaction currency.</summary>
    public CurrencyCode Currency { get; init; }

    /// <summary>The amount paid.</summary>
    public decimal Amount { get; init; }

    /// <summary>The bank/cash account code (credited).</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>Optional explicit exchange rate.</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Who keyed <see cref="ExchangeRate"/> (needs System › Exchange Rates override when the company controls rate overrides).</summary>
    public long? RateOverrideByUserId { get; init; }

    /// <summary>Optional narrative.</summary>
    public string? Narrative { get; init; }

    /// <summary>
    /// A withholding tax code to apply (brief §3.5): when the supplier has no valid tax clearance on the payment date,
    /// its rate is withheld from the payment - the supplier's account is settled for the full amount, the bank pays
    /// the net and the tax is owed on the code's output (WHT payable) account. Ignored for a cleared supplier.
    /// </summary>
    public string? WithholdingTaxCode { get; init; }

    /// <summary>
    /// Set when no money leaves the business (a contra settled through the clearing account, a creditor write-back to income): nothing is withheld,
    /// since withholding tax is on cash paid to the supplier and holding it back would leave the clearing or income account short.
    /// </summary>
    public bool NoCashPaid { get; init; }

    /// <summary>
    /// The user who keys a single payment (the desktop's Supplier payment, the REST API): it is then held to the payment run's controls - refused for a
    /// supplier on hold or blacklisted or with a bank-detail change pending, and, where the company requires payment approval, it needs
    /// <see cref="ApprovedByUserId"/>. Null for payments posted from documents already controlled elsewhere (the payment run, a contra, a write-back,
    /// a bank statement line already paid, a standing order).
    /// </summary>
    public long? EnteredByUserId { get; init; }

    /// <summary>The user who approved a keyed single payment; where the company requires payment approval it must be someone other than <see cref="EnteredByUserId"/>.</summary>
    public long? ApprovedByUserId { get; init; }
}

/// <summary>
/// The analysis a trading document's revenue or expense lines post with (brief §3.1, §4.4): cost centre, department and project, so departmental
/// budgets and the departmental P&amp;L see invoices and credit notes, not only manual journals.
/// </summary>
public interface ITradingAnalysis
{
    /// <summary>Gets the cost centre.</summary>
    string? CostCentre { get; }

    /// <summary>Gets the department.</summary>
    string? Department { get; }

    /// <summary>Gets the project.</summary>
    string? Project { get; }
}
