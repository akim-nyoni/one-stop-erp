using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>The kind of council licence or permit.</summary>
public enum CouncilLicenceKind
{
    /// <summary>A general business licence.</summary>
    Business = 1,

    /// <summary>A liquor licence.</summary>
    Liquor = 2,

    /// <summary>A shop licence.</summary>
    Shop = 3,

    /// <summary>A building plan approval.</summary>
    BuildingPlan = 4,

    /// <summary>A hawker or street-vending permit.</summary>
    Hawker = 5,

    /// <summary>Any other permit.</summary>
    Other = 9,
}

/// <summary>Where a council licence stands.</summary>
public enum CouncilLicenceStatus
{
    /// <summary>In force (subject to its expiry date).</summary>
    Active = 0,

    /// <summary>Suspended after a failed inspection, until an inspection passes.</summary>
    Suspended = 1,

    /// <summary>Revoked for good.</summary>
    Revoked = 2,
}

/// <summary>
/// A council licence or permit (<c>mun.council_licence</c>, brief §4.9 licensing and permits): business, liquor, shop,
/// building plan or hawker, held by a customer, optionally for a property, renewed in fixed cycles for a fee. A failed
/// inspection suspends it until one passes; a revoked licence cannot be renewed or reinstated.
/// </summary>
public sealed class CouncilLicence : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CouncilLicence()
    {
        LicenceNumber = string.Empty;
        Description = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Issues a licence.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="licenceNumber">The licence number.</param>
    /// <param name="kind">The kind of licence.</param>
    /// <param name="holderCustomerId">The holder's customer account.</param>
    /// <param name="propertyId">The property it is for, if any.</param>
    /// <param name="description">The trading name, premises or works it covers.</param>
    /// <param name="issuedOn">When it was issued.</param>
    /// <param name="expiresOn">The last day it is valid.</param>
    /// <param name="cycleMonths">The months in a renewal cycle.</param>
    /// <param name="fee">The fee per cycle.</param>
    /// <param name="revenueAccountCode">The revenue account the fee is credited to.</param>
    public CouncilLicence(long companyId, string licenceNumber, CouncilLicenceKind kind, long holderCustomerId, long? propertyId, string description,
        DateOnly issuedOn, DateOnly expiresOn, int cycleMonths, decimal fee, string revenueAccountCode)
    {
        CompanyId = companyId;
        LicenceNumber = licenceNumber;
        Kind = kind;
        HolderCustomerId = holderCustomerId;
        PropertyId = propertyId;
        Description = description;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        CycleMonths = cycleMonths;
        Fee = fee;
        RevenueAccountCode = revenueAccountCode;
        Status = CouncilLicenceStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The licence number.</summary>
    public string LicenceNumber { get; private set; }

    /// <summary>The kind of licence.</summary>
    public CouncilLicenceKind Kind { get; private set; }

    /// <summary>The holder's customer account.</summary>
    public long HolderCustomerId { get; private set; }

    /// <summary>The property it is for, if any.</summary>
    public long? PropertyId { get; private set; }

    /// <summary>The trading name, premises or works it covers.</summary>
    public string Description { get; private set; }

    /// <summary>When it was first issued.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>The last day it is valid.</summary>
    public DateOnly ExpiresOn { get; private set; }

    /// <summary>The months in a renewal cycle.</summary>
    public int CycleMonths { get; private set; }

    /// <summary>The fee per cycle.</summary>
    public decimal Fee { get; private set; }

    /// <summary>The revenue account the fee is credited to.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>Where it stands.</summary>
    public CouncilLicenceStatus Status { get; private set; }

    /// <summary>How many times it has been renewed.</summary>
    public int Renewals { get; private set; }

    /// <summary>Why it was revoked.</summary>
    public string? RevokedReason { get; private set; }

    /// <summary>Extends it to a new expiry.</summary>
    /// <param name="newExpiry">The new last valid day.</param>
    public void Renew(DateOnly newExpiry)
    {
        ExpiresOn = newExpiry;
        Renewals++;
    }

    /// <summary>Suspends it after a failed inspection.</summary>
    public void Suspend() => Status = CouncilLicenceStatus.Suspended;

    /// <summary>Puts a suspended licence back in force.</summary>
    public void Reinstate() => Status = CouncilLicenceStatus.Active;

    /// <summary>Revokes it for good.</summary>
    /// <param name="reason">Why.</param>
    public void Revoke(string reason)
    {
        Status = CouncilLicenceStatus.Revoked;
        RevokedReason = reason;
    }
}

/// <summary>An inspection of a council licence's premises or works (<c>mun.council_inspection</c>).</summary>
public sealed class CouncilInspection : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CouncilInspection()
    {
        Inspector = string.Empty;
    }

    /// <summary>Records an inspection.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="licenceId">The licence inspected.</param>
    /// <param name="inspectedOn">When.</param>
    /// <param name="inspector">Who inspected.</param>
    /// <param name="passed">Whether it passed.</param>
    /// <param name="notes">Findings.</param>
    public CouncilInspection(long companyId, long licenceId, DateOnly inspectedOn, string inspector, bool passed, string? notes)
    {
        CompanyId = companyId;
        LicenceId = licenceId;
        InspectedOn = inspectedOn;
        Inspector = inspector;
        Passed = passed;
        Notes = notes;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The licence inspected.</summary>
    public long LicenceId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly InspectedOn { get; private set; }

    /// <summary>Who inspected.</summary>
    public string Inspector { get; private set; }

    /// <summary>Whether it passed.</summary>
    public bool Passed { get; private set; }

    /// <summary>Findings.</summary>
    public string? Notes { get; private set; }
}
