using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Encumbrances, land disputes and disposals / exchanges of land (brief §14 H6–H8).</summary>
public sealed class LandRegisterService : ILandRegisterService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    /// <param name="numbers">Document numbering.</param>
    public LandRegisterService(One4AllDbContext db, IPostingEngine posting, INumberSequenceService numbers)
    {
        _db = db;
        _posting = posting;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RegisterEncumbranceAsync(EncumbranceRegistration registration, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(registration.Type) || string.IsNullOrWhiteSpace(registration.Reference) || string.IsNullOrWhiteSpace(registration.InFavourOf))
        {
            return Error.Validation("Hse.EncumbranceDetails", "An encumbrance needs a type, a registration reference and the holder of the right.");
        }

        LandParcel? parcel = await ParcelAsync(registration.CompanyId, registration.ParcelNumber, ct).ConfigureAwait(false);
        if (parcel is null)
        {
            return Error.NotFound("Hse.ParcelNotFound", $"Parcel {registration.ParcelNumber} not found.");
        }

        LandEncumbrance encumbrance = new(registration.CompanyId, parcel.Id, registration.Type, registration.Reference.Trim(), registration.InFavourOf.Trim(),
            registration.Description.Trim(), registration.Date);
        _db.Set<LandEncumbrance>().Add(encumbrance);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return encumbrance.Id;
    }

    /// <inheritdoc />
    public async Task<Result> LiftEncumbranceAsync(long companyId, long encumbranceId, DateOnly date, string reference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Result.Failure(Error.Validation("Hse.LiftDetails", "Lifting an encumbrance needs the cancelling document."));
        }

        LandEncumbrance? encumbrance = await _db.Set<LandEncumbrance>().FirstOrDefaultAsync(e => e.Id == encumbranceId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (encumbrance is null)
        {
            return Result.Failure(Error.NotFound("Hse.EncumbranceNotFound", $"Encumbrance {encumbranceId} not found."));
        }

        if (encumbrance.LiftedOn is not null)
        {
            return Result.Failure(Error.Conflict("Hse.EncumbranceLifted", $"Encumbrance {encumbrance.Reference} was already lifted."));
        }

        if (date < encumbrance.RegisteredOn)
        {
            return Result.Failure(Error.Validation("Hse.LiftDate", "An encumbrance cannot be lifted before it was registered."));
        }

        encumbrance.Lift(date, reference.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<string>> OpenDisputeAsync(LandDisputeLodgement lodgement, CancellationToken ct = default)
        => _db.AtomicAsync(() => OpenDisputeCoreAsync(lodgement, ct), ct);

    private async Task<Result<string>> OpenDisputeCoreAsync(LandDisputeLodgement lodgement, CancellationToken ct)
    {
        if (!Enum.IsDefined(lodgement.Type) || string.IsNullOrWhiteSpace(lodgement.Claimant) || string.IsNullOrWhiteSpace(lodgement.Particulars))
        {
            return Error.Validation("Hse.DisputeDetails", "A dispute needs a type, the claimant and its particulars.");
        }

        LandParcel? parcel = await ParcelAsync(lodgement.CompanyId, lodgement.ParcelNumber, ct).ConfigureAwait(false);
        if (parcel is null)
        {
            return Error.NotFound("Hse.ParcelNotFound", $"Parcel {lodgement.ParcelNumber} not found.");
        }

        long? standId = null;
        if (!string.IsNullOrWhiteSpace(lodgement.StandNumber))
        {
            standId = await _db.HousingStands.Where(s => s.CompanyId == lodgement.CompanyId && s.LandParcelId == parcel.Id && s.StandNumber == lodgement.StandNumber.Trim())
                .Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (standId is null)
            {
                return Error.NotFound("Hse.StandNotFound", $"Stand {lodgement.StandNumber} is not on parcel {parcel.ParcelNumber}.");
            }
        }

        Result<string> number = await _numbers.AllocateAsync(lodgement.CompanyId, "LD", lodgement.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<LandDispute>().Add(new LandDispute(lodgement.CompanyId, number.Value, parcel.Id, standId, lodgement.Type, lodgement.Claimant.Trim(), lodgement.Particulars.Trim(), lodgement.Date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result> AdvanceDisputeAsync(long companyId, string disputeNumber, LandDisputeStage stage, DateOnly date, string note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return Result.Failure(Error.Validation("Hse.DisputeNote", "Say what happened at this stage."));
        }

        LandDispute? dispute = await _db.Set<LandDispute>().Include(d => d.Steps)
            .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.DisputeNumber == disputeNumber, ct).ConfigureAwait(false);
        if (dispute is null)
        {
            return Result.Failure(Error.NotFound("Hse.DisputeNotFound", $"Dispute {disputeNumber} not found."));
        }

        if (!LandRegisterRules.CanMove(dispute.Stage, stage))
        {
            return Result.Failure(Error.Rule("Hse.DisputeStage", $"Dispute {disputeNumber} is at {dispute.Stage} and cannot move to {stage}."));
        }

        if (date < dispute.StageSince)
        {
            return Result.Failure(Error.Validation("Hse.DisputeDate", $"Dispute {disputeNumber} reached {dispute.Stage} on {dispute.StageSince:yyyy-MM-dd}; a later stage cannot be earlier."));
        }

        dispute.MoveTo(stage, date, note.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<LandDisposalResult>> DisposeAsync(LandDisposalRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => DisposeCoreAsync(request, ct), ct);

    // One unit of work: the journal, the parcel's reduced area and cost, any exchange parcel and the register entry commit together.
    private async Task<Result<LandDisposalResult>> DisposeCoreAsync(LandDisposalRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Type) || string.IsNullOrWhiteSpace(request.Counterparty) || string.IsNullOrWhiteSpace(request.ResolutionReference))
        {
            return Error.Validation("Hse.DisposalDetails", "A disposal needs its type, the counterparty and the council resolution authorising it.");
        }

        bool exchange = request.Type == LandDisposalType.Exchange;
        bool cash = request.Type is LandDisposalType.Sale or LandDisposalType.Expropriation;
        if (request.Consideration < 0m || (request.Type == LandDisposalType.Donation && request.Consideration != 0m) || ((cash || exchange) && request.Consideration == 0m))
        {
            return Error.Validation("Hse.DisposalConsideration", "A sale, expropriation or exchange needs its consideration; a donation has none.");
        }

        if (exchange && (string.IsNullOrWhiteSpace(request.ExchangeParcelNumber) || request.ExchangeParcelArea <= 0m))
        {
            return Error.Validation("Hse.ExchangeParcel", "An exchange needs the number and area of the parcel received.");
        }

        LandParcel? parcel = await _db.Set<LandParcel>().FirstOrDefaultAsync(p => p.CompanyId == request.CompanyId && p.ParcelNumber == request.ParcelNumber.Trim().ToUpperInvariant(), ct).ConfigureAwait(false);
        if (parcel is null)
        {
            return Error.NotFound("Hse.ParcelNotFound", $"Parcel {request.ParcelNumber} not found.");
        }

        if (parcel.Status != LandParcelStatus.Acquired)
        {
            return Error.Rule("Hse.ParcelNotUndivided", $"Parcel {parcel.ParcelNumber} is {parcel.Status}; only undivided land in the land bank can be disposed of.");
        }

        decimal area = request.AreaSquareMetres == 0m ? parcel.AreaSquareMetres : request.AreaSquareMetres;
        if (area <= 0m || area > parcel.AreaSquareMetres)
        {
            return Error.Validation("Hse.DisposalArea", $"Parcel {parcel.ParcelNumber} has {parcel.AreaSquareMetres:N2} m² to dispose of.");
        }

        if (await _db.Set<LandEncumbrance>().AnyAsync(e => e.LandParcelId == parcel.Id && e.Type == EncumbranceType.Caveat && e.LiftedOn == null, ct).ConfigureAwait(false))
        {
            return Error.Rule("Hse.ParcelCaveat", $"Parcel {parcel.ParcelNumber} is under a caveat.");
        }

        if (await _db.Set<LandDispute>().AnyAsync(d => d.LandParcelId == parcel.Id && d.Type == LandDisputeType.Ownership && d.Stage != LandDisputeStage.Resolved, ct).ConfigureAwait(false))
        {
            return Error.Rule("Hse.ParcelDisputed", $"Parcel {parcel.ParcelNumber} has an open ownership dispute.");
        }

        LandParcel? received = null;
        if (exchange)
        {
            string number = request.ExchangeParcelNumber!.Trim().ToUpperInvariant();
            if (await _db.Set<LandParcel>().AnyAsync(p => p.CompanyId == request.CompanyId && p.ParcelNumber == number, ct).ConfigureAwait(false))
            {
                return Error.Conflict("Hse.ParcelExists", $"Parcel {number} is already in the land bank.");
            }

            received = new LandParcel(request.CompanyId, number, request.ExchangeParcelDescription?.Trim() ?? $"Received in exchange for {parcel.ParcelNumber}", request.ExchangeParcelArea, request.Consideration);
            _db.Set<LandParcel>().Add(received);
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Housing.CompanyNotFound", $"Company {request.CompanyId} not found.");
        }

        Result<string> disposalNumber = await _numbers.AllocateAsync(request.CompanyId, "LDS", request.Date, ct: ct).ConfigureAwait(false);
        if (disposalNumber.IsFailure)
        {
            return disposalNumber.Error;
        }

        decimal cost = LandRegisterRules.CostOfArea(parcel.AreaSquareMetres, parcel.AcquisitionCost, area);
        decimal gain = request.Consideration - cost;
        List<JournalLineRequest> lines = [new() { AccountCode = request.LandAccountCode, Side = JournalSide.Credit, TransactionAmount = cost, Narrative = $"Cost of {area:N2} m² of {parcel.ParcelNumber}" }];
        if (request.Consideration > 0m)
        {
            lines.Add(new JournalLineRequest
            {
                AccountCode = exchange ? request.LandAccountCode : request.ProceedsAccountCode, Side = JournalSide.Debit, TransactionAmount = request.Consideration,
                Narrative = exchange ? $"Parcel {received!.ParcelNumber} received at fair value" : $"{request.Type} consideration from {request.Counterparty.Trim()}",
            });
        }

        if (gain != 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = request.GainLossAccountCode, Side = gain > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(gain), Narrative = gain > 0m ? "Gain on disposal of land" : "Loss on disposal of land" });
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "GL",
            PostingDate = request.Date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "LAND-DSP",
            DocumentNumber = disposalNumber.Value,
            Narrative = $"Land {request.Type.ToString().ToLowerInvariant()} {parcel.ParcelNumber} to {request.Counterparty.Trim()} ({request.ResolutionReference.Trim()})",
            Lines = lines,
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        parcel.ReleaseArea(area, cost);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _db.Set<LandDisposal>().Add(new LandDisposal(request.CompanyId, disposalNumber.Value, parcel.Id, request.Type, request.Counterparty.Trim(), request.ResolutionReference.Trim(), request.Date,
            area, cost, request.Consideration, received?.Id, posted.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LandDisposalResult(disposalNumber.Value, posted.Value.JournalNumber, cost, gain, received?.Id);
    }

    private Task<LandParcel?> ParcelAsync(long companyId, string parcelNumber, CancellationToken ct)
    {
        string number = parcelNumber.Trim().ToUpperInvariant();
        return _db.Set<LandParcel>().AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.ParcelNumber == number, ct);
    }
}
