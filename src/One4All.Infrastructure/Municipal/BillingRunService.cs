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
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    public BillingRunService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar)
    {
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
        if (await _db.Set<BillingRun>().AnyAsync(r => r.CompanyId == proposal.CompanyId && r.ServiceType == proposal.ServiceType && r.Period == period && r.Status == BillingRunStatus.Proposed, ct).ConfigureAwait(false))
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
        List<long> propertyIds = await MunicipalCharges.PropertiesToBillAsync(_db, proposal.CompanyId, proposal.ServiceType, proposal.BillDate, run.Ward, ct).ConfigureAwait(false);
        Dictionary<long, string> numbers = propertyIds.ToDictionary(id => id, id => $"{prefix}-{id}-{proposal.BillDate:yyyyMM}");
        HashSet<string> alreadyRaised = await MunicipalCharges.AlreadyRaisedAsync(_db, proposal.CompanyId, numbers.Values, ct).ConfigureAwait(false);

        foreach (long propertyId in propertyIds)
        {
            string invoice = numbers[propertyId];
            Result<ComputedCharge> computed = await MunicipalCharges.ComputeAsync(_db, proposal.CompanyId, propertyId, proposal.ServiceType, 0m, proposal.BillDate, prorate: true, ct).ConfigureAwait(false);
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
                bill.Tariff.RevenueAccountCode, bill.Narrative, exceptions, message));
        }

        _db.Set<BillingRun>().Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Summary(run);
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
        => _db.AtomicAsync(() => ApproveCoreAsync(companyId, runNumber, userId, ct), ct);

    private async Task<Result<BillingRunSummary>> ApproveCoreAsync(long companyId, string runNumber, long userId, CancellationToken ct)
    {
        Result<BillingRun> found = await FindProposedAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        BillingRun run = found.Value;
        if (run.ProposedByUserId == userId)
        {
            return Error.Rule("Mun.SelfApproval", $"Run {runNumber} must be approved by someone other than who proposed it.");
        }

        List<BillingRunLine> toPost = [.. run.Lines.Where(l => l.Included).OrderBy(l => l.PropertyId)];
        HashSet<string> raisedSince = await MunicipalCharges.AlreadyRaisedAsync(_db, companyId, [.. toPost.Select(l => l.InvoiceNumber)], ct).ConfigureAwait(false);
        if (raisedSince.Count > 0)
        {
            return Error.Conflict("Mun.AlreadyBilled", $"Bills raised since the run was proposed: {string.Join(", ", raisedSince.Order())}. Cancel and run again.");
        }

        foreach (BillingRunLine line in toPost)
        {
            Result<TradingPostingResult> posted = await MunicipalCharges.PostAsync(_db, _ar, companyId, line.RatepayerCustomerId, line.InvoiceNumber, run.BillDate,
                line.Charge, 0m, line.RevenueAccountCode, null, line.Narrative, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        run.MarkPosted(userId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Summary(run);
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(long companyId, string runNumber, CancellationToken ct = default)
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
        return new BillingRunSummary(run.RunNumber, run.Status, run.Lines.Count, included.Count, included.Sum(l => l.Charge), run.Lines.Count(l => l.Exceptions != BillingExceptions.None));
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
