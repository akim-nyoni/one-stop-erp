using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.FixedAssets;

/// <summary>Collects capital project cost in CWIP and capitalises it into the asset register.</summary>
public sealed class CapitalProjectService : ICapitalProjectService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly IFixedAssetService _assets;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">Posts costs into CWIP.</param>
    /// <param name="assets">Registers the finished asset.</param>
    public CapitalProjectService(One4AllDbContext db, IPostingEngine postingEngine, IFixedAssetService assets)
    {
        _db = db;
        _postingEngine = postingEngine;
        _assets = assets;
    }

    /// <inheritdoc />
    public async Task<Result<long>> OpenAsync(long companyId, string code, string name, string cwipAccountCode, decimal budget, DateOnly openedOn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cwipAccountCode) || budget < 0m)
        {
            return Error.Validation("Fa.ProjectInvalid", "A capital project needs a code, a name, a CWIP account and a non-negative budget.");
        }

        if (!await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == cwipAccountCode && a.Type == AccountType.Asset, ct).ConfigureAwait(false))
        {
            return Error.Validation("Fa.CwipAccount", $"{cwipAccountCode} is not an asset account.");
        }

        string normalised = code.Trim().ToUpperInvariant();
        if (await _db.CapitalProjects.AnyAsync(p => p.CompanyId == companyId && p.Code == normalised, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Fa.ProjectExists", $"Capital project {normalised} already exists.");
        }

        CapitalProject project = new(companyId, normalised, name.Trim(), cwipAccountCode, budget, openedOn);
        _db.CapitalProjects.Add(project);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return project.Id;
    }

    /// <inheritdoc />
    public Task<Result<string>> AddCostAsync(long companyId, long projectId, DateOnly date, decimal amount, string fundingAccountCode, string description, CancellationToken ct = default)
        => _db.AtomicAsync(() => AddCostCoreAsync(companyId, projectId, date, amount, fundingAccountCode, description, ct), ct);

    // One unit of work: the CWIP journal and the cost record commit together.
    private async Task<Result<string>> AddCostCoreAsync(long companyId, long projectId, DateOnly date, decimal amount, string fundingAccountCode, string description, CancellationToken ct)
    {
        if (amount <= 0m || string.IsNullOrWhiteSpace(fundingAccountCode) || string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation("Fa.CostInvalid", "A project cost needs a positive amount, a funding account and a description.");
        }

        CapitalProject? project = await _db.CapitalProjects.Include(p => p.Costs).FirstOrDefaultAsync(p => p.Id == projectId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound("Fa.ProjectNotFound", $"Capital project {projectId} not found.");
        }

        if (project.IsCapitalised)
        {
            return Error.Rule("Fa.ProjectCapitalised", $"Project {project.Code} is capitalised; costs now belong to the asset.");
        }

        if (date < project.OpenedOn)
        {
            return Error.Validation("Fa.CostBeforeOpen", "A cost cannot be dated before the project opened.");
        }

        CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = "CwipCost",
            DocumentNumber = project.Code,
            Narrative = $"CWIP {project.Code}: {description.Trim()}",
            Lines =
            [
                new JournalLineRequest { AccountCode = project.CwipAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        project.AddCost(new CapitalProjectCost(date, description.Trim(), amount, posting.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posting.Value.JournalNumber;
    }

    /// <inheritdoc />
    public Task<Result<long>> CapitaliseAsync(long projectId, RegisterAssetRequest asset, CancellationToken ct = default)
        => _db.AtomicAsync(() => CapitaliseCoreAsync(projectId, asset, ct), ct);

    // One unit of work: the asset registration (which clears CWIP) and the project's capitalised mark commit together.
    private async Task<Result<long>> CapitaliseCoreAsync(long projectId, RegisterAssetRequest asset, CancellationToken ct)
    {
        CapitalProject? project = await _db.CapitalProjects.Include(p => p.Costs).FirstOrDefaultAsync(p => p.Id == projectId && p.CompanyId == asset.CompanyId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound("Fa.ProjectNotFound", $"Capital project {projectId} not found.");
        }

        if (project.IsCapitalised)
        {
            return Error.Rule("Fa.ProjectCapitalised", $"Project {project.Code} is already capitalised.");
        }

        if (project.TotalCost <= 0m)
        {
            return Error.Rule("Fa.ProjectNoCost", $"Project {project.Code} has no cost to capitalise.");
        }

        if (project.Costs.Any(c => c.Date > asset.AcquisitionDate))
        {
            return Error.Validation("Fa.CapitaliseBeforeCost", "The asset cannot come into use before the project's last cost.");
        }

        Result<long> registered = await _assets.RegisterAsync(new RegisterAssetRequest
        {
            CompanyId = asset.CompanyId, AssetNumber = asset.AssetNumber, Description = asset.Description, AssetClass = asset.AssetClass, Cost = project.TotalCost, ResidualValue = asset.ResidualValue,
            AcquisitionDate = asset.AcquisitionDate, Method = asset.Method, UsefulLifeMonths = asset.UsefulLifeMonths, ReducingBalanceRatePercent = asset.ReducingBalanceRatePercent,
            CostAccountCode = asset.CostAccountCode, AccumulatedDepreciationAccountCode = asset.AccumulatedDepreciationAccountCode, DepreciationExpenseAccountCode = asset.DepreciationExpenseAccountCode,
            FundingAccountCode = project.CwipAccountCode, DocumentNumber = string.IsNullOrWhiteSpace(asset.DocumentNumber) ? project.Code : asset.DocumentNumber,
        }, ct).ConfigureAwait(false);
        if (registered.IsFailure)
        {
            return registered.Error;
        }

        project.Capitalise(asset.AcquisitionDate, registered.Value);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return registered.Value;
    }
}
