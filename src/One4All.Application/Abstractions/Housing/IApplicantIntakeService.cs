using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>An applicant's contact and intake details (blank values clear the field).</summary>
public sealed record ApplicantContact(Core.Payroll.Gender Gender, string? PhysicalAddress, string? PostalAddress, string? Phone, string? Employer);

/// <summary>A suspected duplicate awaiting an officer, or decided.</summary>
public sealed record DuplicateCaseView(long Id, string FirstSource, string FirstReference, string SecondSource, string SecondReference, string Reason, DuplicateCaseStatus Status, string? DecisionReason);

/// <summary>What a duplicate scan found.</summary>
/// <param name="Opened">New cases opened.</param>
/// <param name="Deceased">Applicants found on the deceased register.</param>
public sealed record DuplicateScan(int Opened, int Deceased);

/// <summary>
/// Waiting-list intake (brief §4.10.3): the applicant's gender, addresses, contacts and employer; supporting documents lodged and verified one by one with the
/// officer's name (the applicant is verified once every document is); and the duplicate queue — a scan across the waiting list, stand holders, property
/// owners and the deceased register opens cases an officer merges, rejects or clears with a reason.
/// </summary>
public interface IApplicantIntakeService
{
    /// <summary>Sets an applicant's contact and intake details.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="waitingListNumber">The applicant.</param>
    /// <param name="contact">The details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetContactAsync(long companyId, string waitingListNumber, ApplicantContact contact, CancellationToken ct = default);

    /// <summary>Lodges a supporting document.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="waitingListNumber">The applicant.</param>
    /// <param name="type">The kind of document.</param>
    /// <param name="description">What it is.</param>
    /// <param name="lodgedOn">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document id, or why it was refused.</returns>
    Task<Result<long>> LodgeDocumentAsync(long companyId, string waitingListNumber, ApplicantDocumentType type, string description, DateOnly lodgedOn, CancellationToken ct = default);

    /// <summary>Verifies a document; when every document of the applicant is verified, so is the applicant.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="documentId">The document.</param>
    /// <param name="officer">The verifying officer.</param>
    /// <param name="on">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether the applicant is now verified, or why it was refused.</returns>
    Task<Result<bool>> VerifyDocumentAsync(long companyId, long documentId, string officer, DateOnly on, CancellationToken ct = default);

    /// <summary>Adds a death to the deceased register.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="idNumber">The national ID.</param>
    /// <param name="fullName">The name.</param>
    /// <param name="diedOn">The date of death.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RecordDeathAsync(long companyId, string idNumber, string fullName, DateOnly diedOn, CancellationToken ct = default);

    /// <summary>Scans for suspected duplicates and deceased applicants and opens a case for each one not already in the queue.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was found.</returns>
    Task<DuplicateScan> ScanAsync(long companyId, DateOnly asAt, CancellationToken ct = default);

    /// <summary>The duplicate queue (open cases first).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cases.</returns>
    Task<IReadOnlyList<DuplicateCaseView>> QueueAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Decides a case: Merged removes the later of two waiting-list registrations (the earlier keeps its seniority); Rejected removes the second record's
    /// registration; Cleared marks them as different people. A reason is required.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="caseId">The case.</param>
    /// <param name="decision">Merged, Rejected or Cleared.</param>
    /// <param name="reason">Why.</param>
    /// <param name="userId">The officer.</param>
    /// <param name="on">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> DecideAsync(long companyId, long caseId, DuplicateCaseStatus decision, string reason, long userId, DateOnly on, CancellationToken ct = default);
}
