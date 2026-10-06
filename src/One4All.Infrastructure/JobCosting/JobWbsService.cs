using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.JobCosting;
using One4All.Core.Common;
using One4All.Core.JobCosting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.JobCosting;

/// <summary>Work breakdown structure and cost-to-complete on contracts (brief §4.12).</summary>
public sealed partial class JobWbsService : IJobWbsService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public JobWbsService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> AddElementAsync(long companyId, long contractId, string code, string name, decimal budgetCost, string? costHead, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        string? head = string.IsNullOrWhiteSpace(costHead) ? null : costHead.Trim();
        if (!CodePattern().IsMatch(key) || string.IsNullOrWhiteSpace(name) || budgetCost < 0m || (head is null && budgetCost != 0m))
        {
            return Error.Validation("Job.WbsInvalid", "A WBS element needs a dotted code (e.g. 1.2) and a name; a budget belongs on an activity with a cost head.");
        }

        if (!await _db.JobContracts.AnyAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        List<JobWbsElement> existing = await _db.JobWbsElements.Where(e => e.JobContractId == contractId).ToListAsync(ct).ConfigureAwait(false);
        if (existing.Any(e => e.Code == key))
        {
            return Error.Conflict("Job.WbsExists", $"WBS element {key} already exists.");
        }

        if (WbsRollup.Parent(key) is string parentCode)
        {
            JobWbsElement? parent = existing.FirstOrDefault(e => e.Code == parentCode);
            if (parent is null)
            {
                return Error.Rule("Job.WbsNoParent", $"Add {parentCode} before {key}.");
            }

            if (parent.CostHead is not null)
            {
                return Error.Rule("Job.WbsParentIsActivity", $"{parentCode} is an activity carrying cost head '{parent.CostHead}'; it cannot have children.");
            }
        }

        if (head is not null && existing.Any(e => string.Equals(e.CostHead, head, StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Conflict("Job.WbsCostHeadTaken", $"Cost head '{head}' is already on another activity.");
        }

        JobWbsElement element = new(companyId, contractId, key, name.Trim(), budgetCost, head);
        _db.JobWbsElements.Add(element);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return element.Id;
    }

    /// <inheritdoc />
    public async Task<Result> SetEstimateAsync(long companyId, long contractId, string code, decimal? estimateToComplete, DateOnly date, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        JobWbsElement? element = await _db.JobWbsElements.FirstOrDefaultAsync(e => e.CompanyId == companyId && e.JobContractId == contractId && e.Code == key, ct).ConfigureAwait(false);
        if (element is null)
        {
            return Result.Failure(Error.NotFound("Job.WbsNotFound", $"No WBS element {key} on the contract."));
        }

        if (element.CostHead is null || estimateToComplete < 0m)
        {
            return Result.Failure(Error.Rule("Job.WbsEstimateInvalid", "Estimates to complete are given on activities and cannot be negative."));
        }

        element.EstimateToComplete = estimateToComplete;
        element.EstimatedOn = estimateToComplete is null ? null : date;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<WbsBreakdown>> BreakdownAsync(long companyId, long contractId, CancellationToken ct = default)
    {
        JobContract? contract = await _db.JobContracts.AsNoTracking().Include(c => c.Costs).FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        List<JobWbsElement> elements = await _db.JobWbsElements.AsNoTracking().Where(e => e.JobContractId == contractId).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, string> canonical = elements.Where(e => e.CostHead is not null).ToDictionary(e => e.CostHead!, e => e.CostHead!, StringComparer.OrdinalIgnoreCase);
        string Head(string h) => canonical.GetValueOrDefault(h, h);
        Dictionary<string, decimal> actual = contract.Costs.GroupBy(c => Head(c.CostHead)).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount), StringComparer.Ordinal);
        List<Subcontract> subcontracts = await _db.Subcontracts.AsNoTracking().Include(s => s.Certificates).Where(s => s.JobContractId == contractId).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, decimal> committed = subcontracts.GroupBy(s => Head(s.CostHead)).ToDictionary(g => g.Key, g => g.Sum(s => Math.Max(0m, s.Value - s.CertifiedToDate)), StringComparer.Ordinal);
        (IReadOnlyList<WbsLine> lines, WbsLine total) = WbsRollup.Build(
            [.. elements.Select(e => new WbsElementInput(e.Code, e.Name, e.BudgetCost, e.CostHead, e.EstimateToComplete))], actual, committed);
        return new WbsBreakdown(contract.ContractNumber, contract.BudgetCost, lines, total);
    }

    [GeneratedRegex(@"^[0-9A-Z]{1,10}(\.[0-9A-Z]{1,10}){0,5}$")]
    private static partial Regex CodePattern();
}
