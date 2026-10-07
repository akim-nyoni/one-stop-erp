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
    public PostingEngine(One4AllDbContext db, IServiceScopeFactory? scopes = null, Application.Abstractions.Licensing.ILicenceService? licence = null)
    {
        _db = db;
        _scopes = scopes;
        _licence = licence;
    }

    private readonly Application.Abstractions.Licensing.ILicenceService? _licence;

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

        // Resolve the open period for the posting module.
        Result<(FiscalYear year, FiscalPeriod period)> periodResult =
            await ResolvePeriodAsync(request.CompanyId, request.SourceModule, request.PostingDate, company.AllowFuturePeriodPosting, ct, request.ToAdjustmentPeriod)
                .ConfigureAwait(false);
        if (periodResult.IsFailure)
        {
            return periodResult.Error;
        }

        (FiscalYear fiscalYear, FiscalPeriod fiscalPeriod) = periodResult.Value;

        // Build and validate the lines.
        JournalEntry journal = new(request.CompanyId, request.SourceModule, request.PostingDate, baseCurrency, request.TransactionCurrency)
        {
            IsManual = request.EnforceManualJournalRules,
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

        // Rule 1 (continued): Dr = Cr per transaction currency.
        foreach ((string currency, (decimal debit, decimal credit)) in perCurrency)
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

        // A bank account reconciled and signed off to a date takes no more entries on or before it.
        List<string> lineAccounts = [.. journal.Lines.Select(l => l.AccountCode).Distinct()];
        string? lockedBank = await _db.Set<Core.Banking.BankAccount>()
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
        bool held = request.Deferred || request.SubmittedByUserId is not null;
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

        return new PostingReceipt(journal.Id, journalNumber, request.PostingDate, totalBaseDebit, held);
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

        return posted.Value;
    }

    private async Task<Result<JournalLine>> BuildLineAsync(JournalRequest request, JournalLineRequest lineRequest, CurrencyCode baseCurrency, string? defaultFund, CancellationToken ct)
    {
        AccountKey key = lineRequest.ToKey();
        LedgerAccount? account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.CompanyId == request.CompanyId && a.AccountCode == key.AccountCode, ct)
            .ConfigureAwait(false);

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

        CurrencyCode lineCurrency = request.TransactionCurrency;
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
            Result<decimal> rateResult = await LookupRateAsync(lineCurrency, baseCurrency, request.PostingDate, ct).ConfigureAwait(false);
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
            decimal appropriated = await _db.Budgets
                .Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.IsActive
                            && (b.Type == Core.Budgeting.BudgetType.OriginalApproved || b.Type == Core.Budgeting.BudgetType.SupplementaryRevised)
                            && (b.Department == null || b.Department == department))
                .SelectMany(b => b.Lines)
                .Where(l => l.AccountCode == line.AccountCode && (l.CostCentre == null || l.CostCentre == costCentre))
                .SumAsync(l => (decimal?)l.Amount, ct).ConfigureAwait(false) ?? 0m;
            if (appropriated <= 0m)
            {
                return (Error.Rule("Posting.NoAppropriation",
                    $"Account '{line.AccountCode}' has no appropriation in this year's active budget; fund control does not allow expenditure against it."), line.AccountCode);
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
                    Result<decimal> found = await LookupRateAsync(baseCurrency, reportingCurrency, request.PostingDate, ct).ConfigureAwait(false);
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
        bool toAdjustment = false)
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

            return Error.Rule("Posting.PeriodNotOpen",
                $"The period containing {date:yyyy-MM-dd} is {status} for module {moduleCode}; posting is not allowed.");
        }

        return Error.Rule("Posting.NoPeriod", $"No fiscal period is defined for {date:yyyy-MM-dd} in company {companyId}.");
    }

    private async Task<Result<decimal>> LookupRateAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken ct)
    {
        decimal? rate = await ExchangeRateResolver.FindAsync(_db, from, to, ExchangeRateType.Daily, date, ct).ConfigureAwait(false);
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
    {
        ForgetIfUnchanged(_db.NumberSequences.Local.FirstOrDefault(s => s.CompanyId == companyId && s.BranchId == null && s.DocumentType == JournalDocumentType));
        NumberSequence? sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.BranchId == null && s.DocumentType == JournalDocumentType, ct)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            sequence = new NumberSequence(companyId, JournalDocumentType, "JNL-", 6, resetYearly: true);
            _db.NumberSequences.Add(sequence);
        }

        return sequence.Allocate(year);
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
