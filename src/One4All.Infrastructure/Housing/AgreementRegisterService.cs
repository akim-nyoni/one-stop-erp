using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>The housing agreement register: versioned, hashed agreement documents.</summary>
public sealed class AgreementRegisterService : IAgreementRegisterService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    public AgreementRegisterService(One4AllDbContext db, INumberSequenceService numbers)
    {
        _db = db;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public async Task<Result<string>> DraftAsync(AgreementDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Price < 0m || string.IsNullOrWhiteSpace(draft.Terms))
        {
            return Error.Validation("Hse.AgreementTerms", "An agreement needs terms and a price of zero or more.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == draft.PartyCustomerId && c.CompanyId == draft.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.PartyNotFound", $"Customer {draft.PartyCustomerId} not found.");
        }

        if (draft.StandId is long standId && !await _db.HousingStands.AnyAsync(s => s.Id == standId && s.CompanyId == draft.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found.");
        }

        Result<string> number = await _numbers.AllocateAsync(draft.CompanyId, "AGR", draft.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        HousingAgreement agreement = new(draft.CompanyId, number.Value, draft.AgreementType, draft.PartyCustomerId, draft.StandId);
        agreement.AddVersion(await VersionAsync(agreement, 1, draft.Price, draft.Terms.Trim(), Trimmed(draft.SpecialConditions), null, ct).ConfigureAwait(false));
        _db.Set<HousingAgreement>().Add(agreement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result> SignAsync(long companyId, string agreementNumber, DateOnly date, IReadOnlyList<string> witnesses, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(witnesses);
        List<string> named = [.. witnesses.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (named.Count < 2)
        {
            return Result.Failure(Error.Validation("Hse.Witnesses", "A signature needs at least two different witnesses."));
        }

        Result<HousingAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        AgreementVersion latest = found.Value.Latest;
        if (latest.SignedOn is not null)
        {
            return Result.Failure(Error.Rule("Hse.AlreadySigned", $"Version {latest.Version} of {agreementNumber} is already signed; amend it to make changes."));
        }

        latest.Sign(date, string.Join("; ", named));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> AmendAsync(long companyId, string agreementNumber, decimal price, string terms, string? specialConditions, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(terms) || price < 0m)
        {
            return Error.Validation("Hse.Amendment", "An amendment needs a reason, terms and a price of zero or more.");
        }

        Result<HousingAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        HousingAgreement agreement = found.Value;
        if (agreement.Latest.SignedOn is null)
        {
            return Error.Rule("Hse.LatestUnsigned", $"Version {agreement.Latest.Version} of {agreementNumber} is not yet signed; sign or replace it first.");
        }

        int next = agreement.Latest.Version + 1;
        agreement.AddVersion(await VersionAsync(agreement, next, price, terms.Trim(), Trimmed(specialConditions), reason.Trim(), ct).ConfigureAwait(false));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return next;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AgreementVersionEntry>>> HistoryAsync(long companyId, string agreementNumber, CancellationToken ct = default)
    {
        Result<HousingAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        int? inForce = found.Value.InForce?.Version;
        return Result<IReadOnlyList<AgreementVersionEntry>>.Success([.. found.Value.Versions.Select(v =>
            new AgreementVersionEntry(v.Version, v.Price, v.AmendmentReason, v.SignedOn, v.Witnesses, v.Version == inForce, v.Document))]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> VerifyDocumentsAsync(long companyId, CancellationToken ct = default)
    {
        var versions = await (
            from v in _db.Set<AgreementVersion>().AsNoTracking()
            join a in _db.Set<HousingAgreement>().AsNoTracking() on v.HousingAgreementId equals a.Id
            where a.CompanyId == companyId
            orderby a.AgreementNumber, v.Version
            select new { a.AgreementNumber, v.Version, v.Document, v.DocumentHash }).ToListAsync(ct).ConfigureAwait(false);
        return [.. versions.Where(v => AgreementDocument.Hash(v.Document) != v.DocumentHash).Select(v => $"{v.AgreementNumber} v{v.Version}")];
    }

    // Generates a version's document from the agreement, the parties and the stand.
    private async Task<AgreementVersion> VersionAsync(HousingAgreement agreement, int version, decimal price, string terms, string? conditions, string? reason, CancellationToken ct)
    {
        string council = await _db.Set<Company>().Where(c => c.Id == agreement.CompanyId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        string party = await _db.Customers.Where(c => c.Id == agreement.PartyCustomerId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        string? stand = agreement.StandId is long standId
            ? await _db.HousingStands.Where(s => s.Id == standId).Select(s => s.StandNumber).FirstAsync(ct).ConfigureAwait(false)
            : null;
        string document = AgreementDocument.Render(new AgreementContent(agreement.AgreementNumber, version, agreement.AgreementType.ToString(), council, party, stand, price, terms, conditions));
        return new AgreementVersion(version, price, terms, conditions, reason, document, AgreementDocument.Hash(document));
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Result<HousingAgreement>> FindAsync(long companyId, string agreementNumber, CancellationToken ct)
    {
        string number = (agreementNumber ?? string.Empty).Trim();
        HousingAgreement? agreement = await _db.Set<HousingAgreement>().Include(a => a.Versions)
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AgreementNumber == number, ct).ConfigureAwait(false);
        if (agreement is null)
        {
            return Error.NotFound("Hse.AgreementNotFound", $"Agreement {number} not found.");
        }

        return agreement;
    }
}
