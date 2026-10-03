using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Recognises deferred income as it is earned (see <see cref="IDeferredIncomeService"/>).</summary>
public sealed class DeferredIncomeService : IDeferredIncomeService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public DeferredIncomeService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<DeferredIncomeRunResult>> RecogniseAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecogniseCoreAsync(companyId, asAtDate, ct), ct);

    // One unit of work: the recognition journal and the schedules' recognised amounts.
    private async Task<Result<DeferredIncomeRunResult>> RecogniseCoreAsync(long companyId, DateOnly asAtDate, CancellationToken ct)
    {
        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Deferred.CompanyNotFound", $"Company {companyId} not found.");
        }

        List<DeferredIncomeSchedule> schedules = await _db.Set<DeferredIncomeSchedule>()
            .Where(s => s.CompanyId == companyId && s.StartDate <= asAtDate)
            .OrderBy(s => s.StartDate).ThenBy(s => s.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        List<(DeferredIncomeSchedule Schedule, decimal Delta)> due = [.. schedules
            .Select(s => (Schedule: s, Delta: s.EarnedBy(asAtDate) - s.RecognisedAmount))
            .Where(x => x.Delta != 0m)];
        if (due.Count == 0)
        {
            return new DeferredIncomeRunResult(asAtDate, [], null);
        }

        // One line pair per deferred / revenue account pair: Dr deferred income, Cr revenue (reversed if negative).
        List<JournalLineRequest> lines = [];
        foreach (IGrouping<(string Deferred, string Revenue), (DeferredIncomeSchedule Schedule, decimal Delta)> pair in due
            .GroupBy(x => (x.Schedule.DeferredAccountCode, x.Schedule.RevenueAccountCode)))
        {
            decimal net = pair.Sum(x => x.Delta);
            if (net == 0m)
            {
                continue;
            }

            bool earn = net > 0m;
            lines.Add(new JournalLineRequest { AccountCode = pair.Key.Deferred, Side = earn ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(net) });
            lines.Add(new JournalLineRequest { AccountCode = pair.Key.Revenue, Side = earn ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(net) });
        }

        string? journalNumber = null;
        if (lines.Count > 0)
        {
            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = asAtDate,
                TransactionCurrency = company.BaseCurrency,
                EnforceManualJournalRules = false,
                DocumentType = "DeferredIncomeRecognition",
                DocumentNumber = $"DEFINC-{asAtDate:yyyyMMdd}",
                Narrative = $"Deferred income earned to {asAtDate:yyyy-MM-dd}",
                Lines = lines,
            }, ct).ConfigureAwait(false);

            if (posted.IsFailure)
            {
                return posted.Error;
            }

            journalNumber = posted.Value.JournalNumber;
        }

        List<DeferredIncomeLine> result = [];
        foreach ((DeferredIncomeSchedule schedule, decimal delta) in due)
        {
            schedule.Recognise(delta);
            result.Add(new DeferredIncomeLine(schedule.Reference, schedule.Description, schedule.Amount, schedule.RecognisedAmount, delta));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DeferredIncomeRunResult(asAtDate, result, journalNumber);
    }
}
