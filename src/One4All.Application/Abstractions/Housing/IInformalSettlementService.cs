using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A settlement to register.</summary>
public sealed class SettlementRegistration
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The settlement code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Its name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The ward.</summary>
    public string Ward { get; init; } = string.Empty;

    /// <summary>The parcel it stands on, if known.</summary>
    public long? LandParcelId { get; init; }

    /// <summary>Occupation after this date does not qualify for regularisation.</summary>
    public DateOnly CutOffDate { get; init; }
}

/// <summary>One household as an enumerator recorded it.</summary>
public sealed class HouseholdSurvey
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The settlement.</summary>
    public string SettlementCode { get; init; } = string.Empty;

    /// <summary>The head of household's national ID.</summary>
    public string HeadIdNumber { get; init; } = string.Empty;

    /// <summary>The head of household's name.</summary>
    public string HeadName { get; init; } = string.Empty;

    /// <summary>The head of household's date of birth.</summary>
    public DateOnly HeadDateOfBirth { get; init; }

    /// <summary>The number of people in the household.</summary>
    public int HouseholdSize { get; init; }

    /// <summary>The household's monthly income.</summary>
    public decimal MonthlyIncome { get; init; }

    /// <summary>Since when the structure has been occupied.</summary>
    public DateOnly OccupiedSince { get; init; }

    /// <summary>The kind of structure.</summary>
    public StructureType StructureType { get; init; }

    /// <summary>The structure's survey reference.</summary>
    public string StructureReference { get; init; } = string.Empty;

    /// <summary>When it was counted.</summary>
    public DateOnly Date { get; init; }
}

/// <summary>A household as counted.</summary>
/// <param name="HouseholdNumber">The household number.</param>
/// <param name="IsEligible">Whether it qualifies for regularisation.</param>
/// <param name="IneligibleReason">Why it does not.</param>
public sealed record EnumerationResult(string HouseholdNumber, bool IsEligible, string? IneligibleReason);

/// <summary>Where a settlement's regularisation stands.</summary>
/// <param name="Code">The settlement code.</param>
/// <param name="Name">Its name.</param>
/// <param name="PlanApprovedOn">When its upgrading plan was approved, if it has been.</param>
/// <param name="Enumerated">Households counted.</param>
/// <param name="Eligible">Of which qualify.</param>
/// <param name="WaitingListed">Registered on the waiting list.</param>
/// <param name="Regularised">Allocated a stand directly.</param>
/// <param name="Outstanding">Eligible households not yet dealt with.</param>
public sealed record SettlementSummary(string Code, string Name, DateOnly? PlanApprovedOn, int Enumerated, int Eligible, int WaitingListed, int Regularised, int Outstanding);

/// <summary>
/// Informal settlements and regularisation (brief §4.10.8): the settlement register with its cut-off date and upgrading
/// plan; enumeration exercises recording households, structures and occupants with an eligibility assessment; and the
/// conversion of an eligible household into a waiting-list registration or a direct allocation of a stand - with the same
/// anti-double-allocation checks as the rest of the housing module.
/// </summary>
public interface IInformalSettlementService
{
    /// <summary>Registers a settlement.</summary>
    /// <param name="registration">The settlement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Its id, or why it was refused.</returns>
    Task<Result<long>> RegisterAsync(SettlementRegistration registration, CancellationToken ct = default);

    /// <summary>Records a settlement's approved upgrading plan (required before households are regularised).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="settlementCode">The settlement.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="approvedOn">When it was approved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApprovePlanAsync(long companyId, string settlementCode, string plan, DateOnly approvedOn, CancellationToken ct = default);

    /// <summary>Starts an enumeration exercise; a settlement has one open at a time.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="settlementCode">The settlement.</param>
    /// <param name="reference">The exercise reference.</param>
    /// <param name="date">When it starts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The exercise id, or why it was refused.</returns>
    Task<Result<long>> StartEnumerationAsync(long companyId, string settlementCode, string reference, DateOnly date, CancellationToken ct = default);

    /// <summary>Records a household in the open exercise and assesses its eligibility; a person is enumerated once.</summary>
    /// <param name="survey">The household.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The household number and assessment, or why it was refused.</returns>
    Task<Result<EnumerationResult>> EnumerateAsync(HouseholdSurvey survey, CancellationToken ct = default);

    /// <summary>Closes the open exercise.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="settlementCode">The settlement.</param>
    /// <param name="date">When it closes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of households it counted, or why it was refused.</returns>
    Task<Result<int>> CloseEnumerationAsync(long companyId, string settlementCode, DateOnly date, CancellationToken ct = default);

    /// <summary>Registers an eligible household on the waiting list (with the waiting list's own checks).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="householdNumber">The household.</param>
    /// <param name="category">The waiting-list category.</param>
    /// <param name="date">The registration date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The waiting-list number, or why it was refused.</returns>
    Task<Result<string>> ConvertToWaitingListAsync(long companyId, string householdNumber, HousingCategory category, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Allocates a stand directly to an eligible household on regularisation, once the settlement's upgrading plan is
    /// approved; refused when the stand is not available or already allocated, or the person already holds a stand,
    /// an allocation or council property.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="householdNumber">The household.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RegulariseAsync(long companyId, string householdNumber, long standId, DateOnly date, CancellationToken ct = default);

    /// <summary>Where a settlement's regularisation stands.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="settlementCode">The settlement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The summary, or why there is none.</returns>
    Task<Result<SettlementSummary>> SummaryAsync(long companyId, string settlementCode, CancellationToken ct = default);
}
