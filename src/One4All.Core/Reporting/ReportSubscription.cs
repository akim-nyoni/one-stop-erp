using One4All.Core.Common;

namespace One4All.Core.Reporting;

/// <summary>
/// A report subscription (<c>rpt.report_subscription</c>, brief §13.3 scheduling): a report, its stored parameters (which
/// may use relative date tokens), the output format, and the distribution list it is emailed to whenever it runs.
/// </summary>
public sealed class ReportSubscription : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReportSubscription()
    {
        Code = string.Empty;
        ReportCode = string.Empty;
        ParametersJson = "{}";
        Recipients = [];
    }

    /// <summary>Creates a subscription.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The subscription code.</param>
    /// <param name="reportCode">The report.</param>
    /// <param name="format">The output format (the report runner's format number).</param>
    /// <param name="parametersJson">The stored parameters, as a JSON object of strings.</param>
    /// <param name="recipients">The distribution list.</param>
    public ReportSubscription(long companyId, string code, string reportCode, int format, string parametersJson, List<string> recipients)
    {
        CompanyId = companyId;
        Code = code;
        ReportCode = reportCode;
        Format = format;
        ParametersJson = parametersJson;
        Recipients = recipients;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The subscription code.</summary>
    public string Code { get; private set; }

    /// <summary>The report.</summary>
    public string ReportCode { get; private set; }

    /// <summary>The output format.</summary>
    public int Format { get; private set; }

    /// <summary>The stored parameters.</summary>
    public string ParametersJson { get; private set; }

    /// <summary>The distribution list.</summary>
    public List<string> Recipients { get; private set; }

    /// <summary>Whether it runs.</summary>
    public bool IsActive { get; private set; }

    /// <summary>When set, each run is split by this field and each part sent to its own recipients (bursting) instead of the distribution list.</summary>
    public string? BurstKeyField { get; set; }

    /// <summary>For bursting: the field of the rows holding each part's email address.</summary>
    public string? BurstRecipientField { get; set; }

    /// <summary>For bursting: the key is a customer (1) or supplier (2) code, sent to the trader's contacts.</summary>
    public int? BurstLedger { get; set; }

    /// <summary>A folder (under the configured delivery root) each run is also saved to; null: email only.</summary>
    public string? DeliveryFolder { get; set; }

    /// <summary>Gets or sets the printer each run is printed on.</summary>
    public string? DeliveryPrinter { get; set; }

    /// <summary>Starts or stops it.</summary>
    /// <param name="active">Whether it runs.</param>
    public void SetActive(bool active) => IsActive = active;
}

/// <summary>One delivery of a subscription's report (<c>rpt.report_delivery</c>) - the delivery log.</summary>
public sealed class ReportDelivery : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReportDelivery()
    {
        Recipient = string.Empty;
        FileName = string.Empty;
        Detail = string.Empty;
    }

    /// <summary>Records a delivery.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="subscriptionId">The subscription.</param>
    /// <param name="runAt">When it ran.</param>
    /// <param name="recipient">Who it went to.</param>
    /// <param name="fileName">The document sent.</param>
    /// <param name="succeeded">Whether it was handed over for delivery.</param>
    /// <param name="detail">The message reference, or why it failed.</param>
    public ReportDelivery(long companyId, long subscriptionId, DateTimeOffset runAt, string recipient, string fileName, bool succeeded, string detail)
    {
        CompanyId = companyId;
        SubscriptionId = subscriptionId;
        RunAt = runAt;
        Recipient = recipient;
        FileName = fileName;
        Succeeded = succeeded;
        Detail = detail;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The subscription.</summary>
    public long SubscriptionId { get; private set; }

    /// <summary>When it ran.</summary>
    public DateTimeOffset RunAt { get; private set; }

    /// <summary>Who it went to.</summary>
    public string Recipient { get; private set; }

    /// <summary>The document sent.</summary>
    public string FileName { get; private set; }

    /// <summary>Whether it was handed over for delivery.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>The message reference, or why it failed.</summary>
    public string Detail { get; private set; }
}
