using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The kind of housing agreement (brief §4.10.5).</summary>
public enum AgreementType
{
    /// <summary>An outright cash sale.</summary>
    CashSale = 1,

    /// <summary>A sale on instalments.</summary>
    InstalmentSale = 2,

    /// <summary>A lease of a stand.</summary>
    Lease = 3,

    /// <summary>A council rental.</summary>
    Rental = 4,

    /// <summary>A cooperative allocation.</summary>
    Cooperative = 5,

    /// <summary>Rent-to-buy: rent paid towards the price, ownership passing when it is paid.</summary>
    RentToBuy = 6,

    /// <summary>A lease with an option to purchase.</summary>
    LeaseWithOption = 7,

    /// <summary>A site-and-service scheme allocation (a serviced stand, the holder builds).</summary>
    SiteAndService = 8,

    /// <summary>A lodger permit.</summary>
    LodgerPermit = 9,

    /// <summary>A long lease (for example 99 years) with rent reviews.</summary>
    LongLease = 10,
}

/// <summary>
/// A housing agreement on the agreement register (<c>hse.housing_agreement</c>, brief §4.10.5): the parties, stand and
/// type, with its versions — an amendment creates a new version and the superseded one is kept.
/// </summary>
public sealed class HousingAgreement : AuditableEntity, ICompanyScoped
{
    private readonly List<AgreementVersion> _versions = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private HousingAgreement()
    {
        AgreementNumber = string.Empty;
    }

    /// <summary>Records an agreement.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement number.</param>
    /// <param name="agreementType">The kind of agreement.</param>
    /// <param name="partyCustomerId">The other party.</param>
    /// <param name="standId">The stand, if any.</param>
    public HousingAgreement(long companyId, string agreementNumber, AgreementType agreementType, long partyCustomerId, long? standId)
    {
        CompanyId = companyId;
        AgreementNumber = agreementNumber;
        AgreementType = agreementType;
        PartyCustomerId = partyCustomerId;
        StandId = standId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The agreement number.</summary>
    public string AgreementNumber { get; private set; }

    /// <summary>The kind of agreement.</summary>
    public AgreementType AgreementType { get; private set; }

    /// <summary>The other party.</summary>
    public long PartyCustomerId { get; private set; }

    /// <summary>The stand, if any.</summary>
    public long? StandId { get; private set; }

    /// <summary>The versions, oldest first.</summary>
    public IReadOnlyList<AgreementVersion> Versions => _versions.OrderBy(v => v.Version).ToList();

    /// <summary>The newest version.</summary>
    public AgreementVersion Latest => _versions.OrderBy(v => v.Version).Last();

    /// <summary>The version in force: the newest signed one, if any.</summary>
    public AgreementVersion? InForce => _versions.Where(v => v.SignedOn is not null).OrderBy(v => v.Version).LastOrDefault();

    /// <summary>Adds a version.</summary>
    /// <param name="version">The version.</param>
    public void AddVersion(AgreementVersion version) => _versions.Add(version);
}

/// <summary>
/// A version of a housing agreement (<c>hse.agreement_version</c>): the price, terms and special conditions, the
/// generated document and its hash, why it was made, and its signature and witnesses once signed.
/// </summary>
public sealed class AgreementVersion : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AgreementVersion()
    {
        Terms = string.Empty;
        Document = string.Empty;
        DocumentHash = string.Empty;
    }

    /// <summary>Records a version.</summary>
    /// <param name="version">The version number.</param>
    /// <param name="price">The price or annual rent.</param>
    /// <param name="terms">The payment terms.</param>
    /// <param name="specialConditions">Special conditions.</param>
    /// <param name="amendmentReason">Why it was amended (null for the first).</param>
    /// <param name="document">The generated document.</param>
    /// <param name="documentHash">The document's hash.</param>
    public AgreementVersion(int version, decimal price, string terms, string? specialConditions, string? amendmentReason, string document, string documentHash)
    {
        Version = version;
        Price = price;
        Terms = terms;
        SpecialConditions = specialConditions;
        AmendmentReason = amendmentReason;
        Document = document;
        DocumentHash = documentHash;
    }

    /// <summary>The agreement.</summary>
    public long HousingAgreementId { get; private set; }

    /// <summary>The version number.</summary>
    public int Version { get; private set; }

    /// <summary>The price or annual rent.</summary>
    public decimal Price { get; private set; }

    /// <summary>The payment terms.</summary>
    public string Terms { get; private set; }

    /// <summary>Special conditions.</summary>
    public string? SpecialConditions { get; private set; }

    /// <summary>Why it was amended.</summary>
    public string? AmendmentReason { get; private set; }

    /// <summary>The generated document.</summary>
    public string Document { get; private set; }

    /// <summary>The document's SHA-256 hash.</summary>
    public string DocumentHash { get; private set; }

    /// <summary>When it was signed.</summary>
    public DateOnly? SignedOn { get; private set; }

    /// <summary>The witnesses to the signature.</summary>
    public string? Witnesses { get; private set; }

    /// <summary>Records the signature.</summary>
    /// <param name="date">When.</param>
    /// <param name="witnesses">The witnesses.</param>
    public void Sign(DateOnly date, string witnesses)
    {
        SignedOn = date;
        Witnesses = witnesses;
    }
}
