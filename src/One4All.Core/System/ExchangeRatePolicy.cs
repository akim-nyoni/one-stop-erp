using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>
/// Which exchange-rate type a kind of document uses (<c>sys.exchange_rate_policy</c>, brief §3.6: "rate type per document type / module"): keyed by a
/// document type (e.g. <c>SalesInvoice</c>) or a whole ledger (<c>AR</c>, <c>AP</c>); a trader's own rate type still comes first.
/// </summary>
public sealed class ExchangeRatePolicy : Entity, ICompanyScoped
{
    private ExchangeRatePolicy()
    {
        DocumentType = string.Empty;
    }

    /// <summary>Sets the policy.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document type or ledger.</param>
    /// <param name="rateType">The rate type it uses.</param>
    public ExchangeRatePolicy(long companyId, string documentType, ExchangeRateType rateType)
    {
        CompanyId = companyId;
        DocumentType = documentType;
        RateType = rateType;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the document type or ledger.</summary>
    public string DocumentType { get; private set; }

    /// <summary>Gets or sets the rate type it uses.</summary>
    public ExchangeRateType RateType { get; set; }
}
