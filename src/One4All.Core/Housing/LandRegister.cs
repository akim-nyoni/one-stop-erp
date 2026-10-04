using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>A right or restriction registered against land (brief §14 H7).</summary>
public enum EncumbranceType
{
    /// <summary>A servitude (right of way, pipeline, power line).</summary>
    Servitude = 1,

    /// <summary>A caveat: no dealing in the land while it is in force.</summary>
    Caveat = 2,

    /// <summary>A mortgage bond.</summary>
    Mortgage = 3,

    /// <summary>A registered lease.</summary>
    Lease = 4,

    /// <summary>A restrictive title condition.</summary>
    RestrictiveCondition = 5,

    /// <summary>A utility wayleave.</summary>
    Wayleave = 6,
}

/// <summary>
/// An encumbrance on a land parcel (<c>hse.land_encumbrance</c>, brief §14 H7): registered with its reference and holder,
/// kept after it is lifted. A caveat in force stops the parcel being disposed of and its stands being allocated or sold.
/// </summary>
public sealed class LandEncumbrance : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LandEncumbrance()
    {
        Reference = string.Empty;
        InFavourOf = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Registers an encumbrance.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="landParcelId">The parcel.</param>
    /// <param name="type">What it is.</param>
    /// <param name="reference">The registration reference.</param>
    /// <param name="inFavourOf">Who holds the right.</param>
    /// <param name="description">The particulars.</param>
    /// <param name="registeredOn">When it was registered.</param>
    public LandEncumbrance(long companyId, long landParcelId, EncumbranceType type, string reference, string inFavourOf, string description, DateOnly registeredOn)
    {
        CompanyId = companyId;
        LandParcelId = landParcelId;
        Type = type;
        Reference = reference;
        InFavourOf = inFavourOf;
        Description = description;
        RegisteredOn = registeredOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The parcel.</summary>
    public long LandParcelId { get; private set; }

    /// <summary>What it is.</summary>
    public EncumbranceType Type { get; private set; }

    /// <summary>The registration reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Who holds the right.</summary>
    public string InFavourOf { get; private set; }

    /// <summary>The particulars.</summary>
    public string Description { get; private set; }

    /// <summary>When it was registered.</summary>
    public DateOnly RegisteredOn { get; private set; }

    /// <summary>When it was lifted; in force while null.</summary>
    public DateOnly? LiftedOn { get; private set; }

    /// <summary>The document cancelling it.</summary>
    public string? LiftReference { get; private set; }

    /// <summary>Lifts the encumbrance.</summary>
    /// <param name="date">When.</param>
    /// <param name="reference">The cancelling document.</param>
    public void Lift(DateOnly date, string reference)
    {
        LiftedOn = date;
        LiftReference = reference;
    }
}

/// <summary>What a land dispute is about (brief §14 H8).</summary>
public enum LandDisputeType
{
    /// <summary>Who owns the land.</summary>
    Ownership = 1,

    /// <summary>Where the boundary lies.</summary>
    Boundary = 2,

    /// <summary>A structure or use over the boundary.</summary>
    Encroachment = 3,

    /// <summary>Compensation for the land.</summary>
    Compensation = 4,

    /// <summary>Unlawful occupation.</summary>
    Occupation = 5,
}

/// <summary>Where a land dispute stands; it only moves forward.</summary>
public enum LandDisputeStage
{
    /// <summary>Reported, not yet looked into.</summary>
    Reported = 1,

    /// <summary>Being investigated (survey, records).</summary>
    Investigation = 2,

    /// <summary>In negotiation with the other party.</summary>
    Negotiation = 3,

    /// <summary>Before a mediator.</summary>
    Mediation = 4,

    /// <summary>In court.</summary>
    Litigation = 5,

    /// <summary>Judgment given, awaiting compliance.</summary>
    Judgment = 6,

    /// <summary>Closed.</summary>
    Resolved = 7,
}

/// <summary>
/// A disputed or encroached piece of land (<c>hse.land_dispute</c>, brief §14 H8): on a parcel, optionally one stand,
/// with its stages dated so its age and time at each stage can be reported. While open, a dispute naming a stand blocks
/// that stand from being allocated, sold or transferred.
/// </summary>
public sealed class LandDispute : AuditableEntity, ICompanyScoped
{
    private readonly List<LandDisputeStep> _steps = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LandDispute()
    {
        DisputeNumber = string.Empty;
        Claimant = string.Empty;
        Particulars = string.Empty;
    }

    /// <summary>Opens a dispute.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="disputeNumber">Its number.</param>
    /// <param name="landParcelId">The parcel.</param>
    /// <param name="standId">The stand, when the dispute is over one.</param>
    /// <param name="type">What it is about.</param>
    /// <param name="claimant">Who raised it.</param>
    /// <param name="particulars">The particulars.</param>
    /// <param name="openedOn">When.</param>
    public LandDispute(long companyId, string disputeNumber, long landParcelId, long? standId, LandDisputeType type, string claimant, string particulars, DateOnly openedOn)
    {
        CompanyId = companyId;
        DisputeNumber = disputeNumber;
        LandParcelId = landParcelId;
        StandId = standId;
        Type = type;
        Claimant = claimant;
        Particulars = particulars;
        OpenedOn = openedOn;
        Stage = LandDisputeStage.Reported;
        StageSince = openedOn;
        _steps.Add(new LandDisputeStep(LandDisputeStage.Reported, openedOn, particulars));
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Its number.</summary>
    public string DisputeNumber { get; private set; }

    /// <summary>The parcel.</summary>
    public long LandParcelId { get; private set; }

    /// <summary>The stand, when the dispute is over one.</summary>
    public long? StandId { get; private set; }

    /// <summary>What it is about.</summary>
    public LandDisputeType Type { get; private set; }

    /// <summary>Who raised it.</summary>
    public string Claimant { get; private set; }

    /// <summary>The particulars.</summary>
    public string Particulars { get; private set; }

    /// <summary>When it was opened.</summary>
    public DateOnly OpenedOn { get; private set; }

    /// <summary>The current stage.</summary>
    public LandDisputeStage Stage { get; private set; }

    /// <summary>Since when it has been at that stage.</summary>
    public DateOnly StageSince { get; private set; }

    /// <summary>How it ended, once resolved.</summary>
    public string? Outcome { get; private set; }

    /// <summary>The stages it has passed through.</summary>
    public IReadOnlyCollection<LandDisputeStep> Steps => _steps.AsReadOnly();

    /// <summary>Moves the dispute to a later stage (the caller checks it is later and dated on or after the current one).</summary>
    /// <param name="stage">The new stage.</param>
    /// <param name="date">When.</param>
    /// <param name="note">What happened.</param>
    public void MoveTo(LandDisputeStage stage, DateOnly date, string note)
    {
        Stage = stage;
        StageSince = date;
        if (stage == LandDisputeStage.Resolved)
        {
            Outcome = note;
        }

        _steps.Add(new LandDisputeStep(stage, date, note));
    }
}

/// <summary>A stage a land dispute reached.</summary>
public sealed class LandDisputeStep : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LandDisputeStep() => Note = string.Empty;

    /// <summary>Records a stage.</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="date">When it was reached.</param>
    /// <param name="note">What happened.</param>
    public LandDisputeStep(LandDisputeStage stage, DateOnly date, string note)
    {
        Stage = stage;
        Date = date;
        Note = note;
    }

    /// <summary>The dispute.</summary>
    public long LandDisputeId { get; private set; }

    /// <summary>The stage.</summary>
    public LandDisputeStage Stage { get; private set; }

    /// <summary>When it was reached.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>What happened.</summary>
    public string Note { get; private set; }
}

/// <summary>How land left the council's land bank (brief §14 H6).</summary>
public enum LandDisposalType
{
    /// <summary>Sold for cash.</summary>
    Sale = 1,

    /// <summary>Exchanged for another parcel.</summary>
    Exchange = 2,

    /// <summary>Donated (no consideration).</summary>
    Donation = 3,

    /// <summary>Expropriated by another authority, with compensation.</summary>
    Expropriation = 4,
}

/// <summary>
/// A disposal or exchange of land (<c>hse.land_disposal</c>, brief §14 H6): all or part of an undivided parcel, under a
/// council resolution, with the cost taken out of the land bank pro rata to area, the consideration received, any parcel
/// received in exchange and the journal.
/// </summary>
public sealed class LandDisposal : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LandDisposal()
    {
        DisposalNumber = string.Empty;
        Counterparty = string.Empty;
        ResolutionReference = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records a disposal.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="disposalNumber">Its number.</param>
    /// <param name="landParcelId">The parcel disposed of.</param>
    /// <param name="type">How.</param>
    /// <param name="counterparty">To whom.</param>
    /// <param name="resolutionReference">The council resolution authorising it.</param>
    /// <param name="date">When.</param>
    /// <param name="areaSquareMetres">The area disposed of.</param>
    /// <param name="costDerecognised">The land-bank cost taken out.</param>
    /// <param name="consideration">The proceeds, compensation or exchange value.</param>
    /// <param name="exchangeParcelId">The parcel received in exchange.</param>
    /// <param name="journalNumber">The journal.</param>
    public LandDisposal(long companyId, string disposalNumber, long landParcelId, LandDisposalType type, string counterparty, string resolutionReference, DateOnly date,
        decimal areaSquareMetres, decimal costDerecognised, decimal consideration, long? exchangeParcelId, string journalNumber)
    {
        CompanyId = companyId;
        DisposalNumber = disposalNumber;
        LandParcelId = landParcelId;
        Type = type;
        Counterparty = counterparty;
        ResolutionReference = resolutionReference;
        Date = date;
        AreaSquareMetres = areaSquareMetres;
        CostDerecognised = costDerecognised;
        Consideration = consideration;
        ExchangeParcelId = exchangeParcelId;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Its number.</summary>
    public string DisposalNumber { get; private set; }

    /// <summary>The parcel disposed of.</summary>
    public long LandParcelId { get; private set; }

    /// <summary>How.</summary>
    public LandDisposalType Type { get; private set; }

    /// <summary>To whom.</summary>
    public string Counterparty { get; private set; }

    /// <summary>The council resolution authorising it.</summary>
    public string ResolutionReference { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The area disposed of.</summary>
    public decimal AreaSquareMetres { get; private set; }

    /// <summary>The land-bank cost taken out.</summary>
    public decimal CostDerecognised { get; private set; }

    /// <summary>The proceeds, compensation or exchange value.</summary>
    public decimal Consideration { get; private set; }

    /// <summary>The parcel received in exchange.</summary>
    public long? ExchangeParcelId { get; private set; }

    /// <summary>The journal.</summary>
    public string JournalNumber { get; private set; }

    /// <summary>The gain (positive) or loss on disposal.</summary>
    public decimal GainOrLoss => Consideration - CostDerecognised;
}
