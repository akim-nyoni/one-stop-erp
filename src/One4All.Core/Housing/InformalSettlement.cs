using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The kind of structure an enumerated household occupies.</summary>
public enum StructureType
{
    /// <summary>A shack of salvaged or temporary materials.</summary>
    Shack = 1,

    /// <summary>A brick or block structure.</summary>
    Brick = 2,

    /// <summary>Part temporary, part permanent.</summary>
    Mixed = 3,

    /// <summary>Anything else.</summary>
    Other = 4,
}

/// <summary>What became of an enumerated household.</summary>
public enum RegularisationOutcome
{
    /// <summary>Not yet dealt with.</summary>
    Pending = 0,

    /// <summary>Registered on the waiting list.</summary>
    WaitingList = 1,

    /// <summary>Allocated a stand directly on regularisation.</summary>
    Regularised = 2,
}

/// <summary>
/// An informal settlement on the council's register (<c>hse.informal_settlement</c>, brief §4.10.8): where it is, the
/// cut-off date after which new occupation does not qualify for regularisation, and its approved upgrading plan.
/// </summary>
public sealed class InformalSettlement : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InformalSettlement()
    {
        Code = string.Empty;
        Name = string.Empty;
        Ward = string.Empty;
    }

    /// <summary>Registers a settlement.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The settlement code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="ward">The ward.</param>
    /// <param name="landParcelId">The parcel it stands on, if known.</param>
    /// <param name="cutOffDate">Occupation after this date does not qualify.</param>
    public InformalSettlement(long companyId, string code, string name, string ward, long? landParcelId, DateOnly cutOffDate)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        Ward = ward;
        LandParcelId = landParcelId;
        CutOffDate = cutOffDate;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The settlement code.</summary>
    public string Code { get; private set; }

    /// <summary>Its name.</summary>
    public string Name { get; private set; }

    /// <summary>The ward.</summary>
    public string Ward { get; private set; }

    /// <summary>The parcel it stands on, if known.</summary>
    public long? LandParcelId { get; private set; }

    /// <summary>Occupation after this date does not qualify for regularisation.</summary>
    public DateOnly CutOffDate { get; private set; }

    /// <summary>The approved upgrading plan.</summary>
    public string? UpgradingPlan { get; private set; }

    /// <summary>When the upgrading plan was approved.</summary>
    public DateOnly? PlanApprovedOn { get; private set; }

    /// <summary>Records the approved upgrading plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="approvedOn">When it was approved.</param>
    public void ApprovePlan(string plan, DateOnly approvedOn)
    {
        UpgradingPlan = plan;
        PlanApprovedOn = approvedOn;
    }
}

/// <summary>An enumeration exercise: a household survey of a settlement (<c>hse.enumeration_exercise</c>).</summary>
public sealed class EnumerationExercise : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private EnumerationExercise()
    {
        Reference = string.Empty;
    }

    /// <summary>Starts an exercise.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="settlementId">The settlement.</param>
    /// <param name="reference">The exercise reference.</param>
    /// <param name="startedOn">When it started.</param>
    public EnumerationExercise(long companyId, long settlementId, string reference, DateOnly startedOn)
    {
        CompanyId = companyId;
        SettlementId = settlementId;
        Reference = reference;
        StartedOn = startedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The settlement.</summary>
    public long SettlementId { get; private set; }

    /// <summary>The exercise reference.</summary>
    public string Reference { get; private set; }

    /// <summary>When it started.</summary>
    public DateOnly StartedOn { get; private set; }

    /// <summary>When it closed; open while null.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Closes the exercise.</summary>
    /// <param name="date">When.</param>
    public void Close(DateOnly date) => ClosedOn = date;
}

/// <summary>
/// A household counted in an enumeration exercise (<c>hse.enumerated_household</c>): the head of household, the household,
/// the structure, whether it qualifies for regularisation, and what became of it — a waiting-list registration or a stand
/// allocated directly. A person is enumerated once per council, and a stand regularised to one household, in the database.
/// </summary>
public sealed class EnumeratedHousehold : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private EnumeratedHousehold()
    {
        HouseholdNumber = string.Empty;
        HeadIdNumber = string.Empty;
        HeadName = string.Empty;
        StructureReference = string.Empty;
    }

    /// <summary>Records a household.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="settlementId">The settlement.</param>
    /// <param name="exerciseId">The enumeration exercise.</param>
    /// <param name="householdNumber">The household number.</param>
    /// <param name="headIdNumber">The head of household's national ID (normalised).</param>
    /// <param name="headName">The head of household's name.</param>
    /// <param name="headDateOfBirth">The head of household's date of birth.</param>
    /// <param name="householdSize">The number of people in the household.</param>
    /// <param name="monthlyIncome">The household's monthly income.</param>
    /// <param name="occupiedSince">Since when the structure has been occupied.</param>
    /// <param name="structureType">The kind of structure.</param>
    /// <param name="structureReference">The structure's survey reference.</param>
    /// <param name="enumeratedOn">When it was counted.</param>
    public EnumeratedHousehold(long companyId, long settlementId, long exerciseId, string householdNumber, string headIdNumber, string headName, DateOnly headDateOfBirth,
        int householdSize, decimal monthlyIncome, DateOnly occupiedSince, StructureType structureType, string structureReference, DateOnly enumeratedOn)
    {
        CompanyId = companyId;
        SettlementId = settlementId;
        ExerciseId = exerciseId;
        HouseholdNumber = householdNumber;
        HeadIdNumber = headIdNumber;
        HeadName = headName;
        HeadDateOfBirth = headDateOfBirth;
        HouseholdSize = householdSize;
        MonthlyIncome = monthlyIncome;
        OccupiedSince = occupiedSince;
        StructureType = structureType;
        StructureReference = structureReference;
        EnumeratedOn = enumeratedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The settlement.</summary>
    public long SettlementId { get; private set; }

    /// <summary>The enumeration exercise.</summary>
    public long ExerciseId { get; private set; }

    /// <summary>The household number.</summary>
    public string HouseholdNumber { get; private set; }

    /// <summary>The head of household's national ID.</summary>
    public string HeadIdNumber { get; private set; }

    /// <summary>The head of household's name.</summary>
    public string HeadName { get; private set; }

    /// <summary>The head of household's date of birth.</summary>
    public DateOnly HeadDateOfBirth { get; private set; }

    /// <summary>The number of people in the household.</summary>
    public int HouseholdSize { get; private set; }

    /// <summary>The household's monthly income.</summary>
    public decimal MonthlyIncome { get; private set; }

    /// <summary>Since when the structure has been occupied.</summary>
    public DateOnly OccupiedSince { get; private set; }

    /// <summary>The kind of structure.</summary>
    public StructureType StructureType { get; private set; }

    /// <summary>The structure's survey reference.</summary>
    public string StructureReference { get; private set; }

    /// <summary>When it was counted.</summary>
    public DateOnly EnumeratedOn { get; private set; }

    /// <summary>Whether it qualifies for regularisation.</summary>
    public bool IsEligible { get; private set; }

    /// <summary>Why it does not qualify.</summary>
    public string? IneligibleReason { get; private set; }

    /// <summary>What became of it.</summary>
    public RegularisationOutcome Outcome { get; private set; }

    /// <summary>The waiting-list number it was registered under.</summary>
    public string? WaitingListNumber { get; private set; }

    /// <summary>The stand allocated to it on regularisation.</summary>
    public long? StandId { get; private set; }

    /// <summary>When it was dealt with.</summary>
    public DateOnly? OutcomeDate { get; private set; }

    /// <summary>Records the eligibility assessment.</summary>
    /// <param name="problems">Why it does not qualify; empty when it does.</param>
    public void Assess(IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        IsEligible = problems.Count == 0;
        IneligibleReason = IsEligible ? null : string.Join(" ", problems);
    }

    /// <summary>Records its registration on the waiting list.</summary>
    /// <param name="waitingListNumber">The waiting-list number.</param>
    /// <param name="date">When.</param>
    public void RegisterOnWaitingList(string waitingListNumber, DateOnly date)
    {
        Outcome = RegularisationOutcome.WaitingList;
        WaitingListNumber = waitingListNumber;
        OutcomeDate = date;
    }

    /// <summary>Records the stand allocated to it on regularisation.</summary>
    /// <param name="standId">The stand.</param>
    /// <param name="date">When.</param>
    public void Regularise(long standId, DateOnly date)
    {
        Outcome = RegularisationOutcome.Regularised;
        StandId = standId;
        OutcomeDate = date;
    }
}
