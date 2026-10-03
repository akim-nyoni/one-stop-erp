using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>
/// A prepaid token sale (<c>mun.prepaid_sale</c>, brief §4.9 prepaid hooks): the amount tendered, the part taken towards
/// the account's arrears, the units bought and the token issued.
/// </summary>
public sealed class PrepaidSale : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PrepaidSale()
    {
        SaleNumber = string.Empty;
        MeterSerial = string.Empty;
        Token = string.Empty;
        Channel = string.Empty;
    }

    /// <summary>Records a sale.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="saleNumber">The sale number.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="serviceType">The service bought.</param>
    /// <param name="meterSerial">The meter the token is for.</param>
    /// <param name="soldOn">The sale date.</param>
    /// <param name="amount">The amount tendered.</param>
    /// <param name="toArrears">The part taken towards arrears.</param>
    /// <param name="units">The units bought.</param>
    /// <param name="token">The token issued.</param>
    /// <param name="channel">Where it was sold (counter, vendor, mobile).</param>
    public PrepaidSale(long companyId, string saleNumber, long propertyId, ServiceType serviceType, string meterSerial, DateOnly soldOn, decimal amount, decimal toArrears, decimal units, string token, string channel)
    {
        CompanyId = companyId;
        SaleNumber = saleNumber;
        PropertyId = propertyId;
        ServiceType = serviceType;
        MeterSerial = meterSerial;
        SoldOn = soldOn;
        Amount = amount;
        ToArrears = toArrears;
        Units = units;
        Token = token;
        Channel = channel;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The sale number.</summary>
    public string SaleNumber { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The service bought.</summary>
    public ServiceType ServiceType { get; private set; }

    /// <summary>The meter the token is for.</summary>
    public string MeterSerial { get; private set; }

    /// <summary>The sale date.</summary>
    public DateOnly SoldOn { get; private set; }

    /// <summary>The amount tendered.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The part taken towards arrears.</summary>
    public decimal ToArrears { get; private set; }

    /// <summary>The units bought.</summary>
    public decimal Units { get; private set; }

    /// <summary>The token issued.</summary>
    public string Token { get; private set; }

    /// <summary>Where it was sold.</summary>
    public string Channel { get; private set; }
}
