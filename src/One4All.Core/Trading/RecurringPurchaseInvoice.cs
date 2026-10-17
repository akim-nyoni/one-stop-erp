using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// A recurring purchase-invoice template (<c>ap.recurring_invoice</c>): standing instructions to raise a
/// periodic AP invoice from a supplier (rent, subscriptions, service contracts). It holds no ledger balance -
/// a billing run reads the due templates and posts real supplier invoices, then advances the next run.
/// </summary>
public sealed class RecurringPurchaseInvoice : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RecurringPurchaseInvoice()
    {
        Reference = string.Empty;
        ExpenseAccountCode = string.Empty;
    }

    /// <summary>Creates a recurring purchase-invoice template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier billed from.</param>
    /// <param name="reference">The template reference (also the invoice-number stem).</param>
    /// <param name="currency">The billing currency.</param>
    /// <param name="netAmount">The net amount each period.</param>
    /// <param name="taxAmount">The tax amount each period.</param>
    /// <param name="expenseAccountCode">The expense account debited.</param>
    /// <param name="taxAccountCode">The input-tax account, if tax applies.</param>
    /// <param name="intervalMonths">The billing interval in months (positive).</param>
    /// <param name="nextRunDate">The date the next invoice falls due.</param>
    /// <param name="narrative">An optional narrative for each invoice.</param>
    public RecurringPurchaseInvoice(long companyId, long supplierId, string reference, CurrencyCode currency, decimal netAmount, decimal taxAmount, string expenseAccountCode, string? taxAccountCode, int intervalMonths, DateOnly nextRunDate, string? narrative)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        Reference = reference;
        Currency = currency;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        ExpenseAccountCode = expenseAccountCode;
        TaxAccountCode = taxAccountCode;
        IntervalMonths = intervalMonths;
        NextRunDate = nextRunDate;
        Narrative = narrative;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The supplier billed from.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The template reference / invoice-number stem.</summary>
    public string Reference { get; private set; }

    /// <summary>The billing currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The net amount each period.</summary>
    public decimal NetAmount { get; private set; }

    /// <summary>The tax amount each period.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>The expense account debited.</summary>
    public string ExpenseAccountCode { get; private set; }

    /// <summary>The input-tax account, if tax applies.</summary>
    public string? TaxAccountCode { get; private set; }

    /// <summary>The billing interval in months.</summary>
    public int IntervalMonths { get; private set; }

    /// <summary>The date the next invoice falls due.</summary>
    public DateOnly NextRunDate { get; private set; }

    /// <summary>An optional narrative for each invoice.</summary>
    public string? Narrative { get; private set; }

    /// <summary>Whether the template is active (billed on a run).</summary>
    public bool IsActive { get; private set; }

    /// <summary>Advances the next run date by one interval after billing a period.</summary>
    public void AdvanceRun() => NextRunDate = NextRunDate.AddMonths(IntervalMonths);

    /// <summary>Activates or suspends the template.</summary>
    /// <param name="active">Whether the template should be active.</param>
    public void SetActive(bool active) => IsActive = active;
}
