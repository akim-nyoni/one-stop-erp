using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Banking;

/// <summary>Maintains bank rules and applies them through <see cref="IBankStatementPostingService"/>.</summary>
public sealed class BankRuleService : IBankRuleService
{
    private readonly One4AllDbContext _db;
    private readonly IBankStatementPostingService _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The statement-line posting service.</param>
    public BankRuleService(One4AllDbContext db, IBankStatementPostingService posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateRuleAsync(CreateBankRuleRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DescriptionContains))
        {
            return Error.Validation("BankRule.Phrase", "A bank rule needs the phrase the statement description contains.");
        }

        if (string.IsNullOrWhiteSpace(request.AccountCode))
        {
            return Error.Validation("BankRule.Account", "A bank rule needs the account to post to.");
        }

        if (request.MinAmount is { } min && request.MaxAmount is { } max && min > max)
        {
            return Error.Validation("BankRule.Range", "The minimum amount is above the maximum.");
        }

        string code = request.AccountCode.Trim().ToUpperInvariant();
        var account = await _db.Accounts
            .Where(a => a.CompanyId == request.CompanyId && a.AccountCode == code)
            .Select(a => new { a.ControlAccountType })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is null)
        {
            return Error.NotFound("BankRule.AccountNotFound", $"Account {code} not found.");
        }

        if (account.ControlAccountType is ControlAccountType.AccountsReceivable or ControlAccountType.AccountsPayable or ControlAccountType.Stock or ControlAccountType.GoodsReceivedNotInvoiced)
        {
            return Error.Rule("BankRule.ControlAccount", $"{code} is the {account.ControlAccountType} control account and cannot be posted to by a rule.");
        }

        if (request.BankAccountId is { } bankId && !await _db.Set<BankAccount>().AnyAsync(b => b.Id == bankId && b.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Cb.BankNotFound", $"Bank account {bankId} not found.");
        }

        BankRule rule = new(request.CompanyId, request.BankAccountId,
            string.IsNullOrWhiteSpace(request.Name) ? request.DescriptionContains : request.Name,
            request.DescriptionContains, request.Direction, code)
        {
            MinAmount = request.MinAmount,
            MaxAmount = request.MaxAmount,
            Narrative = string.IsNullOrWhiteSpace(request.Narrative) ? null : request.Narrative.Trim(),
            Priority = request.Priority,
        };
        _db.Set<BankRule>().Add(rule);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rule.Id;
    }

    /// <inheritdoc />
    public async Task<Result> SetActiveAsync(long companyId, long ruleId, bool isActive, CancellationToken ct = default)
    {
        BankRule? rule = await _db.Set<BankRule>().FirstOrDefaultAsync(r => r.Id == ruleId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("BankRule.NotFound", $"Bank rule {ruleId} not found."));
        }

        rule.IsActive = isActive;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankRule>> ListAsync(long companyId, CancellationToken ct = default) =>
        await _db.Set<BankRule>()
            .Where(r => r.CompanyId == companyId)
            .OrderBy(r => r.Priority).ThenBy(r => r.Name)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<BankRuleRunResult>> ApplyAsync(long companyId, long bankAccountId, DateOnly upToDate, CancellationToken ct = default)
    {
        if (!await _db.Set<BankAccount>().AnyAsync(b => b.Id == bankAccountId && b.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Cb.BankNotFound", $"Bank account {bankAccountId} not found.");
        }

        List<BankRule> rules = await _db.Set<BankRule>()
            .Where(r => r.CompanyId == companyId && r.IsActive && (r.BankAccountId == null || r.BankAccountId == bankAccountId))
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, BankRule> byId = rules.ToDictionary(r => r.Id);
        List<BankRuleSpec> specs = rules.Select(r => new BankRuleSpec(r.Id, r.BankAccountId, r.DescriptionContains, r.Direction, r.MinAmount, r.MaxAmount, r.Priority)).ToList();

        var lines = await _db.Set<BankStatementLine>()
            .Where(l => l.CompanyId == companyId && l.BankAccountId == bankAccountId && !l.IsReconciled && l.StatementDate <= upToDate)
            .OrderBy(l => l.StatementDate).ThenBy(l => l.Id)
            .Select(l => new { l.Id, l.Description, l.Amount })
            .ToListAsync(ct).ConfigureAwait(false);

        List<BankRulePosting> posted = [];
        List<string> failures = [];
        foreach (var line in lines)
        {
            BankRuleSpec? match = BankRuleMatcher.Match(specs, bankAccountId, line.Description, line.Amount);
            if (match is null)
            {
                continue;
            }

            BankRule rule = byId[match.Id];
            Result<StatementLinePosting> result = await _posting.PostToAccountAsync(companyId, bankAccountId, line.Id, rule.AccountCode, rule.Narrative, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                failures.Add($"{line.Description} ({line.Amount:0.00}) — rule '{rule.Name}': {result.Error.Message}");
                continue;
            }

            posted.Add(new BankRulePosting(line.Id, rule.Name, result.Value.JournalNumber, line.Amount));
        }

        return new BankRuleRunResult(posted, failures);
    }
}
