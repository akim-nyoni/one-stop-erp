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
                ? await PostAsync(companyId, date, $"{claim.ClaimNumber}-{date:yyyyMMdd}", $"Compensation provision for {claim.Claimant}", claim.LandCostAccountCode, claim.ProvisionAccountCode, movement, ct).ConfigureAwait(false)
                : await PostAsync(companyId, date, $"{claim.ClaimNumber}-{date:yyyyMMdd}", $"Compensation provision for {claim.Claimant} reduced", claim.ProvisionAccountCode, claim.LandCostAccountCode, -movement, ct).ConfigureAwait(false);
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

    private async Task<Result<CompensationClaim>> FindOpenAsync(long companyId, string claimNumber, CancellationToken ct)
    {
        string number = (claimNumber ?? string.Empty).Trim();
        CompensationClaim? claim = await _db.Set<CompensationClaim>().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.ClaimNumber == number, ct).ConfigureAwait(false);
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
