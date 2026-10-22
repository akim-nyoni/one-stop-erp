using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>An application to join the housing waiting list.</summary>
public sealed class WaitingListApplication
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The national ID number.</summary>
    public string IdNumber { get; init; } = string.Empty;

    /// <summary>The applicant's full name.</summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>The waiting-list category applied for.</summary>
    public HousingCategory Category { get; init; }

    /// <summary>The date of birth.</summary>
    public DateOnly DateOfBirth { get; init; }

    /// <summary>Since when resident in the area.</summary>
    public DateOnly ResidentSince { get; init; }

    /// <summary>The number of dependants.</summary>
    public int Dependants { get; init; }

    /// <summary>Whether the applicant has a disability.</summary>
    public bool HasDisability { get; init; }

    /// <summary>The monthly income.</summary>
    public decimal MonthlyIncome { get; init; }

    /// <summary>Where the applicant lives now.</summary>
    public CurrentTenure Tenure { get; init; }

    /// <summary>The registration date (it sets seniority): leave it unset for today, the day of capture; an earlier date is refused (register today and request a registration date change).</summary>
    public DateOnly RegistrationDate { get; init; }

    /// <summary>The marital status.</summary>
    public MaritalStatus MaritalStatus { get; init; } = MaritalStatus.Single;

    /// <summary>The marriage regime, when married.</summary>
    public MaritalRegime? MaritalRegime { get; init; }

    /// <summary>The spouse / co-applicant's national ID.</summary>
    public string? SpouseIdNumber { get; init; }

    /// <summary>The spouse / co-applicant's name.</summary>
    public string? SpouseName { get; init; }
}

/// <summary>A council's waiting-list policy as entered.</summary>
/// <param name="Terms">The qualification thresholds and score weights.</param>
public sealed record WaitingListPolicyInput(WaitingListTerms Terms);

/// <summary>A registration on the list.</summary>
/// <param name="WaitingListNumber">The waiting-list number.</param>
/// <param name="Score">The priority score at registration.</param>
public sealed record WaitingListRegistration(string WaitingListNumber, PriorityScore Score);

/// <summary>An applicant's place on the list.</summary>
/// <param name="Position">The place in the category, from 1.</param>
/// <param name="WaitingListNumber">The waiting-list number.</param>
/// <param name="FullName">The applicant.</param>
/// <param name="RegistrationDate">The registration date.</param>
/// <param name="Score">The priority score.</param>
public sealed record WaitingListPlace(int Position, string WaitingListNumber, string FullName, DateOnly RegistrationDate, decimal Score);

/// <summary>How a waiting list is ordered.</summary>
public enum WaitingListOrder
{
    /// <summary>By registration date, earliest first.</summary>
    Seniority = 1,

    /// <summary>By priority score, highest first (seniority breaks ties).</summary>
    Score = 2,
}

/// <summary>What an applicant (or the self-service front-end) sees on enquiry.</summary>
/// <param name="WaitingListNumber">The waiting-list number.</param>
/// <param name="Category">The category.</param>
/// <param name="Status">Where the applicant stands.</param>
/// <param name="PositionBySeniority">The place in the category by seniority, when active.</param>
/// <param name="Score">The priority score with its explanation.</param>
public sealed record WaitingListEnquiry(string WaitingListNumber, HousingCategory Category, ApplicantStatus Status, int? PositionBySeniority, PriorityScore Score);

/// <summary>
/// The housing waiting list (brief §4.10.3): a council's qualification rules and score weights; registration with a
/// unique waiting-list number after duplicate-ID, ownership and qualification checks; the list ranked by seniority or
/// score; and the applicant enquiry with a printable score explanation.
/// </summary>
public interface IWaitingListService
{
    /// <summary>Sets the council's waiting-list policy.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="input">The rules and weights.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetPolicyAsync(long companyId, WaitingListPolicyInput input, CancellationToken ct = default);

    /// <summary>The council's waiting-list thresholds and weights (the defaults until it sets its own).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The terms.</returns>
    Task<WaitingListTerms> PolicyAsync(long companyId, CancellationToken ct = default);

    /// <summary>Sets the further qualification rules: first-time buyers only, refusing forfeiters, rates clearance and the income ceiling per category.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="rules">The rules.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetQualificationRulesAsync(long companyId, QualificationRules rules, CancellationToken ct = default);

    /// <summary>The further qualification rules in force.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rules.</returns>
    Task<QualificationRules> QualificationRulesAsync(long companyId, CancellationToken ct = default);

    /// <summary>Registers an applicant, refusing a duplicate ID, an existing owner or someone who does not qualify.</summary>
    /// <param name="application">The application.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The registration, or why it was refused.</returns>
    Task<Result<WaitingListRegistration>> RegisterAsync(WaitingListApplication application, CancellationToken ct = default);

    /// <summary>The active applicants in a category, ranked.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="category">The category.</param>
    /// <param name="order">By seniority or by score.</param>
    /// <param name="asAtDate">The date scores are worked out at.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ranked list.</returns>
    Task<IReadOnlyList<WaitingListPlace>> RankingAsync(long companyId, HousingCategory category, WaitingListOrder order, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>An applicant's enquiry by waiting-list number or national ID.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="waitingListNumberOrId">The waiting-list number or national ID number.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The enquiry, or why there is none.</returns>
    Task<Result<WaitingListEnquiry>> EnquireAsync(long companyId, string waitingListNumberOrId, DateOnly asAtDate, CancellationToken ct = default);
}
