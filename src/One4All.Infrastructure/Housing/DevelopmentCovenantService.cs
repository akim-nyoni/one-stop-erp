using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Development covenants: milestones, paid extensions, breaches with penalty levies and repossession notices.</summary>
public sealed class DevelopmentCovenantService : IDevelopmentCovenantService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    public DevelopmentCovenantService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result<string>> CreateAsync(long companyId, long standId, long holderCustomerId, DateOnly startDate, int years, CancellationToken ct = default)
    {
        if (years < 1)
        {
            return Error.Validation("Hse.CovenantYears", "A covenant allows at least a year to build.");
        }

        if (!await _db.HousingStands.AnyAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == holderCustomerId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.HolderNotFound", $"Customer {holderCustomerId} not found.");
        }

        if (await _db.Set<DevelopmentCovenant>().AnyAsync(c => c.StandId == standId && c.Status != CovenantStatus.Complied, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.CovenantOpen", "The stand already carries a covenant not yet complied with.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "COV", startDate, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<DevelopmentCovenant>().Add(new DevelopmentCovenant(companyId, number.Value, standId, holderCustomerId, startDate, startDate.AddYears(years).AddDays(-1)));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result> RecordMilestoneAsync(long companyId, string covenantNumber, CovenantMilestone milestone, DateOnly date, string? reference, CancellationToken ct = default)
    {
        Result<DevelopmentCovenant> found = await FindAsync(companyId, covenantNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        DevelopmentCovenant covenant = found.Value;
        if (covenant.Status == CovenantStatus.Complied)
        {
            return Result.Failure(Error.Rule("Hse.CovenantComplied", $"Covenant {covenantNumber} has been complied with."));
        }

        int next = covenant.Latest is CovenantMilestone latest ? (int)latest + 1 : (int)CovenantMilestone.PlanSubmitted;
        if ((int)milestone != next)
        {
            return Result.Failure(Error.Rule("Hse.MilestoneOrder", $"The next milestone is {(CovenantMilestone)next}, not {milestone}."));
        }

        DateOnly floor = covenant.Steps.Count == 0 ? covenant.StartDate : covenant.Steps.Max(s => s.Date);
        if (date < floor)
        {
            return Result.Failure(Error.Validation("Hse.MilestoneDate", $"{milestone} cannot be dated before {floor:yyyy-MM-dd}."));
        }

        covenant.Reach(new CovenantStep(milestone, date, string.IsNullOrWhiteSpace(reference) ? null : reference.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<DateOnly>> GrantExtensionAsync(long companyId, string covenantNumber, int months, DateOnly date, decimal fee, string? feeAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ExtendCoreAsync(companyId, covenantNumber, months, date, fee, feeAccountCode, ct), ct);

    private async Task<Result<DateOnly>> ExtendCoreAsync(long companyId, string covenantNumber, int months, DateOnly date, decimal fee, string? feeAccountCode, CancellationToken ct)
    {
        if (months < 1 || fee < 0m || (fee > 0m && string.IsNullOrWhiteSpace(feeAccountCode)))
        {
            return Error.Validation("Hse.ExtensionTerms", "An extension needs at least a month, a fee of zero or more and a fee account when there is a fee.");
        }

        Result<DevelopmentCovenant> found = await FindAsync(companyId, covenantNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        DevelopmentCovenant covenant = found.Value;
        if (covenant.Status is CovenantStatus.Complied or CovenantStatus.NoticeIssued)
        {
            return Error.Rule("Hse.CovenantClosed", $"Covenant {covenantNumber} is {covenant.Status} and cannot be extended.");
        }

        if (fee > 0m)
        {
            Result<TradingPostingResult> invoiced = await InvoiceAsync(covenant, $"{covenant.CovenantNumber}-X{covenant.Extensions + 1}", date, fee, feeAccountCode!,
                $"Extension {covenant.Extensions + 1} of development covenant {covenant.CovenantNumber}", ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }
        }

        covenant.Extend(covenant.Deadline.AddMonths(months));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return covenant.Deadline;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> RunBreachesAsync(long companyId, DateOnly asAtDate, decimal penaltyLevy, string levyAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => BreachCoreAsync(companyId, asAtDate, penaltyLevy, levyAccountCode, ct), ct);

    private async Task<Result<IReadOnlyList<string>>> BreachCoreAsync(long companyId, DateOnly asAtDate, decimal penaltyLevy, string levyAccountCode, CancellationToken ct)
    {
        if (penaltyLevy < 0m || (penaltyLevy > 0m && string.IsNullOrWhiteSpace(levyAccountCode)))
        {
            return Error.Validation("Hse.LevyTerms", "The penalty levy cannot be negative and needs a revenue account.");
        }

        List<DevelopmentCovenant> overdue = await _db.Set<DevelopmentCovenant>()
            .Where(c => c.CompanyId == companyId && c.Status == CovenantStatus.Open && c.Deadline < asAtDate)
            .OrderBy(c => c.CovenantNumber).ToListAsync(ct).ConfigureAwait(false);
        foreach (DevelopmentCovenant covenant in overdue)
        {
            // One levy per breach: the document number carries the extension count, so a breach after an extension levies again.
            string document = $"{covenant.CovenantNumber}-PEN{covenant.Extensions}";
            bool levied = await _db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.DocumentNumber == document, ct).ConfigureAwait(false);
            if (penaltyLevy > 0m && !levied)
            {
                Result<TradingPostingResult> invoiced = await InvoiceAsync(covenant, document, asAtDate, penaltyLevy, levyAccountCode,
                    $"Penalty levy: development covenant {covenant.CovenantNumber} breached (deadline {covenant.Deadline:yyyy-MM-dd})", ct).ConfigureAwait(false);
                if (invoiced.IsFailure)
                {
                    return invoiced.Error;
                }
            }

            covenant.Breach(asAtDate);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<string>>.Success([.. overdue.Select(c => c.CovenantNumber)]);
    }

    /// <inheritdoc />
    public async Task<Result> IssueNoticeAsync(long companyId, string covenantNumber, DateOnly date, CancellationToken ct = default)
    {
        Result<DevelopmentCovenant> found = await FindAsync(companyId, covenantNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Status != CovenantStatus.Breached)
        {
            return Result.Failure(Error.Rule("Hse.NotBreached", $"Covenant {covenantNumber} is {found.Value.Status}; a notice follows a breach."));
        }

        found.Value.IssueNotice(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CovenantEntry>> RegisterAsync(long companyId, CancellationToken ct = default)
    {
        List<DevelopmentCovenant> covenants = await _db.Set<DevelopmentCovenant>().AsNoTracking().Include(c => c.Steps)
            .Where(c => c.CompanyId == companyId).OrderBy(c => c.CovenantNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> standIds = [.. covenants.Select(c => c.StandId)];
        Dictionary<long, string> stands = await _db.HousingStands.AsNoTracking().Where(s => standIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.StandNumber, ct).ConfigureAwait(false);
        return [.. covenants.Select(c => new CovenantEntry(c.CovenantNumber, stands[c.StandId], c.Deadline, c.Latest, c.Extensions, c.Status))];
    }

    private async Task<Result<TradingPostingResult>> InvoiceAsync(DevelopmentCovenant covenant, string document, DateOnly date, decimal amount, string account, string narrative, CancellationToken ct)
    {
        Customer holder = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == covenant.HolderCustomerId, ct).ConfigureAwait(false);
        return await _ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = covenant.CompanyId, CustomerId = holder.Id, DocumentNumber = document, DocumentDate = date, Currency = holder.Currency,
            NetAmount = amount, SalesAccountCode = account.Trim(), Narrative = narrative,
        }, ct).ConfigureAwait(false);
    }

    private async Task<Result<DevelopmentCovenant>> FindAsync(long companyId, string covenantNumber, CancellationToken ct)
    {
        string number = (covenantNumber ?? string.Empty).Trim();
        DevelopmentCovenant? covenant = await _db.Set<DevelopmentCovenant>().Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CovenantNumber == number, ct).ConfigureAwait(false);
        if (covenant is null)
        {
            return Error.NotFound("Hse.CovenantNotFound", $"Covenant {number} not found.");
        }

        return covenant;
    }
}
