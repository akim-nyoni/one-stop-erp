using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>What a party other than the owner is to a property.</summary>
public enum PropertyPartyRole
{
    /// <summary>A tenant renting the property from the owner.</summary>
    Tenant = 0,

    /// <summary>An occupier living or trading there (not necessarily under a lease).</summary>
    Occupier = 1,

    /// <summary>A managing agent who looks after the property for the owner.</summary>
    ManagingAgent = 2,
}

/// <summary>
/// A tenant, occupier or managing agent of a property (<c>mun.property_party</c>; brief §4.9 owner &amp; tenant register: agents / managing
/// agents), for a period. A party may have an account of its own and may be sent copies of the bills.
/// </summary>
public sealed class PropertyParty : AuditableEntity, ICompanyScoped
{
    private PropertyParty() => Name = string.Empty;

    /// <summary>Records a party.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="role">Tenant, occupier or managing agent.</param>
    /// <param name="name">The party's name.</param>
    /// <param name="from">From when.</param>
    public PropertyParty(long companyId, long propertyId, PropertyPartyRole role, string name, DateOnly from)
    {
        CompanyId = companyId;
        PropertyId = propertyId;
        Role = role;
        Name = name;
        From = from;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>Gets the party's role.</summary>
    public PropertyPartyRole Role { get; private set; }

    /// <summary>Gets the party's name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets or sets the party's own account (customer), if it has one.</summary>
    public long? CustomerId { get; set; }

    /// <summary>Gets or sets the phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>Gets or sets the e-mail address.</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the party's address for correspondence.</summary>
    public string? Address { get; set; }

    /// <summary>Gets or sets whether copies of the bills go to this party.</summary>
    public bool ReceivesBills { get; set; }

    /// <summary>Gets from when.</summary>
    public DateOnly From { get; private set; }

    /// <summary>Gets the last day, or null while current.</summary>
    public DateOnly? To { get; private set; }

    /// <summary>Ends the party's link with the property.</summary>
    /// <param name="lastDay">The last day.</param>
    public void End(DateOnly lastDay) => To = lastDay;

    /// <summary>Whether the party is linked to the property on a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>True when current on the date.</returns>
    public bool IsCurrentOn(DateOnly date) => From <= date && (To is null || To >= date);
}
