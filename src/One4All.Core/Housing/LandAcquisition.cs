using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>How land is acquired (brief §4.10.1).</summary>
public enum AcquisitionMethod
{
    /// <summary>Purchase.</summary>
    Purchase = 1,

    /// <summary>Donation or bequest.</summary>
    Donation = 2,

    /// <summary>State land grant or allocation.</summary>
    StateGrant = 3,

    /// <summary>Compulsory acquisition / expropriation.</summary>
    Compulsory = 4,

    /// <summary>Land swap / exchange.</summary>
    LandSwap = 5,

    /// <summary>Joint venture contribution.</summary>
    JointVenture = 6,

    /// <summary>Escheat / reversion.</summary>
    Escheat = 7,

    /// <summary>Regularisation of existing occupation.</summary>
    Regularisation = 8,
}

/// <summary>Who made an offer in a land price negotiation.</summary>
public enum NegotiationParty
{
    /// <summary>The council.</summary>
    Council = 1,

    /// <summary>The owner or their agent.</summary>
    Counterparty = 2,
}

/// <summary>Where a price negotiation stands.</summary>
public enum NegotiationStatus
{
    /// <summary>No offer made yet (a grant, swap or regularisation may never need one).</summary>
    None = 0,

    /// <summary>Offers are going back and forth.</summary>
    Open = 1,

    /// <summary>A price is agreed.</summary>
    Agreed = 2,

    /// <summary>Talks failed.</summary>
    BrokenOff = 3,
}

/// <summary>An offer or counter-offer in a land price negotiation (<c>housing.acquisition_offer</c>).</summary>
public sealed class NegotiationOffer : Entity
{
    private NegotiationOffer()
    {
    }

    /// <summary>Creates the offer.</summary>
    /// <param name="party">Who offers.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="offeredOn">When.</param>
    /// <param name="note">What was said.</param>
    /// <param name="sequence">Its place in the exchange.</param>
    public NegotiationOffer(NegotiationParty party, decimal amount, DateOnly offeredOn, string? note, int sequence)
    {
        Party = party;
        Amount = amount;
        OfferedOn = offeredOn;
        Note = note;
        Sequence = sequence;
    }

    /// <summary>Gets the acquisition.</summary>
    public long LandAcquisitionId { get; private set; }

    /// <summary>Gets who offered.</summary>
    public NegotiationParty Party { get; private set; }

    /// <summary>Gets the amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets when.</summary>
    public DateOnly OfferedOn { get; private set; }

    /// <summary>Gets what was said.</summary>
    public string? Note { get; private set; }

    /// <summary>Gets its place in the exchange (1 is the first offer).</summary>
    public int Sequence { get; private set; }
}

/// <summary>A due-diligence check that must be signed off before an acquisition completes (brief §4.10.1).</summary>
public enum DueDiligenceItem
{
    /// <summary>Title search result.</summary>
    TitleSearch = 1,

    /// <summary>Encumbrances and bonds.</summary>
    Encumbrances = 2,

    /// <summary>Caveats.</summary>
    Caveats = 3,

    /// <summary>Servitudes and way-leaves.</summary>
    Servitudes = 4,

    /// <summary>Boundary disputes.</summary>
    BoundaryDisputes = 5,

    /// <summary>Outstanding rates on the parcel.</summary>
    OutstandingRates = 6,

    /// <summary>Environmental impact assessment status.</summary>
    EnvironmentalImpact = 7,

    /// <summary>Heritage / archaeological clearance.</summary>
    Heritage = 8,

    /// <summary>Flood-plain or geotechnical constraints.</summary>
    Geotechnical = 9,

    /// <summary>Wetland status.</summary>
    Wetland = 10,

    /// <summary>Existing occupants / compensation liability.</summary>
    Occupants = 11,
}

/// <summary>An element of a parcel's cost (brief §4.10.1 acquisition cost build-up).</summary>
public enum AcquisitionCostComponent
{
    /// <summary>The purchase price.</summary>
    PurchasePrice = 1,

    /// <summary>Survey.</summary>
    Survey = 2,

    /// <summary>Legal / conveyancing.</summary>
    Legal = 3,

    /// <summary>Valuation.</summary>
    Valuation = 4,

    /// <summary>Compensation to occupants.</summary>
    OccupantCompensation = 5,

    /// <summary>Stamp duty / transfer fees.</summary>
    TransferDuty = 6,

    /// <summary>Capitalisable interest on acquisition loans.</summary>
    CapitalisedInterest = 7,
}

/// <summary>Where an acquisition stands.</summary>
public enum AcquisitionStatus
{
    /// <summary>In progress: due diligence and costs being recorded.</summary>
    Open = 1,

    /// <summary>Completed: the parcel is in the land bank at its total cost.</summary>
    Completed = 2,

    /// <summary>Abandoned: its costs written off.</summary>
    Abandoned = 3,
}

/// <summary>
/// A land acquisition case (<c>hse.land_acquisition</c>, brief §4.10.1): the method, counterparty and authorising
/// resolution, the valuation obtained, the due-diligence checklist signed off item by item, and every cost capitalised
/// into land WIP as it is incurred — completing it brings the parcel into the land bank at the total built-up cost.
/// </summary>
public sealed class LandAcquisition : AuditableEntity, ICompanyScoped
{
    private readonly List<DueDiligenceCheck> _checks = [];
    private readonly List<AcquisitionCost> _costs = [];
    private readonly List<NegotiationOffer> _offers = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LandAcquisition()
    {
        AcquisitionNumber = string.Empty;
        ParcelNumber = string.Empty;
        Description = string.Empty;
        Counterparty = string.Empty;
        ResolutionReference = string.Empty;
    }

    /// <summary>Opens an acquisition.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="acquisitionNumber">The acquisition number.</param>
    /// <param name="parcelNumber">The parcel reference it will carry.</param>
    /// <param name="description">The description.</param>
    /// <param name="areaSquareMetres">The area.</param>
    /// <param name="method">How it is acquired.</param>
    /// <param name="counterparty">The seller or grantor.</param>
    /// <param name="resolutionReference">The council resolution authorising it.</param>
    /// <param name="openedOn">When.</param>
    public LandAcquisition(long companyId, string acquisitionNumber, string parcelNumber, string description, decimal areaSquareMetres, AcquisitionMethod method,
        string counterparty, string resolutionReference, DateOnly openedOn)
    {
        CompanyId = companyId;
        AcquisitionNumber = acquisitionNumber;
        ParcelNumber = parcelNumber;
        Description = description;
        AreaSquareMetres = areaSquareMetres;
        Method = method;
        Counterparty = counterparty;
        ResolutionReference = resolutionReference;
        OpenedOn = openedOn;
        Status = AcquisitionStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The acquisition number.</summary>
    public string AcquisitionNumber { get; private set; }

    /// <summary>The parcel reference it will carry.</summary>
    public string ParcelNumber { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The area.</summary>
    public decimal AreaSquareMetres { get; private set; }

    /// <summary>How it is acquired.</summary>
    public AcquisitionMethod Method { get; private set; }

    /// <summary>The seller or grantor.</summary>
    public string Counterparty { get; private set; }

    /// <summary>The council resolution authorising it.</summary>
    public string ResolutionReference { get; private set; }

    /// <summary>When it was opened.</summary>
    public DateOnly OpenedOn { get; private set; }

    /// <summary>The valuer.</summary>
    public string? Valuer { get; private set; }

    /// <summary>When it was valued.</summary>
    public DateOnly? ValuedOn { get; private set; }

    /// <summary>The value obtained.</summary>
    public decimal? ValuedAt { get; private set; }

    /// <summary>Where it stands.</summary>
    public AcquisitionStatus Status { get; private set; }

    /// <summary>The parcel it brought into the land bank.</summary>
    public long? LandParcelId { get; private set; }

    /// <summary>When it was completed or abandoned.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Why it was abandoned.</summary>
    public string? AbandonReason { get; private set; }

    /// <summary>The due-diligence sign-offs.</summary>
    public IReadOnlyList<DueDiligenceCheck> Checks => _checks.AsReadOnly();

    /// <summary>The costs capitalised.</summary>
    public IReadOnlyList<AcquisitionCost> Costs => _costs.AsReadOnly();

    /// <summary>Gets the offers made in the price negotiation, oldest first (brief §4.10.1).</summary>
    public IReadOnlyList<NegotiationOffer> Offers => _offers.AsReadOnly();

    /// <summary>Gets where the negotiation stands.</summary>
    public NegotiationStatus Negotiation { get; private set; }

    /// <summary>Gets the price agreed, once the negotiation is settled.</summary>
    public decimal? AgreedPrice { get; private set; }

    /// <summary>Gets when the price was agreed.</summary>
    public DateOnly? AgreedOn { get; private set; }

    /// <summary>Gets why the negotiation was broken off, if it was.</summary>
    public string? BreakOffReason { get; private set; }

    /// <summary>Records an offer or counter-offer; the negotiation is open from the first offer.</summary>
    /// <param name="party">Who offers.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="date">When.</param>
    /// <param name="note">What was said.</param>
    /// <returns>The offer, or null when the negotiation is settled or broken off.</returns>
    public NegotiationOffer? Offer(NegotiationParty party, decimal amount, DateOnly date, string? note)
    {
        if (Negotiation is NegotiationStatus.Agreed or NegotiationStatus.BrokenOff)
        {
            return null;
        }

        NegotiationOffer offer = new(party, amount, date, note, _offers.Count + 1);
        _offers.Add(offer);
        Negotiation = NegotiationStatus.Open;
        return offer;
    }

    /// <summary>Settles the negotiation at the latest offer's amount.</summary>
    /// <param name="date">When.</param>
    /// <returns>The agreed price, or null when there is no offer to accept or the negotiation is not open.</returns>
    public decimal? Agree(DateOnly date)
    {
        if (Negotiation != NegotiationStatus.Open || _offers.Count == 0)
        {
            return null;
        }

        AgreedPrice = _offers[^1].Amount;
        AgreedOn = date;
        Negotiation = NegotiationStatus.Agreed;
        return AgreedPrice;
    }

    /// <summary>Breaks the negotiation off (the acquisition is then abandoned or reopened by resolution).</summary>
    /// <param name="reason">Why.</param>
    public void BreakOff(string reason)
    {
        Negotiation = NegotiationStatus.BrokenOff;
        BreakOffReason = reason;
    }

    /// <summary>The total cost capitalised.</summary>
    public decimal TotalCost => _costs.Sum(c => c.Amount);

    /// <summary>Records the valuation.</summary>
    /// <param name="valuer">The valuer.</param>
    /// <param name="valuedOn">When.</param>
    /// <param name="value">The value.</param>
    public void Value(string valuer, DateOnly valuedOn, decimal value)
    {
        Valuer = valuer;
        ValuedOn = valuedOn;
        ValuedAt = value;
    }

    /// <summary>Records a sign-off; signing an item off again revises the earlier sign-off.</summary>
    /// <param name="item">The item.</param>
    /// <param name="finding">What was found.</param>
    /// <param name="acceptable">Whether it is acceptable for the acquisition to proceed.</param>
    /// <param name="userId">The officer.</param>
    /// <param name="date">When.</param>
    public void SignOff(DueDiligenceItem item, string finding, bool acceptable, long userId, DateOnly date)
    {
        DueDiligenceCheck? existing = _checks.FirstOrDefault(c => c.Item == item);
        if (existing is null)
        {
            _checks.Add(new DueDiligenceCheck(item, finding, acceptable, userId, date));
        }
        else
        {
            existing.Revise(finding, acceptable, userId, date);
        }
    }

    /// <summary>Records a cost capitalised.</summary>
    /// <param name="cost">The cost.</param>
    public void AddCost(AcquisitionCost cost) => _costs.Add(cost);

    /// <summary>Completes it.</summary>
    /// <param name="landParcelId">The parcel created.</param>
    /// <param name="date">When.</param>
    public void Complete(long landParcelId, DateOnly date)
    {
        Status = AcquisitionStatus.Completed;
        LandParcelId = landParcelId;
        ClosedOn = date;
    }

    /// <summary>Abandons it.</summary>
    /// <param name="date">When.</param>
    /// <param name="reason">Why.</param>
    public void Abandon(DateOnly date, string reason)
    {
        Status = AcquisitionStatus.Abandoned;
        ClosedOn = date;
        AbandonReason = reason;
    }
}

/// <summary>A due-diligence item signed off (<c>hse.due_diligence_check</c>).</summary>
public sealed class DueDiligenceCheck : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DueDiligenceCheck()
    {
        Finding = string.Empty;
    }

    /// <summary>Records a sign-off.</summary>
    /// <param name="item">The item.</param>
    /// <param name="finding">What was found.</param>
    /// <param name="acceptable">Whether it is acceptable for the acquisition to proceed.</param>
    /// <param name="signedOffByUserId">The officer.</param>
    /// <param name="signedOffOn">When.</param>
    public DueDiligenceCheck(DueDiligenceItem item, string finding, bool acceptable, long signedOffByUserId, DateOnly signedOffOn)
    {
        Item = item;
        Finding = finding;
        Acceptable = acceptable;
        SignedOffByUserId = signedOffByUserId;
        SignedOffOn = signedOffOn;
    }

    /// <summary>The acquisition.</summary>
    public long LandAcquisitionId { get; private set; }

    /// <summary>The item.</summary>
    public DueDiligenceItem Item { get; private set; }

    /// <summary>What was found.</summary>
    public string Finding { get; private set; }

    /// <summary>Whether it is acceptable for the acquisition to proceed.</summary>
    public bool Acceptable { get; private set; }

    /// <summary>The officer.</summary>
    public long SignedOffByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly SignedOffOn { get; private set; }

    /// <summary>Revises the sign-off.</summary>
    /// <param name="finding">What was found.</param>
    /// <param name="acceptable">Whether it is acceptable.</param>
    /// <param name="userId">The officer.</param>
    /// <param name="date">When.</param>
    public void Revise(string finding, bool acceptable, long userId, DateOnly date)
    {
        Finding = finding;
        Acceptable = acceptable;
        SignedOffByUserId = userId;
        SignedOffOn = date;
    }
}

/// <summary>A cost capitalised into an acquisition (<c>hse.acquisition_cost</c>).</summary>
public sealed class AcquisitionCost : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AcquisitionCost()
    {
        Reference = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records a cost.</summary>
    /// <param name="component">The element of cost.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="incurredOn">When.</param>
    /// <param name="reference">The supporting document.</param>
    /// <param name="journalNumber">The journal that capitalised it.</param>
    public AcquisitionCost(AcquisitionCostComponent component, decimal amount, DateOnly incurredOn, string reference, string journalNumber)
    {
        Component = component;
        Amount = amount;
        IncurredOn = incurredOn;
        Reference = reference;
        JournalNumber = journalNumber;
    }

    /// <summary>The acquisition.</summary>
    public long LandAcquisitionId { get; private set; }

    /// <summary>The element of cost.</summary>
    public AcquisitionCostComponent Component { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>When.</summary>
    public DateOnly IncurredOn { get; private set; }

    /// <summary>The supporting document.</summary>
    public string Reference { get; private set; }

    /// <summary>The journal that capitalised it.</summary>
    public string JournalNumber { get; private set; }
}
