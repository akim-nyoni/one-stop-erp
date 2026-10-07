using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>IAS 37 compensation provisions on compulsory acquisition, capitalised into land under development.</summary>
public sealed class CompensationService : ICompensationService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="engine">The posting engine.</param>
    public CompensationService(One4AllDbContext db, INumberSequenceService numbers, IPostingEngine engine)
    {
        _db = db;
        _numbers = numbers;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<Result<string>> LodgeAsync(long companyId, long landParcelId, string claimant, DateOnly lodgedOn, string landCostAccountCode, string provisionAccountCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claimant) || string.IsNullOrWhiteSpace(landCostAccountCode) || string.IsNullOrWhiteSpace(provisionAccountCode))
        {
            return Error.Validation("Hse.ClaimDetails", "A claim needs a claimant and the land cost and provision accounts.");
        }

        if (!await _db.LandParcels.AnyAsync(p => p.Id == landParcelId && p.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.ParcelNotFound", $"Land parcel {landParcelId} not found.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "CMP", lodgedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<CompensationClaim>().Add(new CompensationClaim(companyId, number.Value, landParcelId, claimant.Trim(), lodgedOn, landCostAccountCode.Trim(), provisionAccountCode.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result<decimal>> ProvideAsync(long companyId, string claimNumber, decimal bestEstimate, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => ProvideCoreAsync(companyId, claimNumber, bestEstimate, date, ct), ct);

    private async Task<Result<decimal>> ProvideCoreAsync(long companyId, string claimNumber, decimal bestEstimate, DateOnly date, CancellationToken ct)
    {
        if (bestEstimate < 0m)
        {
            return Error.Validation("Hse.Estimate", "The best estimate cannot be negative.");
        }

        Result<CompensationClaim> found = await FindOpenAsync(companyId, claimNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        CompensationClaim claim = found.Value;
        decimal movement = bestEstimate - claim.Provision;
        if (movement != 0m)
        {
            // An increase capitalises more into the land; a decrease takes it back out.
            Result<PostingReceipt> posted = movement > 0m
                ? await PostAsync(companyId, date, $"{claim.ClaimNumber}-{date:yyyyMMdd}-{claim.Steps.Count}", $"Compensation provision for {claim.Claimant}", claim.LandCostAccountCode, claim.ProvisionAccountCode, movement, ct).ConfigureAwait(false)
                : await PostAsync(companyId, date, $"{claim.ClaimNumber}-{date:yyyyMMdd}-{claim.Steps.Count}", $"Compensation provision for {claim.Claimant} reduced", claim.ProvisionAccountCode, claim.LandCostAccountCode, -movement, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        claim.Provide(bestEstimate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return claim.Provision;
    }

    /// <inheritdoc />
    public Task<Result> SettleAsync(long companyId, string claimNumber, decimal paid, string bankAccountCode, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => SettleCoreAsync(companyId, claimNumber, paid, bankAccountCode, date, ct), ct);

    private async Task<Result> SettleCoreAsync(long companyId, string claimNumber, decimal paid, string bankAccountCode, DateOnly date, CancellationToken ct)
    {
        if (paid <= 0m || string.IsNullOrWhiteSpace(bankAccountCode))
        {
            return Result.Failure(Error.Validation("Hse.Settlement", "A settlement needs a positive payment and a bank account."));
        }

        Result<CompensationClaim> found = await FindOpenAsync(companyId, claimNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        CompensationClaim claim = found.Value;
        decimal difference = paid - claim.Provision;
        List<JournalLineRequest> lines = [new JournalLineRequest { AccountCode = bankAccountCode.Trim(), Side = JournalSide.Credit, TransactionAmount = paid }];
        if (claim.Provision > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = claim.ProvisionAccountCode, Side = JournalSide.Debit, TransactionAmount = claim.Provision });
        }

        if (difference != 0m)
        {
            // Paid more than provided: more land cost; paid less: the excess provision comes out of the land cost.
            lines.Add(new JournalLineRequest { AccountCode = claim.LandCostAccountCode, Side = difference > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(difference) });
        }

        Result<PostingReceipt> posted = await PostLinesAsync(companyId, date, $"{claim.ClaimNumber}-PAID", $"Compensation paid to {claim.Claimant}", lines, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return Result.Failure(posted.Error);
        }

        claim.Settle(paid);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> WithdrawAsync(long companyId, string claimNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => WithdrawCoreAsync(companyId, claimNumber, date, ct), ct);

    private async Task<Result> WithdrawCoreAsync(long companyId, string claimNumber, DateOnly date, CancellationToken ct)
    {
        Result<CompensationClaim> found = await FindOpenAsync(companyId, claimNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        CompensationClaim claim = found.Value;
        if (claim.Provision > 0m)
        {
            Result<PostingReceipt> released = await PostAsync(companyId, date, $"{claim.ClaimNumber}-REL", $"Compensation provision for {claim.Claimant} released",
                claim.ProvisionAccountCode, claim.LandCostAccountCode, claim.Provision, ct).ConfigureAwait(false);
            if (released.IsFailure)
            {
                return Result.Failure(released.Error);
            }
        }

        claim.Withdraw();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompensationEntry>> RegisterAsync(long companyId, CancellationToken ct = default)
        => await (
            from c in _db.Set<CompensationClaim>().AsNoTracking()
            join p in _db.LandParcels.AsNoTracking() on c.LandParcelId equals p.Id
            where c.CompanyId == companyId
            orderby c.ClaimNumber
            select new CompensationEntry(c.ClaimNumber, p.ParcelNumber, c.Claimant, c.Status, c.Provision, c.SettledAmount))
            .ToListAsync(ct).ConfigureAwait(false);

    private Task<Result<PostingReceipt>> PostAsync(long companyId, DateOnly date, string document, string narrative, string debitAccount, string creditAccount, decimal amount, CancellationToken ct)
        => PostLinesAsync(companyId, date, document, narrative,
        [
            new JournalLineRequest { AccountCode = debitAccount, Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = creditAccount, Side = JournalSide.Credit, TransactionAmount = amount },
        ], ct);

    private async Task<Result<PostingReceipt>> PostLinesAsync(long companyId, DateOnly date, string document, string narrative, IReadOnlyList<JournalLineRequest> lines, CancellationToken ct)
    {
        CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        return await _engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, DocumentType = "CompensationProvision",
            DocumentNumber = document, Narrative = narrative, Lines = lines,
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result> ServeNoticeAsync(long companyId, string claimNumber, string noticeReference, DateOnly servedOn, int objectionDays, CancellationToken ct = default)
        => StepAsync(companyId, claimNumber, claim =>
        {
            if (string.IsNullOrWhiteSpace(noticeReference) || objectionDays < 0)
            {
                return Error.Validation("Hse.Notice", "A notice needs its reference and an objection period of nil days or more.");
            }

            if (claim.NoticeServedOn is not null)
            {
                return Error.Conflict("Hse.NoticeServed", $"Claim {claim.ClaimNumber}'s notice was served on {claim.NoticeServedOn:yyyy-MM-dd}.");
            }

            claim.ServeNotice(noticeReference.Trim(), servedOn, objectionDays);
            return null;
        }, ct);

    /// <inheritdoc />
    public async Task<Result<long>> LodgeObjectionAsync(long companyId, string claimNumber, string objector, string grounds, DateOnly lodgedOn, CancellationToken ct = default)
    {
        CompensationStep? step = null;
        Result done = await StepAsync(companyId, claimNumber, claim =>
        {
            if (string.IsNullOrWhiteSpace(objector) || string.IsNullOrWhiteSpace(grounds))
            {
                return Error.Validation("Hse.Objection", "An objection needs the objector and the grounds.");
            }

            if (claim.NoticeServedOn is not DateOnly served || lodgedOn < served || lodgedOn > claim.ObjectionDeadline)
            {
                return Error.Rule("Hse.ObjectionWindow", claim.NoticeServedOn is null
                    ? $"No notice has been served on claim {claim.ClaimNumber}."
                    : $"Objections to claim {claim.ClaimNumber} run from {claim.NoticeServedOn:yyyy-MM-dd} to {claim.ObjectionDeadline:yyyy-MM-dd}.");
            }

            step = claim.AddStep(new CompensationStep(CompensationStepKind.Objection, objector.Trim(), lodgedOn, null, grounds.Trim()));
            return null;
        }, ct).ConfigureAwait(false);
        return done.IsFailure ? done.Error : step!.Id;
    }

    /// <inheritdoc />
    public Task<Result> DecideObjectionAsync(long companyId, string claimNumber, long objectionId, bool upheld, DateOnly date, string? note, CancellationToken ct = default)
        => StepAsync(companyId, claimNumber, claim =>
        {
            CompensationStep? objection = OpenStep(claim, objectionId, CompensationStepKind.Objection);
            if (objection is null)
            {
                return Error.NotFound("Hse.ObjectionNotOpen", $"No open objection {objectionId} on claim {claim.ClaimNumber}.");
            }

            objection.Resolve(upheld ? CompensationStepStatus.Upheld : CompensationStepStatus.Dismissed, date, note: note);
            return null;
        }, ct);

    /// <inheritdoc />
    public Task<Result> AssessAsync(long companyId, string claimNumber, string assessor, DateOnly date, decimal amount, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result done = await StepAsync(companyId, claimNumber, claim =>
            {
                if (string.IsNullOrWhiteSpace(assessor) || amount < 0m)
                {
                    return Error.Validation("Hse.Assessment", "An assessment needs the assessor and an amount of nil or more.");
                }

                claim.Assess(assessor.Trim(), date, amount);
                return null;
            }, ct).ConfigureAwait(false);
            return done.IsFailure ? done : await ReProvideAsync(companyId, claimNumber, amount, date, ct).ConfigureAwait(false);
        }, ct);

    /// <inheritdoc />
    public async Task<Result<long>> OfferAsync(long companyId, string claimNumber, decimal amount, DateOnly date, CancellationToken ct = default)
    {
        CompensationStep? step = null;
        Result done = await StepAsync(companyId, claimNumber, claim =>
        {
            if (claim.AssessedAmount is null || amount <= 0m)
            {
                return Error.Rule("Hse.OfferBeforeAssessment", $"Claim {claim.ClaimNumber} needs its compensation assessed before a positive offer is made.");
            }

            if (claim.Steps.Any(s => s.Kind == CompensationStepKind.Offer && s.Status == CompensationStepStatus.Open))
            {
                return Error.Conflict("Hse.OfferOpen", $"Claim {claim.ClaimNumber} already has an offer awaiting a response.");
            }

            step = claim.AddStep(new CompensationStep(CompensationStepKind.Offer, claim.Claimant, date, amount, null));
            return null;
        }, ct).ConfigureAwait(false);
        return done.IsFailure ? done.Error : step!.Id;
    }

    /// <inheritdoc />
    public Task<Result> RespondToOfferAsync(long companyId, string claimNumber, long offerId, bool accepted, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            decimal offered = 0m;
            Result done = await StepAsync(companyId, claimNumber, claim =>
            {
                CompensationStep? offer = OpenStep(claim, offerId, CompensationStepKind.Offer);
                if (offer is null)
                {
                    return Error.NotFound("Hse.OfferNotOpen", $"No open offer {offerId} on claim {claim.ClaimNumber}.");
                }

                offer.Resolve(accepted ? CompensationStepStatus.Accepted : CompensationStepStatus.Rejected, date);
                offered = offer.Amount ?? 0m;
                return null;
            }, ct).ConfigureAwait(false);
            return done.IsFailure || !accepted ? done : await ReProvideAsync(companyId, claimNumber, offered, date, ct).ConfigureAwait(false);
        }, ct);

    /// <inheritdoc />
    public async Task<Result<long>> DisputeAsync(long companyId, string claimNumber, string forum, DateOnly date, string? note, CancellationToken ct = default)
    {
        CompensationStep? step = null;
        Result done = await StepAsync(companyId, claimNumber, claim =>
        {
            if (string.IsNullOrWhiteSpace(forum))
            {
                return Error.Validation("Hse.DisputeForum", "Say where the dispute was referred (arbitrator or court).");
            }

            if (claim.Steps.Any(s => s.Kind == CompensationStepKind.Dispute && s.Status == CompensationStepStatus.Open))
            {
                return Error.Conflict("Hse.DisputeOpen", $"Claim {claim.ClaimNumber} already has a dispute awaiting its award.");
            }

            step = claim.AddStep(new CompensationStep(CompensationStepKind.Dispute, forum.Trim(), date, null, note?.Trim()));
            return null;
        }, ct).ConfigureAwait(false);
        return done.IsFailure ? done.Error : step!.Id;
    }

    /// <inheritdoc />
    public Task<Result> AwardAsync(long companyId, string claimNumber, long disputeId, decimal amount, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result done = await StepAsync(companyId, claimNumber, claim =>
            {
                CompensationStep? dispute = OpenStep(claim, disputeId, CompensationStepKind.Dispute);
                if (dispute is null || amount < 0m)
                {
                    return Error.NotFound("Hse.DisputeNotOpen", $"No open dispute {disputeId} on claim {claim.ClaimNumber} (or a negative award).");
                }

                dispute.Resolve(CompensationStepStatus.Awarded, date, amount);
                return null;
            }, ct).ConfigureAwait(false);
            return done.IsFailure ? done : await ReProvideAsync(companyId, claimNumber, amount, date, ct).ConfigureAwait(false);
        }, ct);

    /// <inheritdoc />
    public Task<Result> PayPartAsync(long companyId, string claimNumber, decimal amount, string bankAccountCode, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<CompensationClaim> found = await FindOpenAsync(companyId, claimNumber, ct).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return Result.Failure(found.Error);
            }

            CompensationClaim claim = found.Value;
            if (amount <= 0m || amount > claim.Provision || string.IsNullOrWhiteSpace(bankAccountCode))
            {
                return Result.Failure(Error.Rule("Hse.PartPayment", $"A part payment on claim {claim.ClaimNumber} must be positive and within the {claim.Provision:0.00} provided, from a bank account."));
            }

            Result<PostingReceipt> posted = await PostAsync(companyId, date, $"{claim.ClaimNumber}-PART-{date:yyyyMMdd}-{claim.PaidToDate:0.##}", $"Compensation part payment to {claim.Claimant}",
                claim.ProvisionAccountCode, bankAccountCode.Trim(), amount, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return Result.Failure(posted.Error);
            }

            claim.PayPart(amount);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }, ct);

    /// <inheritdoc />
    public Task<Result> TakePossessionAsync(long companyId, string claimNumber, DateOnly date, CancellationToken ct = default)
        => StepAsync(companyId, claimNumber, claim =>
        {
            if (claim.NoticeServedOn is null || date <= claim.ObjectionDeadline)
            {
                return Error.Rule("Hse.PossessionEarly", claim.NoticeServedOn is null
                    ? $"No notice has been served on claim {claim.ClaimNumber}."
                    : $"Possession under claim {claim.ClaimNumber} can be taken only after the objection window closes on {claim.ObjectionDeadline:yyyy-MM-dd}.");
            }

            if (claim.Steps.Any(s => s.Kind == CompensationStepKind.Objection && s.Status is CompensationStepStatus.Open or CompensationStepStatus.Upheld))
            {
                return Error.Rule("Hse.PossessionObjected", $"Claim {claim.ClaimNumber} has objections open or upheld; possession cannot be taken.");
            }

            if (claim.PossessionOn is not null)
            {
                return Error.Conflict("Hse.PossessionTaken", $"Possession under claim {claim.ClaimNumber} was taken on {claim.PossessionOn:yyyy-MM-dd}.");
            }

            claim.TakePossession(date);
            return null;
        }, ct);

    // Applies a check-and-change to an open claim (with its steps) and saves it.
    private async Task<Result> StepAsync(long companyId, string claimNumber, Func<CompensationClaim, Error?> change, CancellationToken ct)
    {
        Result<CompensationClaim> found = await FindOpenAsync(companyId, claimNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Error? refused = change(found.Value);
        if (refused is not null)
        {
            return Result.Failure(refused);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> ReProvideAsync(long companyId, string claimNumber, decimal estimate, DateOnly date, CancellationToken ct)
    {
        Result<CompensationClaim> found = await FindOpenAsync(companyId, claimNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        // What is still owed: the estimate less what has been paid already.
        Result<decimal> provided = await ProvideCoreAsync(companyId, claimNumber, Math.Max(0m, estimate - found.Value.PaidToDate), date, ct).ConfigureAwait(false);
        return provided.IsFailure ? Result.Failure(provided.Error) : Result.Success();
    }

    private static CompensationStep? OpenStep(CompensationClaim claim, long id, CompensationStepKind kind)
        => claim.Steps.FirstOrDefault(s => s.Id == id && s.Kind == kind && s.Status == CompensationStepStatus.Open);

    private async Task<Result<CompensationClaim>> FindOpenAsync(long companyId, string claimNumber, CancellationToken ct)
    {
        string number = (claimNumber ?? string.Empty).Trim();
        CompensationClaim? claim = await _db.Set<CompensationClaim>().Include(c => c.Steps).FirstOrDefaultAsync(c => c.CompanyId == companyId && c.ClaimNumber == number, ct).ConfigureAwait(false);
        if (claim is null)
        {
            return Error.NotFound("Hse.ClaimNotFound", $"Claim {number} not found.");
        }

        if (claim.Status is CompensationStatus.Settled or CompensationStatus.Withdrawn)
        {
            return Error.Rule("Hse.ClaimClosed", $"Claim {number} is {claim.Status}.");
        }

        return claim;
    }
}
