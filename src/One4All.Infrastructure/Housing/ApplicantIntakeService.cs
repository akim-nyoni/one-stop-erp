using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Waiting-list intake: contact details, document verification and the duplicate queue.</summary>
public sealed class ApplicantIntakeService : IApplicantIntakeService
{
    private const string DeceasedSource = "Deceased register";
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ApplicantIntakeService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetContactAsync(long companyId, string waitingListNumber, ApplicantContact contact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        HousingApplicant? applicant = await ApplicantAsync(companyId, waitingListNumber, ct).ConfigureAwait(false);
        if (applicant is null)
        {
            return Result.Failure(NotFound(waitingListNumber));
        }

        applicant.Gender = contact.Gender;
        applicant.PhysicalAddress = Clean(contact.PhysicalAddress);
        applicant.PostalAddress = Clean(contact.PostalAddress);
        applicant.Phone = Clean(contact.Phone);
        applicant.Employer = Clean(contact.Employer);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> LodgeDocumentAsync(long companyId, string waitingListNumber, ApplicantDocumentType type, string description, DateOnly lodgedOn, CancellationToken ct = default)
    {
        HousingApplicant? applicant = await ApplicantAsync(companyId, waitingListNumber, ct).ConfigureAwait(false);
        if (applicant is null)
        {
            return NotFound(waitingListNumber);
        }

        ApplicantDocument document = new(companyId, applicant.Id, type, string.IsNullOrWhiteSpace(description) ? type.ToString() : description.Trim(), lodgedOn);
        _db.Set<ApplicantDocument>().Add(document);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return document.Id;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> VerifyDocumentAsync(long companyId, long documentId, string officer, DateOnly on, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(officer))
        {
            return Error.Validation("Hse.Officer", "Say which officer verified the document.");
        }

        ApplicantDocument? document = await _db.Set<ApplicantDocument>().FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Id == documentId, ct).ConfigureAwait(false);
        if (document is null)
        {
            return Error.NotFound("Hse.DocumentNotFound", $"Document {documentId} not found.");
        }

        if (document.IsVerified)
        {
            return Error.Conflict("Hse.DocumentVerified", $"Document {documentId} was verified by {document.VerifiedBy} on {document.VerifiedOn:yyyy-MM-dd}.");
        }

        document.Verify(officer.Trim(), on);
        bool allVerified = await _db.Set<ApplicantDocument>().Where(d => d.ApplicantId == document.ApplicantId && d.Id != document.Id).AllAsync(d => d.VerifiedOn != null, ct).ConfigureAwait(false);
        if (allVerified)
        {
            HousingApplicant applicant = await _db.HousingApplicants.FirstAsync(a => a.Id == document.ApplicantId, ct).ConfigureAwait(false);
            applicant.Verify();
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return allVerified;
    }

    /// <inheritdoc />
    public async Task<Result> RecordDeathAsync(long companyId, string idNumber, string fullName, DateOnly diedOn, CancellationToken ct = default)
    {
        string id = DuplicateApplicants.NormaliseId(idNumber ?? string.Empty);
        if (id.Length == 0 || string.IsNullOrWhiteSpace(fullName))
        {
            return Result.Failure(Error.Validation("Hse.Deceased", "A death needs the national ID and the name."));
        }

        if (await _db.Set<DeceasedPerson>().AnyAsync(d => d.CompanyId == companyId && d.IdNumber == id, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.DeceasedExists", $"ID {id} is already on the deceased register."));
        }

        _db.Set<DeceasedPerson>().Add(new DeceasedPerson(companyId, id, fullName.Trim(), diedOn));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<DuplicateScan> ScanAsync(long companyId, DateOnly asAt, CancellationToken ct = default)
    {
        List<PersonRecord> people = [.. (await _db.HousingApplicants.AsNoTracking().Where(a => a.CompanyId == companyId && a.Status != ApplicantStatus.Removed)
                .Select(a => new { a.WaitingListNumber, a.IdNumber, a.FullName, a.DateOfBirth, a.Phone }).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => new PersonRecord(DuplicateApplicants.WaitingListSource, a.WaitingListNumber ?? a.IdNumber, a.FullName, a.IdNumber, a.DateOfBirth, a.Phone))];
        people.AddRange((await (from s in _db.HousingStands.AsNoTracking() join c in _db.Customers.AsNoTracking() on s.CurrentHolderCustomerId equals c.Id
                                where s.CompanyId == companyId select new { s.StandNumber, c.Name, c.NationalIdNumber }).ToListAsync(ct).ConfigureAwait(false))
            .Select(h => new PersonRecord("Stand holder", h.StandNumber, h.Name, h.NationalIdNumber, null)));
        List<DuplicateMatch> matches = [.. DuplicateApplicants.Find(people).Where(m => m.First.Source == DuplicateApplicants.WaitingListSource || m.Second.Source == DuplicateApplicants.WaitingListSource)];

        // Applicants on the deceased register.
        Dictionary<string, DeceasedPerson> deceased = (await _db.Set<DeceasedPerson>().AsNoTracking().Where(d => d.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(d => d.IdNumber);
        int dead = 0;
        foreach (PersonRecord applicant in people.Where(p => p.Source == DuplicateApplicants.WaitingListSource && p.IdNumber is not null))
        {
            if (deceased.TryGetValue(DuplicateApplicants.NormaliseId(applicant.IdNumber!), out DeceasedPerson? death))
            {
                matches.Add(new DuplicateMatch(applicant, new PersonRecord(DeceasedSource, death.IdNumber, death.FullName, death.IdNumber, null), $"On the deceased register (died {death.DiedOn:yyyy-MM-dd})"));
                dead++;
            }
        }

        HashSet<(string, string, string, string)> known = [.. (await _db.Set<DuplicateCase>().AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(c => new[] { (c.FirstSource, c.FirstReference, c.SecondSource, c.SecondReference), (c.SecondSource, c.SecondReference, c.FirstSource, c.FirstReference) })];
        int opened = 0;
        foreach (DuplicateMatch match in matches.Where(m => !known.Contains((m.First.Source, m.First.Reference, m.Second.Source, m.Second.Reference))))
        {
            _db.Set<DuplicateCase>().Add(new DuplicateCase(companyId, match.First.Source, match.First.Reference, match.Second.Source, match.Second.Reference, match.Reason, asAt));
            known.Add((match.First.Source, match.First.Reference, match.Second.Source, match.Second.Reference));
            opened++;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DuplicateScan(opened, dead);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DuplicateCaseView>> QueueAsync(long companyId, CancellationToken ct = default)
        => [.. (await _db.Set<DuplicateCase>().AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.Status).ThenBy(c => c.Id).Take(1000).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => new DuplicateCaseView(c.Id, c.FirstSource, c.FirstReference, c.SecondSource, c.SecondReference, c.Reason, c.Status, c.DecisionReason))];

    /// <inheritdoc />
    public async Task<Result> DecideAsync(long companyId, long caseId, DuplicateCaseStatus decision, string reason, long userId, DateOnly on, CancellationToken ct = default)
    {
        if (decision == DuplicateCaseStatus.Open || string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Hse.Decision", "A case is merged, rejected or cleared, with a reason."));
        }

        DuplicateCase? found = await _db.Set<DuplicateCase>().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Id == caseId, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Result.Failure(Error.NotFound("Hse.CaseNotFound", $"Duplicate case {caseId} not found."));
        }

        if (found.Status != DuplicateCaseStatus.Open)
        {
            return Result.Failure(Error.Conflict("Hse.CaseDecided", $"Case {caseId} was already {found.Status.ToString().ToLowerInvariant()}."));
        }

        HousingApplicant? Listed(string source, string reference, List<HousingApplicant> applicants)
            => source == DuplicateApplicants.WaitingListSource ? applicants.FirstOrDefault(a => a.WaitingListNumber == reference || (a.WaitingListNumber == null && a.IdNumber == reference)) : null;
        List<string> references = [found.FirstReference, found.SecondReference];
        List<HousingApplicant> involved = await _db.HousingApplicants.Where(a => a.CompanyId == companyId && (references.Contains(a.WaitingListNumber!) || references.Contains(a.IdNumber)))
            .ToListAsync(ct).ConfigureAwait(false);
        HousingApplicant? first = Listed(found.FirstSource, found.FirstReference, involved), second = Listed(found.SecondSource, found.SecondReference, involved);

        HousingApplicant? remove = decision switch
        {
            DuplicateCaseStatus.Merged when first is not null && second is not null => first.RegistrationDate <= second.RegistrationDate ? second : first,
            DuplicateCaseStatus.Merged => null,
            DuplicateCaseStatus.Rejected => second ?? first,
            _ => null,
        };
        if (decision == DuplicateCaseStatus.Merged && remove is null)
        {
            return Result.Failure(Error.Rule("Hse.CannotMerge", "Only two waiting-list registrations can be merged; reject or clear this case instead."));
        }

        if (remove is not null && remove.Status != ApplicantStatus.Removed)
        {
            remove.SetStatus(ApplicantStatus.Removed);
            _db.Set<ApplicantNotice>().Add(new ApplicantNotice(companyId, remove.Id, ApplicantNoticeType.Removed, on));
        }

        found.Decide(decision, reason.Trim(), userId, on);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<HousingApplicant?> ApplicantAsync(long companyId, string waitingListNumber, CancellationToken ct)
    {
        string key = (waitingListNumber ?? string.Empty).Trim().ToUpperInvariant();
        return _db.HousingApplicants.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.WaitingListNumber == key, ct);
    }

    private static Error NotFound(string waitingListNumber) => Error.NotFound("Hse.ApplicantNotFound", $"Applicant {waitingListNumber} not found.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
