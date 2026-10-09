using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Workflow;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Stand pricing rules: proposal, approval and quotation.</summary>
public sealed class StandPricingService : IStandPricingService
{
    private const string PricingPermission = "Housing.Pricing";

    private readonly One4AllDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="permissions">The permission resolver.</param>
    public StandPricingService(One4AllDbContext db, IPermissionService permissions, Lazy<IWorkflowService>? workflow = null)
    {
        _workflow = workflow;
        _db = db;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public Task<Result<long>> ProposeAsync(PricingRuleInput input, CancellationToken ct = default)
        => _db.AtomicAsync(() => ProposeCoreAsync(input, ct), ct);

    // One unit of work: the rule and its approval workflow (when prices have one) start together.
    private async Task<Result<long>> ProposeCoreAsync(PricingRuleInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        bool scoped = input.Scope switch
        {
            PricingScope.Stand => input.StandId is not null && input.SchemeId is null && input.MinSize is null && input.MaxSize is null,
            PricingScope.SizeBand => input.SchemeId is not null && input.StandId is null && input.MinSize >= 0m && input.MaxSize >= input.MinSize,
            PricingScope.Scheme => input.SchemeId is not null && input.StandId is null && input.MinSize is null && input.MaxSize is null,
            _ => false,
        };
        if (!scoped || input.OverheadPercent < 0m || input.LevyPerStand < 0m || input.LevyPerSquareMetre < 0m || input.MarginPercent < 0m || input.RoundTo < 0m)
        {
            return Error.Validation("Hse.PricingRule", "A rule names exactly what it prices (a stand; a scheme; or a scheme and size band) and has no negative terms.");
        }

        if (!(await _permissions.ResolveAsync(input.UserId, ct).ConfigureAwait(false)).Has(PricingPermission, OperationFlags.Edit))
        {
            return Error.Rule("Hse.NotAuthorised", "Only an officer with the pricing permission can propose prices.");
        }

        bool known = input.Scope == PricingScope.Stand
            ? await _db.HousingStands.AnyAsync(s => s.Id == input.StandId && s.CompanyId == input.CompanyId, ct).ConfigureAwait(false)
            : await _db.HousingSchemes.AnyAsync(s => s.Id == input.SchemeId && s.CompanyId == input.CompanyId, ct).ConfigureAwait(false);
        if (!known)
        {
            return Error.NotFound("Hse.PricingTarget", "The stand or scheme priced is not on the register.");
        }

        StandPricingRule rule = new(input.CompanyId, input.Scope, input.SchemeId, input.MinSize, input.MaxSize, input.StandId, input.EffectiveFrom,
            input.OverheadPercent, input.LevyPerStand, input.LevyPerSquareMetre, input.MarginPercent, input.RoundTo, input.UserId);
        _db.Set<StandPricingRule>().Add(rule);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, input.CompanyId, WorkflowDocumentTypes.StandPrice, $"PR-{rule.Id}", input.MarginPercent, input.UserId, ct)
            .ConfigureAwait(false);
        return governed.IsFailure ? Result<long>.Failure(governed.Error) : rule.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ApproveAsync(long companyId, long ruleId, long userId, DateOnly date, CancellationToken ct = default)
    {
        if (!(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has(PricingPermission, OperationFlags.Approve))
        {
            return Result.Failure(Error.Rule("Hse.NotAuthorised", "Only an officer with the pricing Approve permission can approve prices."));
        }

        StandPricingRule? rule = await _db.Set<StandPricingRule>().FirstOrDefaultAsync(r => r.Id == ruleId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("Hse.PricingRuleNotFound", $"Pricing rule {ruleId} not found."));
        }

        if (rule.ApprovedOn is not null)
        {
            return Result.Failure(Error.Conflict("Hse.PricingApproved", $"Pricing rule {ruleId} is already approved."));
        }

        if (rule.ProposedByUserId == userId)
        {
            return Result.Failure(Error.Rule("Hse.SelfApproval", "A price must be approved by someone other than who proposed it."));
        }

        if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.StandPrice, $"PR-{rule.Id}", ct).ConfigureAwait(false))
        {
            return Result.Failure(Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.StandPrice, $"PR-{rule.Id}"));
        }

        rule.Approve(userId, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<StandPriceQuote>> QuoteAsync(long companyId, long standId, DateOnly date, CancellationToken ct = default)
    {
        HousingStand? stand = await _db.HousingStands.AsNoTracking().FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found.");
        }

        StandPriceQuote? quote = await StandPrices.QuoteAsync(_db, stand, date, ct).ConfigureAwait(false);
        if (quote is null)
        {
            return Error.Rule("Hse.NoPrice", $"No approved price applies to stand {stand.StandNumber} on {date:yyyy-MM-dd}.");
        }

        return quote;
    }
}

/// <summary>Prices a stand from the approved rules on a date (shared with allocation offers, which freeze the price).</summary>
internal static class StandPrices
{
    /// <summary>The stand's price on a date, or null when no approved rule applies.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="stand">The stand.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quote.</returns>
    public static async Task<StandPriceQuote?> QuoteAsync(One4AllDbContext db, HousingStand stand, DateOnly date, CancellationToken ct)
    {
        List<StandPricingRule> rules = await db.Set<StandPricingRule>().AsNoTracking()
            .Where(r => r.CompanyId == stand.CompanyId && r.ApprovedOn != null && r.EffectiveFrom <= date && (r.StandId == stand.Id || (r.SchemeId != null && r.SchemeId == stand.SchemeId)))
            .ToListAsync(ct).ConfigureAwait(false);
        PricingRuleKey? key = StandPricing.Choose(rules.Select(r => new PricingRuleKey(r.Id, r.Scope, r.SchemeId, r.MinSize, r.MaxSize, r.StandId, r.EffectiveFrom)),
            stand.Id, stand.SchemeId, stand.SizeSquareMetres, date);
        if (key is null)
        {
            return null;
        }

        StandPricingRule rule = rules.Single(r => r.Id == key.Id);
        PriceBuildUp price = StandPricing.Build(stand.AllocatedCost, stand.SizeSquareMetres,
            new PricingTerms(rule.OverheadPercent, rule.LevyPerStand, rule.LevyPerSquareMetre, rule.MarginPercent, rule.RoundTo));
        return new StandPriceQuote(stand.StandNumber, rule.Id, rule.Scope, rule.EffectiveFrom, price);
    }
}

/// <summary>Concludes a pricing rule's approval workflow: approval puts the price into force; a rejection leaves it unapproved, never applied.</summary>
internal sealed class StandPriceWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly One4AllDbContext _db;

    public StandPriceWorkflowHandler(One4AllDbContext db) => _db = db;

    public string DocumentType => WorkflowDocumentTypes.StandPrice;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        if (outcome != Core.Workflow.WorkflowStatus.Approved || !long.TryParse(documentReference.AsSpan(3), out long ruleId))
        {
            return Result.Success();
        }

        StandPricingRule? rule = await _db.Set<StandPricingRule>().FirstOrDefaultAsync(r => r.Id == ruleId && r.CompanyId == companyId && r.ApprovedOn == null, ct).ConfigureAwait(false);
        rule?.Approve(decidedByUserId, DateOnly.FromDateTime(DateTime.Today));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
