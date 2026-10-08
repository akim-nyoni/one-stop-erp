using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Reviewed billing runs: proposed, reviewed, approved and posted through the receivables ledger.</summary>
public sealed class BillingRunService : IBillingRunService
{
    private readonly Application.Abstractions.Integration.IWebhookService? _webhooks;
    private readonly One4AllDbContext _db;
    private readonly Lazy<Application.Abstractions.Workflow.IWorkflowService>? _workflow;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    /// <param name="webhooks">Publishes generated bills.</param>
    public BillingRunService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar, Application.Abstractions.Integration.IWebhookService? webhooks = null,
        Lazy<Application.Abstractions.Workflow.IWorkflowService>? workflow = null)
    {
        _workflow = workflow;
        _webhooks = webhooks;
        _db = db;
        _numbers = numbers;
        _ar = ar;
    }

    /// <inheritdoc />
    public Task<Result<BillingRunSummary>> ProposeAsync(BillingRunProposal proposal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return _db.AtomicAsync(() => ProposeCoreAsync(proposal, ct), ct);
    }

    private async Task<Result<BillingRunSummary>> ProposeCoreAsync(BillingRunProposal proposal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(proposal.InvoicePrefix) || proposal.VariancePercent < 0m)
        {
            return Error.Validation("Mun.RunRequest", "A billing run needs a bill-number prefix and a variance tolerance of zero or more.");
        }

        DateOnly period = new(proposal.BillDate.Year, proposal.BillDate.Month, 1);
        string? serviceCode = string.IsNullOrWhiteSpace(proposal.ServiceCode) ? null : proposal.ServiceCode.Trim().ToUpperInvariant();
        if (await _db.Set<BillingRun>().AnyAsync(r => r.CompanyId == proposal.CompanyId && r.ServiceType == proposal.ServiceType && r.ServiceCode == serviceCode && r.Period == period
            && r.Status == BillingRunStatus.Proposed, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Mun.RunPending", $"A {proposal.ServiceType} run for {period:yyyy-MM} is already awaiting approval.");
        }

        Result<string> number = await _numbers.AllocateAsync(proposal.CompanyId, "BR", proposal.BillDate, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        string prefix = proposal.InvoicePrefix.Trim();
        BillingRun run = new(proposal.CompanyId, number.Value, proposal.ServiceType, proposal.BillDate, string.IsNullOrWhiteSpace(proposal.Ward) ? null : proposal.Ward.Trim(), prefix, proposal.UserId);
        run.ServiceCode = serviceCode;
        List<long> propertyIds = await MunicipalCharges.PropertiesToBillAsync(_db, proposal.CompanyId, proposal.ServiceType, proposal.BillDate, run.Ward, ct, serviceCode).ConfigureAwait(false);
        Dictionary<long, string> numbers = propertyIds.ToDictionary(id => id, id => $"{prefix}-{id}-{proposal.BillDate:yyyyMM}");
        HashSet<string> alreadyRaised = await MunicipalCharges.AlreadyRaisedAsync(_db, proposal.CompanyId, numbers.Values, ct).ConfigureAwait(false);

        foreach (long propertyId in propertyIds)
        {
            string invoice = numbers[propertyId];
            Result<ComputedCharge> computed = await MunicipalCharges.ComputeAsync(_db, proposal.CompanyId, propertyId, proposal.ServiceType, 0m, proposal.BillDate, prorate: true, ct, serviceCode).ConfigureAwait(false);
            if (computed.IsFailure && computed.Error.Code == "Mun.NotDueThisCycle")
            {
                continue;
            }

            // A cycle charge carries its period (or instalment month): already raised for the period means nothing to bill this run.
            if (computed.IsSuccess && computed.Value.BillMonth is DateOnly billMonth)
            {
                invoice = $"{prefix}-{propertyId}-{billMonth:yyyyMM}";
                if ((await MunicipalCharges.AlreadyRaisedAsync(_db, proposal.CompanyId, [invoice], ct).ConfigureAwait(false)).Count > 0)
                {
                    continue;
                }
            }

            if (computed.IsFailure)
            {
                var property = await _db.Properties.AsNoTracking().Where(p => p.Id == propertyId).Select(p => new { p.StandNumber, p.RatepayerCustomerId }).FirstAsync(ct).ConfigureAwait(false);
                BillingExceptions kind = computed.Error.Code == "Mun.NothingToBill" ? BillingExceptions.NothingToBill : BillingExceptions.Failed;
                run.AddLine(new BillingRunLine(propertyId, property.StandNumber, property.RatepayerCustomerId, invoice, 0m, null, string.Empty, string.Empty,
                    kind | (alreadyRaised.Contains(invoice) ? BillingExceptions.AlreadyBilled : BillingExceptions.None), computed.Error.Message));
                continue;
            }

            ComputedCharge bill = computed.Value;
            decimal? previous = await PreviousChargeAsync(proposal.CompanyId, bill, proposal.BillDate, ct).ConfigureAwait(false);
            BillingExceptions exceptions = BillingExceptions.None;
            string? message = null;
            if (alreadyRaised.Contains(invoice))
            {
                exceptions |= BillingExceptions.AlreadyBilled;
                message = $"Bill {invoice} is already on the ledger.";
            }

            if (previous is decimal last && last > 0m && Math.Abs(bill.Charge - last) > last * proposal.VariancePercent / 100m)
            {
                exceptions |= BillingExceptions.LargeChange;
                message ??= $"{bill.Charge:0.00} against {last:0.00} last time.";
            }

            run.AddLine(new BillingRunLine(propertyId, bill.Property.StandNumber, bill.Property.RatepayerCustomerId, invoice, bill.Charge, previous,
                bill.Tariff.RevenueAccountCode, bill.Narrative, exceptions, message ?? bill.CycleNote));
        }

        _db.Set<BillingRun>().Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The company's billing-run workflow, when defined, governs the approval from here.
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, proposal.CompanyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.BillingRun, run.RunNumber,
            run.Lines.Where(l => l.Included).Sum(l => l.Charge), proposal.UserId, ct).ConfigureAwait(false);
        return governed.IsFailure ? governed.Error : Summary(run);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BillingRunLineView>>> LinesAsync(long companyId, string runNumber, bool exceptionsOnly, CancellationToken ct = default)
    {
        Result<BillingRun> found = await FindAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        List<BillingRunLineView> lines = [.. found.Value.Lines
            .Where(l => !exceptionsOnly || l.Exceptions != BillingExceptions.None)
            .OrderBy(l => l.PropertyId)
            .Select(l => new BillingRunLineView(l.PropertyId, l.StandNumber, l.InvoiceNumber, l.Charge, l.PreviousCharge, l.Exceptions, l.Message, l.Included))];
        return Result<IReadOnlyList<BillingRunLineView>>.Success(lines);
    }

    /// <inheritdoc />
    public async Task<Result<BillingRunSummary>> SetIncludedAsync(long companyId, string runNumber, long propertyId, bool included, CancellationToken ct = default)
    {
        Result<BillingRun> found = await FindProposedAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        BillingRunLine? line = found.Value.Lines.FirstOrDefault(l => l.PropertyId == propertyId);
        if (line is null)
        {
            return Error.NotFound("Mun.RunLineNotFound", $"Run {runNumber} has no bill for property {propertyId}.");
        }

        if (included && line.IsBlocked)
        {
            return Error.Rule("Mun.LineBlocked", $"The bill for stand {line.StandNumber} cannot be posted: {line.Exceptions}.");
        }

        line.SetIncluded(included);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Summary(found.Value);
    }

    /// <inheritdoc />
    public Task<Result<BillingRunSummary>> ApproveAsync(long companyId, string runNumber, long userId, CancellationToken ct = default)
        => ApproveAsync(companyId, runNumber, userId, new BillingPostOptions(), ct);

    /// <inheritdoc />
    public async Task<Result<BillingRunSummary>> ApproveAsync(long companyId, string runNumber, long userId, BillingPostOptions options, CancellationToken ct = default)
    {
        if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.BillingRun, runNumber, ct).ConfigureAwait(false))
        {
            return Workflow.WorkflowGate.Governed("Billing run", runNumber);
        }

        Result<BillingRun> approved = await AuthoriseAsync(companyId, runNumber, userId, ct).ConfigureAwait(false);
        return approved.IsFailure ? approved.Error : await PostChunksAsync(companyId, approved.Value.Id, options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Authorises a proposed run (also the end of its workflow): the checks, then the run marked Posting — its bills then post through
    /// <see cref="ResumeAsync"/> in resumable chunks.
    /// </summary>
    internal Task<Result<BillingRun>> AuthoriseAsync(long companyId, string runNumber, long userId, CancellationToken ct)
        => _db.AtomicAsync(async () =>
        {
            Result<BillingRun> found = await FindProposedAsync(companyId, runNumber, ct).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return found;
            }

            BillingRun run = found.Value;
            if (run.ProposedByUserId == userId)
            {
                return Error.Rule("Mun.SelfApproval", $"Run {runNumber} must be approved by someone other than who proposed it.");
            }

            List<string> invoices = [.. run.Lines.Where(l => l.Included).Select(l => l.InvoiceNumber)];
            HashSet<string> raisedSince = await MunicipalCharges.AlreadyRaisedAsync(_db, companyId, invoices, ct).ConfigureAwait(false);
            if (raisedSince.Count > 0)
            {
                return Error.Conflict("Mun.AlreadyBilled", $"Bills raised since the run was proposed: {string.Join(", ", raisedSince.Order().Take(20))}. Cancel and run again.");
            }

            run.StartPosting(userId);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<BillingRun>.Success(run);
        }, ct);

    /// <inheritdoc />
    public async Task<Result<BillingRunSummary>> ResumeAsync(long companyId, string runNumber, BillingPostOptions options, CancellationToken ct = default)
    {
        Result<BillingRun> found = await FindAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        return found.Value.Status != BillingRunStatus.Posting
            ? Error.Rule("Mun.NotPosting", $"Run {runNumber} is {found.Value.Status}; only a run part-way through posting resumes.")
            : await PostChunksAsync(companyId, found.Value.Id, options, ct).ConfigureAwait(false);
    }

    // Posts the run's remaining bills chunk by chunk, each bill on its own: a failure is recorded on its line and the rest carry on; the
    // change tracker is cleared after each chunk so memory stays flat however large the run.
    private async Task<Result<BillingRunSummary>> PostChunksAsync(long companyId, long runId, BillingPostOptions options, CancellationToken ct)
    {
        int chunkSize = Math.Clamp(options.ChunkSize, 1, 5_000);
        var header = await _db.Set<BillingRun>().AsNoTracking().Where(r => r.Id == runId).Select(r => new { r.RunNumber, r.BillDate }).SingleAsync(ct).ConfigureAwait(false);
        int total = await _db.Set<BillingRunLine>().CountAsync(l => l.BillingRunId == runId && l.Included, ct).ConfigureAwait(false);
        int handled = 0;
        while (options.MaxBills is not int cap || handled < cap)
        {
            ct.ThrowIfCancellationRequested();
            int take = options.MaxBills is int limit ? Math.Min(chunkSize, limit - handled) : chunkSize;
            List<BillingRunLine> chunk = await _db.Set<BillingRunLine>()
                .Where(l => l.BillingRunId == runId && l.Included && !l.IsPosted && (l.Exceptions & BillingExceptions.Failed) == 0)
                .OrderBy(l => l.PropertyId).Take(take).ToListAsync(ct).ConfigureAwait(false);
            if (chunk.Count == 0)
            {
                break;
            }

            // A bill already on the account (posted before an interruption) is marked, not posted again.
            Dictionary<string, long> existing = await _db.OpenItems.Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable
                    && chunk.Select(l => l.InvoiceNumber).Contains(i.DocumentNumber))
                .ToDictionaryAsync(i => i.DocumentNumber, i => i.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
            foreach (BillingRunLine line in chunk)
            {
                if (existing.TryGetValue(line.InvoiceNumber, out long already))
                {
                    line.MarkPosted(already);
                    continue;
                }

                Result<TradingPostingResult> posted;
                try
                {
                    posted = await MunicipalCharges.PostAsync(_db, _ar, companyId, line.RatepayerCustomerId, line.InvoiceNumber, header.BillDate,
                        line.Charge, 0m, line.RevenueAccountCode, null, line.Narrative, ct).ConfigureAwait(false);
                }
                catch (DbUpdateException ex)
                {
                    posted = Error.Failure("Mun.PostFailed", ex.InnerException?.Message ?? ex.Message);
                }

                if (posted.IsSuccess)
                {
                    line.MarkPosted(posted.Value.OpenItemId);
                }
                else
                {
                    line.MarkFailed(posted.Error.Message);
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            handled += chunk.Count;
            _db.ChangeTracker.Clear();
            if (options.Progress is not null)
            {
                (int done, int failed) = await CountsAsync(runId, ct).ConfigureAwait(false);
                options.Progress.Report(new BillingRunProgress(done, failed, total));
            }
        }

        BillingRun run = await _db.Set<BillingRun>().Include(r => r.Lines).SingleAsync(r => r.Id == runId, ct).ConfigureAwait(false);
        bool finished = !run.Lines.Any(l => l.Included && !l.IsPosted && (l.Exceptions & BillingExceptions.Failed) == 0);
        if (finished && run.Status == BillingRunStatus.Posting)
        {
            run.FinishPosting();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (_webhooks is not null)
            {
                List<BillingRunLine> posted = [.. run.Lines.Where(l => l.IsPosted)];
                await _webhooks.PublishAsync(companyId, Application.Integration.WebhookEvents.BillsGenerated, new
                {
                    RunNumber = header.RunNumber, header.BillDate, Bills = posted.Count, Total = posted.Sum(l => l.Charge), Invoices = posted.Select(l => l.InvoiceNumber),
                }, ct).ConfigureAwait(false);
            }
        }

        return Summary(run);
    }

    private async Task<(int Posted, int Failed)> CountsAsync(long runId, CancellationToken ct)
    {
        var counts = await _db.Set<BillingRunLine>().AsNoTracking().Where(l => l.BillingRunId == runId && l.Included)
            .GroupBy(_ => 1).Select(g => new { Posted = g.Count(l => l.IsPosted), Failed = g.Count(l => (l.Exceptions & BillingExceptions.Failed) != 0) })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return (counts?.Posted ?? 0, counts?.Failed ?? 0);
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(long companyId, string runNumber, CancellationToken ct = default)
    {
        if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.BillingRun, runNumber, ct).ConfigureAwait(false))
        {
            return Result.Failure(Workflow.WorkflowGate.Governed("Billing run", runNumber));
        }

        return await CancelFromWorkflowAsync(companyId, runNumber, ct).ConfigureAwait(false);
    }

    /// <summary>Cancels a proposed run (also a rejected workflow's end).</summary>
    internal async Task<Result> CancelFromWorkflowAsync(long companyId, string runNumber, CancellationToken ct)
    {
        Result<BillingRun> found = await FindProposedAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.Cancel();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static BillingRunSummary Summary(BillingRun run)
    {
        List<BillingRunLine> included = [.. run.Lines.Where(l => l.Included)];
        return new BillingRunSummary(run.RunNumber, run.Status, run.Lines.Count, included.Count, included.Sum(l => l.Charge), run.Lines.Count(l => l.Exceptions != BillingExceptions.None),
            included.Count(l => l.IsPosted), included.Count(l => (l.Exceptions & BillingExceptions.Failed) != 0));
    }

    // The last bill of this tariff for the stand before the run's month, whichever version it was raised under.
    private async Task<decimal?> PreviousChargeAsync(long companyId, ComputedCharge bill, DateOnly billDate, CancellationToken ct)
    {
        DateOnly monthStart = new(billDate.Year, billDate.Month, 1);
        List<Tariff> versions = await TariffVersions.LoadAsync(_db, bill.Tariff, ct).ConfigureAwait(false);
        List<string> narratives = [.. versions.Append(bill.Tariff).Select(t => ValuationHistory.BillNarrative(t.Name, bill.Property.StandNumber)).Distinct()];
        return await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == bill.Property.RatepayerCustomerId
                && i.ItemType == OpenItemType.Invoice && narratives.Contains(i.Narrative!) && i.DocumentDate < monthStart)
            .OrderByDescending(i => i.DocumentDate).ThenByDescending(i => i.Id)
            .Select(i => (decimal?)i.OriginalBase)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private async Task<Result<BillingRun>> FindAsync(long companyId, string runNumber, CancellationToken ct)
    {
        BillingRun? run = await _db.Set<BillingRun>().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RunNumber == runNumber, ct).ConfigureAwait(false);
        if (run is null)
        {
            return Error.NotFound("Mun.RunNotFound", $"Billing run {runNumber} not found.");
        }

        return run;
    }

    private async Task<Result<BillingRun>> FindProposedAsync(long companyId, string runNumber, CancellationToken ct)
    {
        Result<BillingRun> found = await FindAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsSuccess && found.Value.Status != BillingRunStatus.Proposed)
        {
            return Error.Rule("Mun.RunNotProposed", $"Billing run {runNumber} is {found.Value.Status} and can no longer change.");
        }

        return found;
    }
}

/// <summary>
/// Applies the end of a billing run's workflow: approved → authorised (its bills then post through the resumable chunked posting);
/// rejected or returned → cancelled.
/// </summary>
internal sealed class BillingRunWorkflowHandler : Application.Abstractions.Workflow.IWorkflowDocumentHandler
{
    private readonly BillingRunService _runs;

    public BillingRunWorkflowHandler(BillingRunService runs) => _runs = runs;

    public string DocumentType => Application.Abstractions.Workflow.WorkflowDocumentTypes.BillingRun;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        if (outcome != Core.Workflow.WorkflowStatus.Approved)
        {
            Result cancelled = await _runs.CancelFromWorkflowAsync(companyId, documentReference, ct).ConfigureAwait(false);
            return cancelled.IsFailure && cancelled.Error.Code == "Mun.RunNotFound" ? Result.Success() : cancelled;
        }

        Result<Core.Municipal.BillingRun> authorised = await _runs.AuthoriseAsync(companyId, documentReference, decidedByUserId, ct).ConfigureAwait(false);
        return authorised.IsFailure && authorised.Error.Code != "Mun.RunNotFound" ? Result.Failure(authorised.Error) : Result.Success();
    }
}
