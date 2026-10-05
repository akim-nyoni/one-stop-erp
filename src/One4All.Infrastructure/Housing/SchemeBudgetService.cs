using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Scheme budgets and capitalised scheme costs (brief §14 H84).</summary>
public sealed class SchemeBudgetService : ISchemeBudgetService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public SchemeBudgetService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result> SetBudgetAsync(long companyId, string schemeCode, SchemeCostElement element, decimal amount, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(element) || amount < 0m)
        {
            return Result.Failure(Error.Validation("Hse.BudgetDetails", "A budget is for one cost element and cannot be negative."));
        }

        long? schemeId = await SchemeIdAsync(companyId, schemeCode, ct).ConfigureAwait(false);
        if (schemeId is null)
        {
            return Result.Failure(Error.NotFound("Hse.SchemeNotFound", $"Scheme {schemeCode} not found."));
        }

        SchemeBudget? budget = await _db.Set<SchemeBudget>().FirstOrDefaultAsync(b => b.SchemeId == schemeId && b.Element == element, ct).ConfigureAwait(false);
        if (budget is null)
        {
            _db.Set<SchemeBudget>().Add(new SchemeBudget(companyId, schemeId.Value, element, amount));
        }
        else
        {
            budget.Amount = amount;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<string>> RecordCostAsync(SchemeCostRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecordCostCoreAsync(request, ct), ct);

    // One unit of work: the journal, each unsold stand's share and the register entry commit together.
    private async Task<Result<string>> RecordCostCoreAsync(SchemeCostRequest request, CancellationToken ct)
    {
        if (request.Element is not (SchemeCostElement.ProfessionalFees or SchemeCostElement.Overheads) || request.Amount <= 0m || string.IsNullOrWhiteSpace(request.Reference))
        {
            return Error.Validation("Hse.SchemeCostDetails", "Only professional fees and overheads are capitalised this way, with a positive amount and a reference.");
        }

        long? schemeId = await SchemeIdAsync(request.CompanyId, request.SchemeCode, ct).ConfigureAwait(false);
        if (schemeId is null)
        {
            return Error.NotFound("Hse.SchemeNotFound", $"Scheme {request.SchemeCode} not found.");
        }

        List<HousingStand> unsold = await _db.HousingStands.Where(s => s.SchemeId == schemeId && s.Status != StandStatus.Sold && s.Status != StandStatus.Transferred)
            .OrderBy(s => s.StandNumber).ToListAsync(ct).ConfigureAwait(false);
        if (unsold.Count == 0)
        {
            return Error.Rule("Hse.NoUnsoldStands", $"Scheme {request.SchemeCode} has no unsold stands to carry the cost; expense it instead.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Housing.CompanyNotFound", $"Company {request.CompanyId} not found.");
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId, SourceModule = "GL", PostingDate = request.Date, TransactionCurrency = company.BaseCurrency, DocumentType = "HSE-SCOST",
            DocumentNumber = request.Reference.Trim(), Narrative = $"{request.Element} capitalised to {request.SchemeCode}: {request.Description}".Trim(),
            Lines =
            [
                new JournalLineRequest { AccountCode = request.StandsAccountCode, Side = JournalSide.Debit, TransactionAmount = request.Amount },
                new JournalLineRequest { AccountCode = request.FundingAccountCode, Side = JournalSide.Credit, TransactionAmount = request.Amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        IReadOnlyList<decimal> shares = SchemeCostRules.Spread(request.Amount, unsold.Count);
        for (int i = 0; i < unsold.Count; i++)
        {
            unsold[i].AddServicingCost(shares[i]);
        }

        _db.Set<SchemeCost>().Add(new SchemeCost(request.CompanyId, schemeId.Value, request.Element, request.Date, request.Amount, request.Reference.Trim(), request.Description.Trim(),
            unsold.Count, posted.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    private Task<long?> SchemeIdAsync(long companyId, string schemeCode, CancellationToken ct)
    {
        string code = schemeCode.Trim();
        return _db.HousingSchemes.Where(s => s.CompanyId == companyId && s.Code == code).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct);
    }
}
