using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// A recurring sales-invoice template (<c>ar.recurring_invoice</c>): standing instructions to raise a periodic
/// AR invoice for a customer (subscriptions, retainers, rent). It holds no ledger balance itself — a billing
/// run reads the due templates and posts real invoices through the sales ledger, then advances the next run.
/// </summary>
public sealed class RecurringSalesInvoice : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RecurringSalesInvoice()
    {
        Reference = string.Empty;
        SalesAccountCode = string.Empty;
    }

    /// <summary>Creates a recurring invoice template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer billed.</param>
    /// <param name="reference">The template reference (also the invoice-number stem).</param>
    /// <param name="currency">The billing currency.</param>
    /// <param name="netAmount">The net amount billed each period.</param>
    /// <param name="taxAmount">The tax amount billed each period.</param>
    /// <param name="salesAccountCode">The revenue account credited.</param>
    /// <param name="taxAccountCode">The output-tax account, if tax applies.</param>
    /// <param name="intervalMonths">The billing interval in months (positive).</param>
    /// <param name="nextRunDate">The date the next invoice falls due.</param>
    /// <param name="narrative">An optional narrative for each invoice.</param>
    public RecurringSalesInvoice(long companyId, long customerId, string reference, CurrencyCode currency, decimal netAmount, decimal taxAmount, string salesAccountCode, string? taxAccountCode, int intervalMonths, DateOnly nextRunDate, string? narrative)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        Reference = reference;
        Currency = currency;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        SalesAccountCode = salesAccountCode;
        TaxAccountCode = taxAccountCode;
        IntervalMonths = intervalMonths;
        NextRunDate = nextRunDate;
        Narrative = narrative;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer billed.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The template reference / invoice-number stem.</summary>
    public string Reference { get; private set; }

    /// <summary>The billing currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The net amount billed each period.</summary>
    public decimal NetAmount { get; private set; }

    /// <summary>The tax amount billed each period.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>The revenue account credited.</summary>
    public string SalesAccountCode { get; private set; }

    /// <summary>The output-tax account, if tax applies.</summary>
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
