using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.JobCosting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Servicing contracts' cost apportioned into scheme stands.</summary>
public sealed class ServicingCostService : IServicingCostService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public ServicingCostService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result> LinkAsync(long companyId, long schemeId, long jobContractId, ServicingBasis basis, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(basis))
        {
            return Result.Failure(Error.Validation("Hse.ServicingBasis", "Name the apportionment basis."));
        }

        if (!await _db.HousingSchemes.AnyAsync(s => s.Id == schemeId && s.CompanyId == companyId, ct).ConfigureAwait(false)
            || !await _db.Set<JobContract>().AnyAsync(c => c.Id == jobContractId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Hse.ServicingTarget", "The scheme or the contract is not on the register."));
        }

        if (await _db.Set<ServicingLink>().AnyAsync(l => l.JobContractId == jobContractId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.ContractLinked", "The contract already services a scheme."));
        }

        _db.Set<ServicingLink>().Add(new ServicingLink(companyId, schemeId, jobContractId, basis));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetMeasuresAsync(long companyId, long standId, decimal? frontageMetres, decimal? zoningWeight, CancellationToken ct = default)
    {
        if (frontageMetres <= 0m || zoningWeight <= 0m)
        {
            return Result.Failure(Error.Validation("Hse.StandMeasures", "Frontage and zoning weight must be positive when given."));
        }

        HousingStand? stand = await _db.HousingStands.FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Result.Failure(Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found."));
        }

        stand.SetServicingMeasures(frontageMetres, zoningWeight);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ServicingRunLine>>> ApportionAsync(long companyId, long schemeId, DateOnly date, string servicingWipAccountCode, string standsAvailableAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApportionCoreAsync(companyId, schemeId, date, servicingWipAccountCode, standsAvailableAccountCode, ct), ct);

    private async Task<Result<IReadOnlyList<ServicingRunLine>>> ApportionCoreAsync(long companyId, long schemeId, DateOnly date, string wipAccount, string standsAccount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(wipAccount) || string.IsNullOrWhiteSpace(standsAccount))
        {
            return Error.Validation("Hse.ServicingAccounts", "Name the servicing WIP and stands available accounts.");
        }

        List<ServicingLink> links = await _db.Set<ServicingLink>().Where(l => l.CompanyId == companyId && l.SchemeId == schemeId).OrderBy(l => l.Id).ToListAsync(ct).ConfigureAwait(false);
        if (links.Count == 0)
        {
            return Error.Rule("Hse.NoServicingContract", "No servicing contract is linked to the scheme.");
        }

        // Every stand still in stock takes servicing cost — being planned, surveyed or serviced, available, reserved, offered or allocated; a sold or transferred stand's
        // cost has already gone to cost of sales (thirteenth audit #7: stands before "available" were skipped, skewing the cost per stand).
        List<HousingStand> stands = await _db.HousingStands.Where(s => s.CompanyId == companyId && s.SchemeId == schemeId && s.Status != StandStatus.Sold && s.Status != StandStatus.Transferred
                && s.Status != StandStatus.TitleHandedOver && s.Status != StandStatus.ConsolidatedAway && s.Status != StandStatus.ReSubdivided)
            .OrderBy(s => s.StandNumber).ToListAsync(ct).ConfigureAwait(false);
        CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);

        List<ServicingRunLine> lines = [];
        foreach (ServicingLink link in links)
        {
            JobContract contract = await _db.Set<JobContract>().Include(c => c.Costs).FirstAsync(c => c.Id == link.JobContractId, ct).ConfigureAwait(false);
            decimal fresh = contract.Costs.Where(c => c.Date <= date).Sum(c => c.Amount) - link.ApportionedToDate;
            if (fresh <= 0m)
            {
                lines.Add(new ServicingRunLine(contract.ContractNumber, 0m, null));
                continue;
            }

            string? problem = ServicingApportionment.Apportion([.. stands.Select(s => new StandMeasures(s.Id, s.SizeSquareMetres, s.FrontageMetres, s.ZoningWeight))], link.Basis, fresh, out IReadOnlyList<StandShare> shares);
            if (problem is not null)
            {
                return Error.Rule("Hse.Apportionment", problem);
            }

            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, DocumentType = "STAND-SVC",
                DocumentNumber = $"SVC-{contract.ContractNumber}-{date:yyyyMMdd}", Narrative = $"Servicing cost of {contract.ContractNumber} apportioned {link.Basis}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = standsAccount.Trim(), Side = JournalSide.Debit, TransactionAmount = fresh },
                    new JournalLineRequest { AccountCode = wipAccount.Trim(), Side = JournalSide.Credit, TransactionAmount = fresh },
                ],
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            Dictionary<long, HousingStand> byId = stands.ToDictionary(s => s.Id);
            foreach (StandShare share in shares)
            {
                byId[share.StandId].AddServicingCost(share.Amount);
                _db.Set<ServicingCharge>().Add(new ServicingCharge(companyId, link.Id, share.StandId, share.Amount, date, posted.Value.JournalNumber));
            }

            link.Apportioned(fresh);
            lines.Add(new ServicingRunLine(contract.ContractNumber, fresh, posted.Value.JournalNumber));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<ServicingRunLine>>.Success(lines);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StandCostEntry>> CostPerStandAsync(long companyId, long schemeId, CancellationToken ct = default)
    {
        var stands = await _db.HousingStands.AsNoTracking().Where(s => s.CompanyId == companyId && s.SchemeId == schemeId)
            .OrderBy(s => s.StandNumber).Select(s => new { s.Id, s.StandNumber, s.AllocatedCost }).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. stands.Select(s => s.Id)];
        Dictionary<long, decimal> servicing = await _db.Set<ServicingCharge>().Where(c => ids.Contains(c.StandId))
            .GroupBy(c => c.StandId).Select(g => new { g.Key, Total = g.Sum(c => c.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct).ConfigureAwait(false);
        return [.. stands.Select(s => new StandCostEntry(s.StandNumber, s.AllocatedCost, servicing.GetValueOrDefault(s.Id)))];
    }
}
