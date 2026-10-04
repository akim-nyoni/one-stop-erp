using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Where a title deed application stands (brief §4.10.6 title deed pipeline).</summary>
public enum TitleStage
{
    /// <summary>Applied for.</summary>
    Applied = 1,

    /// <summary>A conveyancer assigned.</summary>
    ConveyancerAssigned = 2,

    /// <summary>Documents lodged at the deeds registry.</summary>
    Lodged = 3,

    /// <summary>The registry raised a query.</summary>
    QueryRaised = 4,

    /// <summary>The query resolved and re-lodged.</summary>
    QueryResolved = 5,

    /// <summary>Registered: deed number and registration date captured.</summary>
    Registered = 6,

    /// <summary>The deed received into council custody.</summary>
    InCustody = 7,

    /// <summary>Handed over to the owner against a signed receipt.</summary>
    HandedOver = 8,
}

/// <summary>
/// A title deed application for a stand (<c>hse.title_deed</c>): it moves through the pipeline stage by stage, each step
/// recorded with its date, so the age of every deed at every stage can be reported. One application per stand.
/// </summary>
public sealed class TitleDeed : AuditableEntity, ICompanyScoped
{
    private readonly List<TitleDeedStep> _steps = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TitleDeed()
    {
        ApplicationNumber = string.Empty;
    }

    /// <summary>Applies for title.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application number.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="holderCustomerId">The holder title is applied for.</param>
    /// <param name="appliedOn">When.</param>
    public TitleDeed(long companyId, string applicationNumber, long standId, long holderCustomerId, DateOnly appliedOn)
    {
        CompanyId = companyId;
        ApplicationNumber = applicationNumber;
        StandId = standId;
        HolderCustomerId = holderCustomerId;
        AppliedOn = appliedOn;
        Stage = TitleStage.Applied;
        StageSince = appliedOn;
        _steps.Add(new TitleDeedStep(TitleStage.Applied, appliedOn, null));
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The application number.</summary>
    public string ApplicationNumber { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The holder title is applied for.</summary>
    public long HolderCustomerId { get; private set; }

    /// <summary>When it was applied for.</summary>
    public DateOnly AppliedOn { get; private set; }

    /// <summary>The current stage.</summary>
    public TitleStage Stage { get; private set; }

    /// <summary>Since when it has been at the current stage.</summary>
    public DateOnly StageSince { get; private set; }

    /// <summary>The conveyancer.</summary>
    public string? Conveyancer { get; private set; }

    /// <summary>The deed number.</summary>
    public string? DeedNumber { get; private set; }

    /// <summary>The registration date.</summary>
    public DateOnly? RegisteredOn { get; private set; }

    /// <summary>Who signed the receipt on hand-over.</summary>
    public string? ReceivedBy { get; private set; }

    /// <summary>Whether the deed has been handed over (the application is closed).</summary>
    public bool IsClosed => Stage == TitleStage.HandedOver;

    /// <summary>Every step, oldest first.</summary>
    public IReadOnlyList<TitleDeedStep> Steps => _steps.AsReadOnly();

    /// <summary>Moves to the next stage (the transition is validated by the caller).</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="date">When.</param>
    /// <param name="note">A note, if any.</param>
    public void Advance(TitleStage stage, DateOnly date, string? note)
    {
        Stage = stage;
        StageSince = date;
        _steps.Add(new TitleDeedStep(stage, date, note));
    }

    /// <summary>Records the conveyancer.</summary>
    /// <param name="conveyancer">The conveyancer.</param>
    public void AssignConveyancer(string conveyancer) => Conveyancer = conveyancer;

    /// <summary>Records the registration.</summary>
    /// <param name="deedNumber">The deed number.</param>
    /// <param name="registeredOn">The registration date.</param>
    public void Register(string deedNumber, DateOnly registeredOn)
    {
        DeedNumber = deedNumber;
        RegisteredOn = registeredOn;
    }

    /// <summary>Records who signed for the deed.</summary>
    /// <param name="receivedBy">Who.</param>
    public void HandOver(string receivedBy) => ReceivedBy = receivedBy;
}

/// <summary>One step of a title deed application (<c>hse.title_deed_step</c>).</summary>
public sealed class TitleDeedStep : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TitleDeedStep()
    {
    }

    /// <summary>Records a step.</summary>
    /// <param name="stage">The stage reached.</param>
    /// <param name="date">When.</param>
    /// <param name="note">A note, if any.</param>
    public TitleDeedStep(TitleStage stage, DateOnly date, string? note)
    {
        Stage = stage;
        Date = date;
        Note = note;
    }

    /// <summary>The application.</summary>
    public long TitleDeedId { get; private set; }

    /// <summary>The stage reached.</summary>
    public TitleStage Stage { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>A note, if any.</summary>
    public string? Note { get; private set; }
}
