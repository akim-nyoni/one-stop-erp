using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Where a property's service stands (brief §4.9: connected, suspended, disconnected, closed).</summary>
public enum ServiceLinkStatus
{
    /// <summary>Connected and billed.</summary>
    Active = 0,

    /// <summary>Temporarily not billed (e.g. a vacant property on request).</summary>
    Suspended = 1,

    /// <summary>Cut off (e.g. for arrears); not billed until reconnected.</summary>
    Disconnected = 2,

    /// <summary>Ended for good.</summary>
    Closed = 3,
}

/// <summary>
/// A council's own service (<c>mun.service_type</c>, brief §4.9: "configurable service types"): a code and name billed like one of the built-in
/// categories, metered or not, with the deposit a new connection pays and the account it is held in.
/// </summary>
public sealed class MunicipalServiceType : Entity, ICompanyScoped
{
    private MunicipalServiceType()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Defines a service.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">Its code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="category">The built-in category it bills as.</param>
    public MunicipalServiceType(long companyId, string code, string name, ServiceType category)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Category = category;
        IsActive = true;
    }

    /// <summary>Gets the council.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets its code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets its name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the built-in category it bills as.</summary>
    public ServiceType Category { get; set; }

    /// <summary>Gets or sets a value indicating whether it is read from a meter.</summary>
    public bool IsMetered { get; set; }

    /// <summary>Gets or sets the deposit a new connection pays (0: none).</summary>
    public decimal DefaultDeposit { get; set; }

    /// <summary>Gets or sets the liability account deposits are held in.</summary>
    public string? DepositAccountCode { get; set; }

    /// <summary>Gets or sets a value indicating whether new connections may use it.</summary>
    public bool IsActive { get; set; }
}
