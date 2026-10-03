using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>
/// An owner's share of a property for a period (<c>mun.property_owner</c>, brief §4.9 owner register): joint owners hold
/// shares adding up to 100%, one of them is the ratepayer billed for the property, and a transfer closes the sellers'
/// shares and opens the buyers'. Rows are never deleted, so the register keeps the ownership history.
/// </summary>
public sealed class PropertyOwner : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PropertyOwner()
    {
        Reference = string.Empty;
    }

    /// <summary>Records an owner's share.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="customerId">The owner's customer account.</param>
    /// <param name="sharePercent">The share held.</param>
    /// <param name="isRatepayer">Whether this owner is billed for the property.</param>
    /// <param name="ownedFrom">The first day of ownership.</param>
    /// <param name="reference">The clearance certificate the transfer went through, or the register entry.</param>
    /// <param name="deedsReference">The title deed reference.</param>
    public PropertyOwner(long companyId, long propertyId, long customerId, decimal sharePercent, bool isRatepayer, DateOnly ownedFrom, string reference, string? deedsReference)
    {
        CompanyId = companyId;
        PropertyId = propertyId;
        CustomerId = customerId;
        SharePercent = sharePercent;
        IsRatepayer = isRatepayer;
        OwnedFrom = ownedFrom;
        Reference = reference;
        DeedsReference = deedsReference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The owner's customer account.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The share held.</summary>
    public decimal SharePercent { get; private set; }

    /// <summary>Whether this owner is billed for the property.</summary>
    public bool IsRatepayer { get; private set; }

    /// <summary>The first day of ownership.</summary>
    public DateOnly OwnedFrom { get; private set; }

    /// <summary>The last day of ownership, once transferred.</summary>
    public DateOnly? OwnedTo { get; private set; }

    /// <summary>The clearance certificate the transfer went through, or the register entry.</summary>
    public string Reference { get; private set; }

    /// <summary>The title deed reference.</summary>
    public string? DeedsReference { get; private set; }

    /// <summary>Ends the ownership.</summary>
    /// <param name="lastDay">The last day owned.</param>
    public void End(DateOnly lastDay) => OwnedTo = lastDay;
}
