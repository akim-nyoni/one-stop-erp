using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Runs overhead recharge rules at period end through the posting engine.</summary>
public sealed class OverheadAllocationService : IOverheadAllocationService
{
    private const string DocumentType = "Allocation";

    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    public OverheadAllocationService(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateRuleAsync(AllocationRuleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.SourceCostCentre))
        {
            return Error.Validation("Alloc.Incomplete", "An allocation rule needs a code and a source cost centre.");
        }

        if (!Enum.IsDefined(request.Basis))
        {
            return Error.Validation("Alloc.Basis", "Choose fixed percentage, headcount, floor area or usage as the basis.");
        }

        string code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Set<AllocationRule>().AnyAsync(r => r.CompanyId == request.CompanyId && r.Code == code, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Alloc.Duplicate", $"Allocation rule {code} already exists.");
        }

        foreach (string account in new[] { request.SourceAccountCode, request.TargetAccountCode }.OfType<string>())
        {
            if (!await _db.Accounts.AnyAsync(a => a.CompanyId == request.CompanyId && a.AccountCode == account && a.IsActive, ct).ConfigureAwait(false))
            {
                return Error.NotFound("Alloc.Account", $"Account {account} does not exist or is inactive.");
            }
        }

        AllocationRule rule = new(request.CompanyId, code, request.Name, request.Sequence, request.Basis, request.SourceAccountCode, request.SourceCostCentre, request.TargetAccountCode);
        Error? invalid = await ApplyTargetsAsync(rule, request.Targets, ct).ConfigureAwait(false);
        if (invalid is not null)
        {
            return invalid;
        }

        _db.Set<AllocationRule>().Add(rule);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rule.Id;
    }

    /// <inheritdoc />
    public async Task<Result> SetDriversAsync(long companyId, long ruleId, IReadOnlyList<AllocationTargetRequest> targets, CancellationToken ct = default)
    {
        AllocationRule? rule = await _db.Set<AllocationRule>().Include(r => r.Targets)
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("Alloc.NotFound", $"Allocation rule {ruleId} not found."));
        }

        Error? invalid = await ApplyTargetsAsync(rule, targets, ct).ConfigureAwait(false);
        if (invalid is not null)
        {
            return Result.Failure(invalid);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<AllocationRunResult>> PreviewAsync(long companyId, DateOnly periodDate, CancellationToken ct = default)
        => ComputeAsync(companyId, periodDate, ct);

    /// <inheritdoc />
    public Task<Result<AllocationRunResult>> RunAsync(long companyId, DateOnly periodDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunCoreAsync(companyId, periodDate, ct), ct);

    // One unit: every rule's journal posts, or none does.
    private async Task<Result<AllocationRunResult>> RunCoreAsync(long companyId, DateOnly periodDate, CancellationToken ct)
    {
        Result<AllocationRunResult> computed = await ComputeAsync(companyId, periodDate, ct).ConfigureAwait(false);
        if (computed.IsFailure)
        {
            return computed.Error;
        }

        CurrencyCode baseCurrency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        Dictionary<string, AllocationRule> rules = await _db.Set<AllocationRule>().AsNoTracking()
            .Where(r => r.CompanyId == companyId).ToDictionaryAsync(r => r.Code, ct).ConfigureAwait(false);
        AllocationRunResult run = computed.Value;
        string period = run.PeriodEnd.ToString("yyyyMM", CultureInfo.InvariantCulture);
        List<AllocationRuleRun> posted = [];
        foreach (AllocationRuleRun ruleRun in run.Rules)
        {
            if (ruleRun.Pool == 0m)
            {
                posted.Add(ruleRun);
                continue;
            }

            AllocationRule rule = rules[ruleRun.RuleCode];
            JournalSide poolSide = ruleRun.Pool > 0m ? JournalSide.Credit : JournalSide.Debit;
            JournalSide shareSide = poolSide == JournalSide.Credit ? JournalSide.Debit : JournalSide.Credit;
            List<JournalLineRequest> lines =
            [
                new JournalLineRequest { AccountCode = rule.SourceAccountCode, CostCentre = rule.SourceCostCentre, Side = poolSide, TransactionAmount = Math.Abs(ruleRun.Pool), Narrative = $"{rule.Name} recharged" },
                .. ruleRun.Shares.Where(s => s.Amount != 0m).Select(s => new JournalLineRequest
                {
                    AccountCode = rule.ChargeAccountCode, CostCentre = s.CostCentre, Department = s.Department, Side = shareSide, TransactionAmount = Math.Abs(s.Amount),
                    Narrative = $"{rule.Name} share",
                }),
            ];

            Result<PostingReceipt> receipt = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = run.PeriodEnd,
                TransactionCurrency = baseCurrency,
                DocumentType = DocumentType,
                DocumentNumber = $"{rule.Code}-{period}",
                Narrative = $"Overhead recharge {rule.Code} for {period}",
                EnforceManualJournalRules = false,
                Lines = lines,
            }, ct).ConfigureAwait(false);
            if (receipt.IsFailure)
            {
                return receipt.Error;
            }

            posted.Add(ruleRun with { JournalNumber = receipt.Value.JournalNumber });
        }

        return run with { Rules = posted };
    }

    // Works out every active rule's pool and shares, in sequence order, before anything is posted: a pool is the net
    // base cost booked in the period to the source account in the source cost centre, plus what earlier rules in this
    // run charge into it.
    private async Task<Result<AllocationRunResult>> ComputeAsync(long companyId, DateOnly periodDate, CancellationToken ct)
    {
        FiscalPeriod? period = await _db.FiscalYears.Where(y => y.CompanyId == companyId)
            .SelectMany(y => y.Periods)
            .FirstOrDefaultAsync(p => p.StartDate <= periodDate && p.EndDate >= periodDate, ct).ConfigureAwait(false);
        if (period is null)
        {
            return Error.NotFound("Alloc.NoPeriod", $"No fiscal period contains {periodDate:yyyy-MM-dd}.");
        }

        List<AllocationRule> rules = await _db.Set<AllocationRule>().AsNoTracking().Include(r => r.Targets)
            .Where(r => r.CompanyId == companyId && r.IsActive)
            .OrderBy(r => r.Sequence).ThenBy(r => r.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<(string Account, string CostCentre), decimal> chargedInRun = [];
        List<AllocationRuleRun> runs = [];
        foreach (AllocationRule rule in rules)
        {
            decimal booked = await _db.Set<JournalLine>()
                .Where(l => l.AccountCode == rule.SourceAccountCode && l.CostCentre == rule.SourceCostCentre)
                .Join(_db.Journals.Where(j => j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed)
                        && j.PostingDate >= period.StartDate && j.PostingDate <= period.EndDate),
                    l => l.JournalEntryId, j => j.Id, (l, j) => l.BaseDebit - l.BaseCredit)
                .SumAsync(ct).ConfigureAwait(false);
            decimal pool = decimal.Round(booked + chargedInRun.GetValueOrDefault((rule.SourceAccountCode, rule.SourceCostCentre)), 2, MidpointRounding.AwayFromZero);
            chargedInRun[(rule.SourceAccountCode, rule.SourceCostCentre)] = chargedInRun.GetValueOrDefault((rule.SourceAccountCode, rule.SourceCostCentre)) - pool;

            List<AllocationShare> shares = [];
            if (pool != 0m)
            {
                decimal[] amounts = OverheadAllocation.Share(pool, [.. rule.Targets.Select(t => t.Driver)]);
                for (int i = 0; i < rule.Targets.Count; i++)
                {
                    AllocationRuleTarget target = rule.Targets[i];
                    shares.Add(new AllocationShare(target.CostCentre, target.Department, target.Driver, amounts[i]));
                    (string, string) key = (rule.ChargeAccountCode, target.CostCentre);
                    chargedInRun[key] = chargedInRun.GetValueOrDefault(key) + amounts[i];
                }
            }

            runs.Add(new AllocationRuleRun(rule.Code, rule.Name, rule.Basis, pool, shares, null));
        }

        return new AllocationRunResult(period.StartDate, period.EndDate, runs);
    }

    private async Task<Error?> ApplyTargetsAsync(AllocationRule rule, IReadOnlyList<AllocationTargetRequest> targets, CancellationToken ct)
    {
        string? invalid = OverheadAllocation.Validate(rule.Basis, [.. targets.Select(t => t.Driver)]);
        if (invalid is not null)
        {
            return Error.Validation("Alloc.Drivers", invalid);
        }

        List<string> costCentres = [.. targets.Select(t => t.CostCentre.Trim().ToUpperInvariant())];
        if (costCentres.Contains(rule.SourceCostCentre))
        {
            return Error.Rule("Alloc.SelfCharge", $"Cost centre {rule.SourceCostCentre} cannot receive its own overhead.");
        }

        if (costCentres.Distinct(StringComparer.Ordinal).Count() != costCentres.Count)
        {
            return Error.Validation("Alloc.DuplicateTarget", "Each receiving cost centre may appear once.");
        }

        List<string> wanted = [rule.SourceCostCentre, .. costCentres];
        List<string> known = await _db.CostCentres.Where(c => c.CompanyId == rule.CompanyId && c.IsActive && wanted.Contains(c.Code))
            .Select(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
        string? missing = wanted.FirstOrDefault(c => !known.Contains(c));
        if (missing is not null)
        {
            return Error.NotFound("Alloc.CostCentre", $"Cost centre {missing} does not exist or is inactive.");
        }

        rule.SetTargets(targets.Select(t => (t.CostCentre, t.Department, t.Driver)));
        return null;
    }
}
