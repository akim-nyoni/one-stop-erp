using Microsoft.EntityFrameworkCore;
using One4All.Core.Budgeting;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// The single, non-bypassable posting engine (brief §3.2). Validates and posts one balanced journal
/// inside one <see cref="One4AllDbContext.SaveChangesAsync"/> transaction, so the journal, its gapless
/// number and the period-balance updates commit together or not at all. The audit trail is written by
/// the change interceptor in the same transaction.
/// </summary>
public sealed class PostingEngine : IPostingEngine
{
    private const string JournalDocumentType = "GLJournal";
    private const decimal BalanceTolerance = 0m;

    private readonly One4AllDbContext _db;
    private readonly IServiceScopeFactory? _scopes;

    /// <summary>Creates the engine.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="scopes">Opens a separate unit of work to record a fund-control block that outlives the refused posting.</param>
    /// <param name="licence">The licence: nothing posts while it is read-only or unusable.</param>
    public PostingEngine(One4AllDbContext db, IServiceScopeFactory? scopes = null, Application.Abstractions.Licensing.ILicenceService? licence = null,
        Application.Abstractions.Security.IPermissionService? permissions = null, One4All.Licensing.Core.IClock? clock = null,
        Application.Abstractions.Security.IAuditService? audit = null, Application.Abstractions.Security.ICurrentUser? caller = null)
    {
        _audit = audit;
        _caller = caller;
        _permissions = permissions;
        _clock = clock;
        _db = db;
        _scopes = scopes;
        _licence = licence;
    }

    private readonly Application.Abstractions.Licensing.ILicenceService? _licence;
    private readonly Application.Abstractions.Security.IPermissionService? _permissions;
    private readonly One4All.Licensing.Core.IClock? _clock;
    private readonly Application.Abstractions.Security.IAuditService? _audit;
    private readonly Application.Abstractions.Security.ICurrentUser? _caller;

    /// <inheritdoc />
    public Task<Result<PostingReceipt>> PostAsync(JournalRequest request, CancellationToken ct = default)
        => _licence?.CanPost() is { IsFailure: true } refused ? Task.FromResult<Result<PostingReceipt>>(refused.Error) : _db.AtomicAsync(() => PostCoreAsync(request, ct), ct);

    // One unit of work, retried if it loses a race with another posting for the sequence or a period balance.
    private async Task<Result<PostingReceipt>> PostCoreAsync(JournalRequest request, CancellationToken ct)
    {
        // A request already posted with the same idempotency key gets the original journal back, not a second one.
        string? idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (idempotencyKey is not null
            && await _db.Journals.AsNoTracking().Include(j => j.Lines).FirstOrDefaultAsync(j => j.CompanyId == request.CompanyId && j.IdempotencyKey == idempotencyKey, ct).ConfigureAwait(false)
                is JournalEntry earlier)
        {
            return earlier.Status == JournalStatus.Cancelled
                ? Error.Conflict("Posting.KeyUsed", $"Idempotency key {idempotencyKey} belongs to journal {earlier.JournalNumber}, which was rejected; send a new key.")
                : new PostingReceipt(earlier.Id, earlier.JournalNumber, earlier.PostingDate, earlier.Lines.Sum(l => l.BaseDebit), earlier.Status == JournalStatus.Deferred);
        }

        if (request.Lines.Count == 0)
        {
            return Error.Validation("Posting.NoLines", "A journal must have at least one line.");
        }

        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Posting.CompanyNotFound", $"Company {request.CompanyId} not found.");
        }

        CurrencyCode baseCurrency = company.BaseCurrency;
        if ((request.TransactionCurrency != baseCurrency || request.Lines.Any(l => l.Currency is CurrencyCode lineCurrency && lineCurrency != baseCurrency))
            && Licensing.LicenceFeatureGate.Check(_licence, One4All.Licensing.Core.LicenceFeatures.MultiCurrency, "Posting in a foreign currency") is { IsFailure: true } foreign)
        {
            return foreign.Error;
        }

        // Back-dating: a person posting further back than the company allows needs the back-date permission (brief §5.1).
        if (company.BackDateAllowanceDays is int allowance && (request.PostedByUserId ?? request.SubmittedByUserId) is long actor)
        {
            DateOnly today = DateOnly.FromDateTime((_clock?.UtcNow ?? DateTimeOffset.UtcNow).UtcDateTime);
            if (request.PostingDate < today.AddDays(-allowance)
                && (_permissions is null || !(await _permissions.ResolveAsync(actor, ct).ConfigureAwait(false)).Has("GeneralLedger.Journal", Core.Security.OperationFlags.BackDate)))
            {
                return Error.Forbidden("Posting.BackDateNotAllowed",
                    $"{request.PostingDate:yyyy-MM-dd} is more than {allowance} day(s) back: only a user with the back-date permission may post it.");
            }
        }

        // Resolve the open period for the posting module.
        Result<(FiscalYear year, FiscalPeriod period)> periodResult =
            await ResolvePeriodAsync(request.CompanyId, request.SourceModule, request.PostingDate, company.AllowFuturePeriodPosting, ct, request.ToAdjustmentPeriod,
                    request.PostedByUserId ?? request.SubmittedByUserId)
                .ConfigureAwait(false);
        if (periodResult.IsFailure)
        {
            return periodResult.Error;
        }

        (FiscalYear fiscalYear, FiscalPeriod fiscalPeriod) = periodResult.Value;

        Result<(JournalEntry Journal, decimal TotalBaseDebit)> built = await BuildJournalAsync(request, company, idempotencyKey, ct).ConfigureAwait(false);
        if (built.IsFailure)
        {
            return built.Error;
        }

        (JournalEntry journal, decimal totalBaseDebit) = built.Value;

        // A bank account reconciled and signed off to a date takes no more entries on or before it - except a valuation entry, which restates base amounts only.
        List<string> lineAccounts = [.. journal.Lines.Select(l => l.AccountCode).Distinct()];
        string? lockedBank = request.ValuationOnly ? null : await _db.Set<Core.Banking.BankAccount>()
            .Where(b => b.CompanyId == request.CompanyId && lineAccounts.Contains(b.GlAccountCode) && b.ReconciledTo != null && b.ReconciledTo >= request.PostingDate)
            .Select(b => b.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (lockedBank is not null)
        {
            return Error.Rule("Posting.BankReconciled", $"Bank account {lockedBank} is reconciled past {request.PostingDate:yyyy-MM-dd}; post the entry in an open period.");
        }

        // Fund control: no expenditure against a vote without an appropriation.
        if (company.FundControlEnabled && await CheckAppropriationAsync(request.CompanyId, fiscalYear.Id, journal, ct).ConfigureAwait(false) is { } unappropriated)
        {
            JournalLine refused = journal.Lines.First(l => l.AccountCode == unappropriated.Account && l.BaseDebit > 0m);
            BudgetControlEvent decision = new(request.CompanyId, BudgetControlOutcome.Blocked, request.PostingDate, refused.AccountCode, refused.Department, refused.BaseDebit,
                $"{request.DocumentType} {request.DocumentNumber}".Trim());
            bool overridden = !string.IsNullOrWhiteSpace(request.BudgetOverrideReason) && request.BudgetOverrideAuthorisedByUserId is long by && by != request.SubmittedByUserId;
            if (!overridden)
            {
                await RecordBlockAsync(decision, ct).ConfigureAwait(false);
                return unappropriated.Error;
            }

            BudgetControlEvent overrideEvent = new(request.CompanyId, BudgetControlOutcome.Overridden, request.PostingDate, refused.AccountCode, refused.Department, refused.BaseDebit, decision.Document)
            {
                Reason = request.BudgetOverrideReason!.Trim(), AuthorisedByUserId = request.BudgetOverrideAuthorisedByUserId,
            };
            _db.Set<BudgetControlEvent>().Add(overrideEvent);
        }

        // Allocate the gapless journal number (increment persists in this transaction).
        string journalNumber = await AllocateJournalNumberAsync(request.CompanyId, request.PostingDate.Year, ct).ConfigureAwait(false);

        DateTimeOffset entryDate = DateTimeOffset.UtcNow;
        // Held: asked for, awaiting approval, or from a module the company queues for a controlled release (brief §3.2 rule 8).
        bool held = request.Deferred || request.SubmittedByUserId is not null || company.DefersPostingsFor(request.SourceModule);
        if (held)
        {
            journal.MarkDeferred(journalNumber, fiscalYear.Id, fiscalPeriod.Id, entryDate);
            if (request.SubmittedByUserId is long preparer)
            {
                journal.HoldForApproval(preparer);
            }
        }
        else
        {
            journal.MarkPosted(journalNumber, fiscalYear.Id, fiscalPeriod.Id, entryDate);
            await UpdatePeriodBalancesAsync(request.CompanyId, fiscalYear.Id, fiscalPeriod.Id, journal, ct).ConfigureAwait(false);
        }

        _db.Journals.Add(journal);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (!held)
        {
            await Security.SemanticAudit.RecordAsync(_audit, _caller, request.CompanyId, journal.SourceModule, "JournalEntry", journalNumber, Core.Audit.AuditAction.Post,
                $"{request.DocumentType} {request.DocumentNumber} {totalBaseDebit:0.00}".Trim(), ct: ct).ConfigureAwait(false);
        }

        return new PostingReceipt(journal.Id, journalNumber, request.PostingDate, totalBaseDebit, held);
    }

    // The journal built from a request and checked: its lines against the accounts, the balance in base and per currency, the funds, the reporting
    // amounts and the maker-checker threshold. Shared by the single and the batch paths.
    private async Task<Result<(JournalEntry Journal, decimal TotalBaseDebit)>> BuildJournalAsync(JournalRequest request, Company company, string? idempotencyKey, CancellationToken ct)
    {
        CurrencyCode baseCurrency = company.BaseCurrency;
        // Build and validate the lines.
        JournalEntry journal = new(request.CompanyId, request.SourceModule, request.PostingDate, baseCurrency, request.TransactionCurrency)
        {
            IsManual = request.EnforceManualJournalRules,
            Terminal = _caller?.Terminal ?? _caller?.ClientAddress,
        };
        journal.DocumentType = request.DocumentType;
        journal.DocumentNumber = request.DocumentNumber;
        journal.Reference = request.Reference;
        journal.Narrative = request.Narrative;
        journal.IdempotencyKey = idempotencyKey;

        Dictionary<string, (decimal debit, decimal credit)> perCurrency = new(StringComparer.Ordinal);
        decimal totalBaseDebit = 0m;
        decimal totalBaseCredit = 0m;

        foreach (JournalLineRequest lineRequest in request.Lines)
        {
            Result<JournalLine> lineResult = await BuildLineAsync(request, lineRequest, baseCurrency, company.FundAccounting ? company.DefaultFund : null, ct).ConfigureAwait(false);
            if (lineResult.IsFailure)
            {
                return lineResult.Error;
            }

            JournalLine line = lineResult.Value;
            journal.AddLine(line);

            totalBaseDebit += line.BaseDebit;
            totalBaseCredit += line.BaseCredit;

            string cur = line.TransactionCurrency.Value;
            (decimal debit, decimal credit) running = perCurrency.GetValueOrDefault(cur);
            perCurrency[cur] = (running.debit + line.TransactionDebit, running.credit + line.TransactionCredit);
        }

        // Fund accounting: every line names a fund, and each fund balances on its own.
        if (company.FundAccounting)
        {
            if (journal.Lines.FirstOrDefault(l => l.Fund is null) is JournalLine unfunded)
            {
                return Error.Rule("Posting.FundRequired", $"Fund accounting is on: the line to '{unfunded.AccountCode}' must name a fund.");
            }

            foreach (IGrouping<string?, JournalLine> fundLines in journal.Lines.GroupBy(l => l.Fund))
            {
                decimal difference = fundLines.Sum(l => l.BaseDebit) - fundLines.Sum(l => l.BaseCredit);
                if (Math.Abs(difference) > BalanceTolerance)
                {
                    return Error.Rule("Posting.FundOutOfBalance",
                        $"Fund {fundLines.Key} does not balance ({difference:0.00}); move money between funds with a balanced inter-fund entry in each.");
                }
            }
        }

        // Rule 1: Dr = Cr in base currency.
        if (Math.Abs(totalBaseDebit - totalBaseCredit) > BalanceTolerance)
        {
            return Error.Rule("Posting.OutOfBalance",
                $"Journal does not balance in base currency ({baseCurrency}): debits {totalBaseDebit:0.0000} vs credits {totalBaseCredit:0.0000}.");
        }

        // Rule 1 (continued): Dr = Cr per transaction currency - for a journal in one currency. A multi-currency journal (a till's euros against a
        // base-currency sale) balances in base, which rule 1 already holds it to.
        foreach ((string currency, (decimal debit, decimal credit)) in perCurrency.Count == 1 ? perCurrency : [])
        {
            if (Math.Abs(debit - credit) > BalanceTolerance)
            {
                return Error.Rule("Posting.OutOfBalanceCurrency",
                    $"Journal does not balance in {currency}: debits {debit:0.0000} vs credits {credit:0.0000}.");
            }
        }

        // Rule 4: the reporting currency, when the company has one, is kept on every posting.
        if (company.ReportingCurrency is CurrencyCode reportingCurrency
            && await ApplyReportingAmountsAsync(journal, request, baseCurrency, reportingCurrency, ct).ConfigureAwait(false) is Error reportingError)
        {
            return reportingError;
        }

        // Maker-checker: a manual journal above the company's threshold must go through approval.
        if (request.EnforceManualJournalRules && request.SubmittedByUserId is null
            && company.ManualJournalApprovalThreshold is decimal threshold && totalBaseDebit > threshold)
        {
            return Error.Rule("Posting.ApprovalRequired",
                $"Manual journals over {threshold:0.00} {baseCurrency} need a second user's approval: submit the journal for approval.");
        }

        return (journal, totalBaseDebit);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PostingReceipt>>> PostBatchAsync(IReadOnlyList<JournalRequest> requests, CancellationToken ct = default)
        => _licence?.CanPost() is { IsFailure: true } refused ? Task.FromResult<Result<IReadOnlyList<PostingReceipt>>>(refused.Error) : _db.AtomicAsync(() => PostBatchCoreAsync(requests, ct), ct);

    // Several journals as one unit of work: the company, the periods and the accounts are read once, the journal counter is locked once and
    // allocates every number, the period balances the batch touches are read in one query and updated in memory, and one save writes the lot
    // (one audit chain extension). What needs the single path's per-journal checks is refused, and the caller posts those one by one.
    private async Task<Result<IReadOnlyList<PostingReceipt>>> PostBatchCoreAsync(IReadOnlyList<JournalRequest> requests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return Array.Empty<PostingReceipt>();
        }

        long companyId = requests[0].CompanyId;
        Company? batchCompany = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        foreach (JournalRequest request in requests)
        {
            if (request.CompanyId != companyId || request.Lines.Count == 0 || request.Deferred || request.SubmittedByUserId is not null || request.EnforceManualJournalRules
                || (batchCompany?.DefersPostingsFor(request.SourceModule) ?? false)
                || request.ToAdjustmentPeriod || !string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.BudgetOverrideReason is not null)
            {
                return Error.Rule("Posting.BatchUnsupported", "A deferred, submitted, manual, adjustment-period, keyed or budget-overridden journal posts on its own.");
            }
        }

        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Posting.CompanyNotFound", $"Company {companyId} not found.");
        }

        if (company.FundControlEnabled)
        {
            return Error.Rule("Posting.BatchUnsupported", "Fund control checks each journal's appropriation on its own.");
        }

        CurrencyCode baseCurrency = company.BaseCurrency;
        if (requests.Any(r => r.TransactionCurrency != baseCurrency || r.Lines.Any(l => l.Currency is CurrencyCode lineCurrency && lineCurrency != baseCurrency))
            && Licensing.LicenceFeatureGate.Check(_licence, One4All.Licensing.Core.LicenceFeatures.MultiCurrency, "Posting in a foreign currency") is { IsFailure: true } foreign)
        {
            return foreign.Error;
        }

        // Back-dating is checked once per person for the oldest date they post.
        if (company.BackDateAllowanceDays is int allowance)
        {
            DateOnly today = DateOnly.FromDateTime((_clock?.UtcNow ?? DateTimeOffset.UtcNow).UtcDateTime);
            foreach (IGrouping<long, JournalRequest> byActor in requests.Where(r => (r.PostedByUserId ?? r.SubmittedByUserId) is not null).GroupBy(r => (r.PostedByUserId ?? r.SubmittedByUserId)!.Value))
            {
                DateOnly oldest = byActor.Min(r => r.PostingDate);
                if (oldest < today.AddDays(-allowance)
                    && (_permissions is null || !(await _permissions.ResolveAsync(byActor.Key, ct).ConfigureAwait(false)).Has("GeneralLedger.Journal", Core.Security.OperationFlags.BackDate)))
                {
                    return Error.Forbidden("Posting.BackDateNotAllowed",
                        $"{oldest:yyyy-MM-dd} is more than {allowance} day(s) back: only a user with the back-date permission may post it.");
                }
            }
        }

        // One period per module and date.
        Dictionary<(string Module, DateOnly Date), (FiscalYear Year, FiscalPeriod Period)> periods = [];
        foreach (JournalRequest request in requests)
        {
            if (periods.ContainsKey((request.SourceModule, request.PostingDate)))
            {
                continue;
            }

            Result<(FiscalYear year, FiscalPeriod period)> periodResult = await ResolvePeriodAsync(request.CompanyId, request.SourceModule, request.PostingDate, company.AllowFuturePeriodPosting, ct,
                false, request.PostedByUserId ?? request.SubmittedByUserId).ConfigureAwait(false);
            if (periodResult.IsFailure)
            {
                return periodResult.Error;
            }

            periods[(request.SourceModule, request.PostingDate)] = periodResult.Value;
        }

        List<(JournalRequest Request, JournalEntry Journal, decimal TotalBaseDebit)> built = [];
        foreach (JournalRequest request in requests)
        {
            Result<(JournalEntry Journal, decimal TotalBaseDebit)> journal = await BuildJournalAsync(request, company, null, ct).ConfigureAwait(false);
            if (journal.IsFailure)
            {
                return journal.Error;
            }

            built.Add((request, journal.Value.Journal, journal.Value.TotalBaseDebit));
        }

        // A bank account reconciled and signed off to a date takes no more entries on or before it: one check for the batch's accounts and dates.
        List<string> lineAccounts = [.. built.SelectMany(b => b.Journal.Lines.Select(l => l.AccountCode)).Distinct()];
        DateOnly earliest = requests.Min(r => r.PostingDate);
        string? lockedBank = await _db.Set<Core.Banking.BankAccount>()
            .Where(b => b.CompanyId == companyId && lineAccounts.Contains(b.GlAccountCode) && b.ReconciledTo != null && b.ReconciledTo >= earliest)
            .Select(b => b.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (lockedBank is not null)
        {
            return Error.Rule("Posting.BankReconciled", $"Bank account {lockedBank} is reconciled past {earliest:yyyy-MM-dd}; post the entries in an open period.");
        }

        // The period balances the batch touches, read once; the rest are made.
        List<long> periodIds = [.. periods.Values.Select(v => v.Period.Id).Distinct()];
        foreach (GlPeriodBalance tracked in _db.PeriodBalances.Local.Where(b => b.CompanyId == companyId && periodIds.Contains(b.FiscalPeriodId)).ToList())
        {
            ForgetIfUnchanged(tracked);
        }

        Dictionary<(long Period, string Account, string CostCentre, string Department), GlPeriodBalance> balances = (await _db.PeriodBalances
                .Where(b => b.CompanyId == companyId && periodIds.Contains(b.FiscalPeriodId) && lineAccounts.Contains(b.AccountCode)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(b => (b.FiscalPeriodId, b.AccountCode, b.CostCentre, b.Department));

        NumberSequence sequence = await JournalSequenceAsync(companyId, ct).ConfigureAwait(false);
        DateTimeOffset entryDate = DateTimeOffset.UtcNow;
        List<PostingReceipt> receipts = [];
        foreach ((JournalRequest request, JournalEntry journal, decimal totalBaseDebit) in built)
        {
            (FiscalYear year, FiscalPeriod period) = periods[(request.SourceModule, request.PostingDate)];
            string journalNumber = sequence.Allocate(request.PostingDate.Year);
            journal.MarkPosted(journalNumber, year.Id, period.Id, entryDate);
            foreach (JournalLine line in journal.Lines)
            {
                AccountKey key = line.Key;
                if (!balances.TryGetValue((period.Id, key.AccountCode, key.CostCentre, key.Department), out GlPeriodBalance? balance))
                {
                    balance = new GlPeriodBalance(companyId, year.Id, period.Id, key);
                    _db.PeriodBalances.Add(balance);
                    balances[(period.Id, key.AccountCode, key.CostCentre, key.Department)] = balance;
                }

                balance.AddMovement(line.BaseDebit, line.BaseCredit, line.ReportingDebit, line.ReportingCredit);
            }

            _db.Journals.Add(journal);
            receipts.Add(new PostingReceipt(0, journalNumber, request.PostingDate, totalBaseDebit, false));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        for (int i = 0; i < receipts.Count; i++)
        {
            receipts[i] = receipts[i] with { JournalId = built[i].Journal.Id };
        }

        await Security.SemanticAudit.RecordAsync(_audit, _caller, companyId, requests[0].SourceModule, "JournalEntry", $"{receipts[0].JournalNumber}..{receipts[^1].JournalNumber}", Core.Audit.AuditAction.Post,
            $"{receipts.Count} journals {requests[0].DocumentType} {built.Sum(b => b.TotalBaseDebit):0.00}".Trim(), ct: ct).ConfigureAwait(false);
        return receipts;
    }

    /// <inheritdoc />
    public Task<Result<PostingReceipt>> ReleaseDeferredAsync(long companyId, long journalId, CancellationToken ct = default)
        => _licence?.CanPost() is { IsFailure: true } refused ? Task.FromResult<Result<PostingReceipt>>(refused.Error)
            : _db.AtomicAsync(() => ReleaseDeferredCoreAsync(companyId, journalId, ct), ct);

    // One unit of work, retried if it loses a race with another posting for the sequence or a period balance.
    private async Task<Result<PostingReceipt>> ReleaseDeferredCoreAsync(long companyId, long journalId, CancellationToken ct)
    {
        JournalEntry? journal = await _db.Journals.Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == journalId && j.CompanyId == companyId, ct).ConfigureAwait(false);
        if (journal is null)
        {
            return Error.NotFound("Gl.JournalNotFound", $"Journal {journalId} not found.");
        }

        if (journal.Status != JournalStatus.Deferred)
        {
            return Error.Rule("Posting.NotDeferred", $"Journal {journal.JournalNumber} is {journal.Status}, not deferred.");
        }

        if (journal.AwaitingApproval)
        {
            return Error.Rule("Posting.AwaitingApproval", $"Journal {journal.JournalNumber} is awaiting approval.");
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);

        // The period must still be open for the module now that the journal is really being posted.
        Result<(FiscalYear, FiscalPeriod)> periodResult =
            await ResolvePeriodAsync(companyId, journal.SourceModule, journal.PostingDate, company.AllowFuturePeriodPosting, ct).ConfigureAwait(false);
        if (periodResult.IsFailure)
        {
            return periodResult.Error;
        }

        (FiscalYear fiscalYear, FiscalPeriod fiscalPeriod) = periodResult.Value;

        journal.MarkPosted(journal.JournalNumber, fiscalYear.Id, fiscalPeriod.Id, DateTimeOffset.UtcNow);
        await UpdatePeriodBalancesAsync(companyId, fiscalYear.Id, fiscalPeriod.Id, journal, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await Security.SemanticAudit.RecordAsync(_audit, _caller, companyId, journal.SourceModule, "JournalEntry", journal.JournalNumber, Core.Audit.AuditAction.Post,
            $"Released {journal.DocumentType} {journal.DocumentNumber}".Trim(), ct: ct).ConfigureAwait(false);

        return new PostingReceipt(journal.Id, journal.JournalNumber, journal.PostingDate, journal.Lines.Sum(l => l.BaseDebit), false);
    }

    /// <inheritdoc />
    public async Task<DeferredReleaseResult> ReleaseAllDeferredAsync(long companyId, DateOnly upToDate, CancellationToken ct = default)
    {
        List<long> ids = await _db.Journals
            .Where(j => j.CompanyId == companyId && j.Status == JournalStatus.Deferred && !j.AwaitingApproval && j.PostingDate <= upToDate)
            .OrderBy(j => j.PostingDate).ThenBy(j => j.Id)
            .Select(j => j.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        List<string> released = [];
        List<DeferredReleaseFailure> failed = [];
        foreach (long id in ids)
        {
            Result<PostingReceipt> result = await ReleaseDeferredAsync(companyId, id, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                released.Add(result.Value.JournalNumber);
            }
            else
            {
                string number = await _db.Journals.Where(j => j.Id == id).Select(j => j.JournalNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                failed.Add(new DeferredReleaseFailure(id, number, result.Error.Message));
            }
        }

        return new DeferredReleaseResult(released, failed);
    }

    /// <inheritdoc />
    public Task<Result<PostingReceipt>> ReverseAsync(long companyId, long journalId, DateOnly date, string reason, CancellationToken ct = default)
        => _licence?.CanPost() is { IsFailure: true } refused ? Task.FromResult<Result<PostingReceipt>>(refused.Error)
            : _db.AtomicAsync(() => ReverseCoreAsync(companyId, journalId, date, reason, ct), ct);

    // One unit of work, retried if it loses a race with another posting for the sequence or a period balance.
    private async Task<Result<PostingReceipt>> ReverseCoreAsync(long companyId, long journalId, DateOnly date, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Gl.ReasonRequired", "A journal reversal requires a reason.");
        }

        JournalEntry? original = await _db.Journals.Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == journalId && j.CompanyId == companyId, ct).ConfigureAwait(false);
        if (original is null)
        {
            return Error.NotFound("Gl.JournalNotFound", $"Journal {journalId} not found.");
        }

        if (original.Status != JournalStatus.Posted)
        {
            return Error.Rule("Gl.NotPosted", $"Journal {original.JournalNumber} is {original.Status}; only a posted journal can be reversed.");
        }

        if (original.IsReversal)
        {
            return Error.Rule("Gl.NotReversible", "A reversal journal cannot itself be reversed.");
        }

        bool alreadyReversed = await _db.Journals.AnyAsync(j => j.CompanyId == companyId && j.ReversalOfJournalId == journalId, ct).ConfigureAwait(false);
        if (alreadyReversed)
        {
            return Error.Rule("Gl.AlreadyReversed", $"Journal {journalId} has already been reversed.");
        }

        // Post the exact inverse: swap each line's debit and credit (preserving the original base amounts).
        List<JournalLineRequest> lines = original.Lines.Select(l => new JournalLineRequest
        {
            AccountCode = l.AccountCode,
            CostCentre = string.IsNullOrEmpty(l.CostCentre) ? null : l.CostCentre,
            Department = string.IsNullOrEmpty(l.Department) ? null : l.Department,
            Project = l.Project,
            Fund = l.Fund,
            Branch = l.Branch,
            Side = l.TransactionDebit != 0m ? JournalSide.Credit : JournalSide.Debit,
            TransactionAmount = l.TransactionDebit != 0m ? l.TransactionDebit : l.TransactionCredit,
            ExchangeRate = l.ExchangeRate,
            BaseAmount = l.TransactionDebit != 0m ? l.BaseDebit : l.BaseCredit,
            ReportingAmount = l.ReportingDebit != 0m || l.ReportingCredit != 0m ? l.ReportingDebit + l.ReportingCredit : null,
        }).ToList();

        // A journal in the adjustment period is reversed there when reversed on the same day.
        bool inAdjustment = date == original.PostingDate
            && await _db.Set<FiscalPeriod>().AnyAsync(p => p.Id == original.FiscalPeriodId && p.IsAdjustment, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = original.SourceModule,
            PostingDate = date,
            ToAdjustmentPeriod = inAdjustment,
            TransactionCurrency = original.TransactionCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "REVERSAL",
            DocumentNumber = original.JournalNumber,
            Narrative = $"Reversal of {original.JournalNumber}: {reason}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        // The original stays Posted so it and its inverse both net to zero in the ledger (accrual-style reversal).
        JournalEntry reversal = await _db.Journals.FirstAsync(j => j.Id == posted.Value.JournalId, ct).ConfigureAwait(false);
        reversal.MarkAsReversalOf(original.Id);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await Security.SemanticAudit.RecordAsync(_audit, _caller, companyId, original.SourceModule, "JournalEntry", original.JournalNumber, Core.Audit.AuditAction.Void,
            $"Reversed by {posted.Value.JournalNumber}", reason, ct: ct).ConfigureAwait(false);

        return posted.Value;
    }

    // Accounts this engine has read (a long run of postings reads each once): a copy this context tracks wins, as it may hold unsaved changes.
    private readonly Dictionary<(long CompanyId, string Code), LedgerAccount> _accounts = [];

    private async Task<LedgerAccount?> AccountAsync(long companyId, string code, CancellationToken ct)
    {
        if (_db.Accounts.Local.FirstOrDefault(a => a.CompanyId == companyId && a.AccountCode == code) is LedgerAccount tracked)
        {
            return tracked;
        }

        if (_accounts.TryGetValue((companyId, code), out LedgerAccount? known))
        {
            return known;
        }

        LedgerAccount? account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AccountCode == code, ct).ConfigureAwait(false);
        if (account is not null)
        {
            _accounts[(companyId, code)] = account;
        }

        return account;
    }

    private async Task<Result<JournalLine>> BuildLineAsync(JournalRequest request, JournalLineRequest lineRequest, CurrencyCode baseCurrency, string? defaultFund, CancellationToken ct)
    {
        AccountKey key = lineRequest.ToKey();
        LedgerAccount? account = await AccountAsync(request.CompanyId, key.AccountCode, ct).ConfigureAwait(false);

        if (account is null)
        {
            return Error.NotFound("Posting.AccountNotFound", $"Account '{key.AccountCode}' does not exist.");
        }

        if (!account.IsActive)
        {
            return Error.Rule("Posting.AccountInactive", $"Account '{key.AccountCode}' is not active.");
        }

        if (request.EnforceManualJournalRules && account.IsControlAccount)
        {
            return Error.Rule("Posting.ControlAccountManualJournal",
                $"Account '{key.AccountCode}' is a control account and cannot be posted by manual journal.");
        }

        if (request.EnforceManualJournalRules && !account.AllowManualJournal)
        {
            return Error.Rule("Posting.ManualJournalNotAllowed",
                $"Account '{key.AccountCode}' does not allow manual journals; it is posted by the system only.");
        }

        CurrencyCode lineCurrency = lineRequest.Currency ?? request.TransactionCurrency;
        if (!account.AllowsCurrency(lineCurrency))
        {
            return Error.Rule("Posting.CurrencyNotAllowed",
                $"Account '{key.AccountCode}' is restricted to {account.CurrencyRestriction} and cannot take {lineCurrency}.");
        }

        if (lineRequest.TransactionAmount < 0m)
        {
            return Error.Validation("Posting.NegativeAmount", "Line amounts must be non-negative; use the opposite side instead.");
        }

        // Resolve the exchange rate and base amount.
        decimal rate;
        if (lineCurrency == baseCurrency)
        {
            rate = 1m;
        }
        else if (lineRequest.ExchangeRate is { } explicitRate and > 0m)
        {
            rate = explicitRate;
        }
        else
        {
            Result<decimal> rateResult = await LookupRateAsync(request, lineCurrency, baseCurrency, ct).ConfigureAwait(false);
            if (rateResult.IsFailure)
            {
                return rateResult.Error;
            }

            rate = rateResult.Value;
        }

        decimal baseAmount = lineRequest.BaseAmount ?? decimal.Round(lineRequest.TransactionAmount * rate, Money.StorageScale, MidpointRounding.ToEven);

        JournalLine line = lineRequest.Side == JournalSide.Debit
            ? JournalLine.Debit(key, lineCurrency, lineRequest.TransactionAmount, rate, baseAmount, lineRequest.Narrative)
            : JournalLine.Credit(key, lineCurrency, lineRequest.TransactionAmount, rate, baseAmount, lineRequest.Narrative);
        line.SetTaxCode(lineRequest.TaxCode);
        line.SetAnalysis(lineRequest.Project, string.IsNullOrWhiteSpace(lineRequest.Fund) ? defaultFund : lineRequest.Fund, lineRequest.Branch);

        // Project, fund and branch must be known and open; some accounts insist on them.
        if (line.Project is string project && !await _db.Projects.AnyAsync(p => p.CompanyId == request.CompanyId && p.Code == project && p.IsActive, ct).ConfigureAwait(false))
        {
            return Error.Rule("Posting.ProjectUnknown", $"Project '{project}' does not exist or is closed.");
        }

        if (line.Fund is string fund && !await _db.Funds.AnyAsync(f => f.CompanyId == request.CompanyId && f.Code == fund && f.IsActive, ct).ConfigureAwait(false))
        {
            return Error.Rule("Posting.FundUnknown", $"Fund '{fund}' does not exist or is inactive.");
        }

        if (line.Branch is string branch && !await _db.Branches.AnyAsync(b => b.CompanyId == request.CompanyId && b.Code == branch, ct).ConfigureAwait(false))
        {
            return Error.Rule("Posting.BranchUnknown", $"Branch '{branch}' does not exist.");
        }

        // Cost centre and department must be ones the company has set up and still uses (brief §3.2: "permitted for the company / cost centre /
        // department").
        if (key.CostCentre.Length > 0 && !await _db.CostCentres.AnyAsync(c => c.CompanyId == request.CompanyId && c.Code == key.CostCentre && c.IsActive, ct).ConfigureAwait(false))
        {
            return Error.Rule("Posting.CostCentreUnknown", $"Cost centre '{key.CostCentre}' does not exist or is inactive.");
        }

        if (key.Department.Length > 0 && !await _db.Departments.AnyAsync(d => d.CompanyId == request.CompanyId && d.Code == key.Department && d.IsActive, ct).ConfigureAwait(false))
        {
            return Error.Rule("Posting.DepartmentUnknown", $"Department '{key.Department}' does not exist or is inactive.");
        }

        if (account.RequiresProject && line.Project is null)
        {
            return Error.Rule("Posting.ProjectRequired", $"Postings to '{key.AccountCode}' must name a project.");
        }

        if (account.RequiresFund && line.Fund is null)
        {
            return Error.Rule("Posting.FundRequired", $"Postings to '{key.AccountCode}' must name a fund.");
        }

        return line;
    }

    // A debit to a budget-enabled expense account (a vote) needs an appropriation: a positive amount for the account
    // (and the line's cost centre, where the budget names one) in the year's active approved or supplementary budget,
    // company-wide or for the line's department.
    // A block is recorded in its own unit of work, because the refused posting's transaction rolls back.
    private async Task RecordBlockAsync(BudgetControlEvent decision, CancellationToken ct)
    {
        if (_scopes is null)
        {
            return;
        }

        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        One4AllDbContext log = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        log.Set<BudgetControlEvent>().Add(decision);
        await log.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<(Error Error, string Account)?> CheckAppropriationAsync(long companyId, long fiscalYearId, JournalEntry journal, CancellationToken ct)
    {
        List<JournalLine> debits = [.. journal.Lines.Where(l => l.BaseDebit > 0m)];
        List<string> codes = [.. debits.Select(l => l.AccountCode).Distinct()];
        List<string> votes = await _db.Accounts
            .Where(a => a.CompanyId == companyId && codes.Contains(a.AccountCode) && a.BudgetEnabled && a.Type == AccountType.Expense)
            .Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        foreach (JournalLine line in debits.Where(l => votes.Contains(l.AccountCode)))
        {
            string? costCentre = string.IsNullOrEmpty(line.CostCentre) ? null : line.CostCentre;
            string? department = string.IsNullOrEmpty(line.Department) ? null : line.Department;
            string? fund = string.IsNullOrEmpty(line.Fund) ? null : line.Fund;
            decimal appropriated = await _db.Budgets
                .Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.IsActive
                            && (b.Type == Core.Budgeting.BudgetType.OriginalApproved || b.Type == Core.Budgeting.BudgetType.SupplementaryRevised)
                            && (b.Department == null || b.Department == department))
                .SelectMany(b => b.Lines)
                .Where(l => l.AccountCode == line.AccountCode && (l.CostCentre == null || l.CostCentre == costCentre) && (l.Fund == null || l.Fund == fund))
                .SumAsync(l => (decimal?)l.Amount, ct).ConfigureAwait(false) ?? 0m;
            if (appropriated <= 0m)
            {
                return (Error.Rule("Posting.NoAppropriation",
                    $"Account '{line.AccountCode}'{(fund is null ? string.Empty : $" (fund {fund})")} has no appropriation in this year's active budget; fund control does not allow expenditure against it."),
                    line.AccountCode);
            }
        }

        return null;
    }

    // Each line's reporting amount: given (a reversal), the transaction amount when the line is in the reporting currency, the base
    // amount when reporting is the base currency, else the base amount at the base→reporting rate on the posting date. Rounding is
    // evened out on the largest computed line so the journal balances in the reporting currency too.
    private async Task<Error?> ApplyReportingAmountsAsync(JournalEntry journal, JournalRequest request, CurrencyCode baseCurrency, CurrencyCode reportingCurrency, CancellationToken ct)
    {
        List<JournalLine> lines = [.. journal.Lines];
        decimal? rate = null;
        List<(JournalLine Line, decimal Amount, bool Computed)> amounts = [];
        for (int i = 0; i < lines.Count; i++)
        {
            JournalLine line = lines[i];
            decimal baseAmount = line.BaseDebit != 0m ? line.BaseDebit : line.BaseCredit;
            decimal transaction = line.TransactionDebit != 0m ? line.TransactionDebit : line.TransactionCredit;
            if (i < request.Lines.Count && request.Lines[i].ReportingAmount is decimal given)
            {
                amounts.Add((line, given, false));
            }
            else if (reportingCurrency == baseCurrency)
            {
                amounts.Add((line, baseAmount, false));
            }
            else if (line.TransactionCurrency == reportingCurrency)
            {
                amounts.Add((line, transaction, false));
            }
            else
            {
                if (rate is null)
                {
                    Result<decimal> found = await LookupRateAsync(request, baseCurrency, reportingCurrency, ct).ConfigureAwait(false);
                    if (found.IsFailure)
                    {
                        return Error.Rule("Posting.NoReportingRate",
                            $"No {baseCurrency.Value}→{reportingCurrency.Value} rate on {request.PostingDate:yyyy-MM-dd} for the reporting currency: capture the day's rate first.");
                    }

                    rate = found.Value;
                }

                amounts.Add((line, decimal.Round(baseAmount * rate.Value, Money.StorageScale, MidpointRounding.ToEven), true));
            }
        }

        decimal Signed((JournalLine Line, decimal Amount, bool Computed) a) => a.Line.BaseDebit != 0m || a.Line.TransactionDebit != 0m ? a.Amount : -a.Amount;
        decimal difference = amounts.Sum(Signed);
        if (difference != 0m && amounts.Where(a => a.Computed).OrderByDescending(a => a.Amount).FirstOrDefault() is { Line: not null } largest)
        {
            int at = amounts.IndexOf(largest);
            bool debit = largest.Line.BaseDebit != 0m || largest.Line.TransactionDebit != 0m;
            amounts[at] = (largest.Line, largest.Amount - (debit ? difference : -difference), true);
        }

        foreach ((JournalLine line, decimal amount, bool _) in amounts)
        {
            bool debit = line.BaseDebit != 0m || line.TransactionDebit != 0m;
            line.SetReportingAmounts(debit ? amount : 0m, debit ? 0m : amount);
        }

        return null;
    }

    private async Task<Result<(FiscalYear, FiscalPeriod)>> ResolvePeriodAsync(long companyId, string moduleCode, DateOnly date, bool allowFuture, CancellationToken ct,
        bool toAdjustment = false, long? actorUserId = null)
    {
        List<FiscalYear> years = await _db.FiscalYears
            .Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses)
            .Where(y => y.CompanyId == companyId && y.StartDate <= date && date <= y.EndDate)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (FiscalYear year in years)
        {
            // The adjustment period is reached only on request, and only on the year's last day.
            FiscalPeriod? period = toAdjustment ? year.Periods.FirstOrDefault(p => p.IsAdjustment && p.Contains(date)) : year.Periods.FirstOrDefault(p => !p.IsAdjustment && p.Contains(date));
            if (period is null)
            {
                if (toAdjustment)
                {
                    return Error.Rule("Posting.NoAdjustmentPeriod", $"Fiscal year {year.Code} has no adjustment period on {date:yyyy-MM-dd} (it is on the year's last day, {year.EndDate:yyyy-MM-dd}).");
                }

                continue;
            }

            PeriodStatus status = period.StatusFor(moduleCode);
            if (status == PeriodStatus.Open || (status == PeriodStatus.Future && allowFuture))
            {
                return (year, period);
            }

            // A (soft-)closed period still takes adjustments from someone permitted to make them (brief §3.4: "soft close… allow adjustments with
            // permission, then hard close"); a permanently closed one takes nothing.
            // A year the year-end routine has closed takes no adjustments: they would miss the closing entries (reverse the year-end instead).
            if (status == PeriodStatus.Closed && !year.IsClosed)
            {
                if (actorUserId is long actor && _permissions is not null
                    && (await _permissions.ResolveAsync(actor, ct).ConfigureAwait(false)).Has("GeneralLedger.PeriodAdjustment", Core.Security.OperationFlags.Post))
                {
                    return (year, period);
                }

                return Error.Rule("Posting.PeriodNotOpen",
                    $"The period containing {date:yyyy-MM-dd} is closed for module {moduleCode}; only a user permitted to post adjustments into closed periods can post to it.");
            }

            return Error.Rule("Posting.PeriodNotOpen",
                $"The period containing {date:yyyy-MM-dd} is {status} for module {moduleCode}; posting is not allowed.");
        }

        return Error.Rule("Posting.NoPeriod", $"No fiscal period is defined for {date:yyyy-MM-dd} in company {companyId}.");
    }

    // The company's exchange-rate policy for the document type (else the source module) picks the rate type; the daily rate is the fallback (brief §3.3).
    private async Task<Result<decimal>> LookupRateAsync(JournalRequest request, CurrencyCode from, CurrencyCode to, CancellationToken ct)
    {
        DateOnly date = request.PostingDate;
        decimal? rate = await ExchangeRateResolver.PolicyRateAsync(_db, request.CompanyId, from, to, request.DocumentType, request.SourceModule, date, ct).ConfigureAwait(false);
        if (rate is null)
        {
            return Error.Rule("Posting.NoExchangeRate", $"No daily exchange rate from {from} to {to} (direct, inverse or through another currency) on or before {date:yyyy-MM-dd}.");
        }

        return rate.Value;
    }

    private async Task UpdatePeriodBalancesAsync(long companyId, long fiscalYearId, long fiscalPeriodId, JournalEntry journal, CancellationToken ct)
    {
        // Aggregate by account key so a journal that touches the same account on more than one line
        // (e.g. a POS sale with a cash tender and cash change) updates a single period-balance row.
        Dictionary<string, GlPeriodBalance> touched = new(StringComparer.Ordinal);

        foreach (JournalLine line in journal.Lines)
        {
            AccountKey key = line.Key;
            string mapKey = $"{key.AccountCode}{key.CostCentre}{key.Department}";

            if (!touched.TryGetValue(mapKey, out GlPeriodBalance? balance))
            {
                ForgetIfUnchanged(_db.PeriodBalances.Local.FirstOrDefault(
                    b => b.CompanyId == companyId && b.FiscalPeriodId == fiscalPeriodId &&
                         b.AccountCode == key.AccountCode && b.CostCentre == key.CostCentre && b.Department == key.Department));
                balance = await _db.PeriodBalances.FirstOrDefaultAsync(
                    b => b.CompanyId == companyId && b.FiscalPeriodId == fiscalPeriodId &&
                         b.AccountCode == key.AccountCode && b.CostCentre == key.CostCentre && b.Department == key.Department,
                    ct).ConfigureAwait(false);

                if (balance is null)
                {
                    balance = new GlPeriodBalance(companyId, fiscalYearId, fiscalPeriodId, key);
                    _db.PeriodBalances.Add(balance);
                }

                touched[mapKey] = balance;
            }

            balance.AddMovement(line.BaseDebit, line.BaseCredit, line.ReportingDebit, line.ReportingCredit);
        }
    }

    private async Task<string> AllocateJournalNumberAsync(long companyId, int year, CancellationToken ct)
        => (await JournalSequenceAsync(companyId, ct).ConfigureAwait(false)).Allocate(year);

    // The company's journal counter, locked for the rest of the unit of work and read afresh.
    private async Task<NumberSequence> JournalSequenceAsync(long companyId, CancellationToken ct)
    {
        // Inside a unit of work the company's journal counter is locked until it commits, so postings made at the same moment (a billing run
        // posting several bills at once, two cashiers) queue for the next number instead of colliding and retrying.
        if (_db.Database.CurrentTransaction is not null)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM sys.number_sequence WHERE company_id = {companyId} AND branch_id IS NULL AND document_type = {JournalDocumentType} FOR UPDATE", ct).ConfigureAwait(false);
        }

        ForgetIfUnchanged(_db.NumberSequences.Local.FirstOrDefault(s => s.CompanyId == companyId && s.BranchId == null && s.DocumentType == JournalDocumentType));
        NumberSequence? sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.BranchId == null && s.DocumentType == JournalDocumentType, ct)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            sequence = new NumberSequence(companyId, JournalDocumentType, "JNL-", 6, resetYearly: true);
            _db.NumberSequences.Add(sequence);
        }

        return sequence;
    }

    /// <summary>
    /// Stops tracking an unchanged copy of a counter row so the next query reads it from the database. A query returns
    /// an already-tracked instance as it is in memory; after a caller rolls back a transaction that included an earlier
    /// posting, the sequence counter (with its per-year counters) and the period balances would still hold the
    /// rolled-back values and concurrency token, so the next posting would skip numbers, double-count movements or fail.
    /// Rows with pending changes of this unit of work are left alone.
    /// </summary>
    private void ForgetIfUnchanged(object? entity)
    {
        if (entity is null || _db.Entry(entity).State != EntityState.Unchanged)
        {
            return;
        }

        if (entity is NumberSequence sequence)
        {
            foreach (NumberSequenceYear year in sequence.Years)
            {
                _db.Entry(year).State = EntityState.Detached;
            }
        }

        _db.Entry(entity).State = EntityState.Detached;
    }
}
