using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Manages recurring-journal templates and posts them through the central posting engine on their schedule: monthly,
/// quarterly, half-yearly or yearly, between a start month and an end date or for a number of times, in a currency,
/// reversed on the first day of the next month when the template is an accrual.
/// </summary>
public sealed class RecurringJournalService : IRecurringJournalService
{
    private static readonly int[] Intervals = [1, 3, 6, 12];
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public RecurringJournalService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<long>> CreateAsync(long companyId, string code, string description, string sourceModule, IReadOnlyList<RecurringJournalLineInput> lines, string? narrative = null, CancellationToken ct = default)
        => CreateAsync(companyId, code, description, sourceModule, lines, new RecurringSchedule(), narrative, ct);

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, string code, string description, string sourceModule, IReadOnlyList<RecurringJournalLineInput> lines, RecurringSchedule schedule, string? narrative = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(schedule);
        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation("Gl.RecurringNoCode", "A recurring journal needs a code.");
        }

        if (lines.Count == 0)
        {
            return Error.Validation("Gl.RecurringNoLines", "A recurring journal needs at least one line.");
        }

        if (lines.Any(l => l.Amount <= 0m || string.IsNullOrWhiteSpace(l.AccountCode)))
        {
            return Error.Rule("Gl.RecurringInvalidLine", "Each line needs a positive amount and an account.");
        }

        Result scheduleCheck = await CheckScheduleAsync(schedule, ct).ConfigureAwait(false);
        if (scheduleCheck.IsFailure)
        {
            return scheduleCheck.Error;
        }

        string normalized = code.Trim().ToUpperInvariant();
        bool exists = await _db.Set<RecurringJournal>().AnyAsync(r => r.CompanyId == companyId && r.Code == normalized, ct).ConfigureAwait(false);
        if (exists)
        {
            return Error.Rule("Gl.RecurringDuplicate", $"A recurring journal with code {normalized} already exists.");
        }

        RecurringJournal template = new(companyId, code, description, sourceModule, narrative);
        foreach (RecurringJournalLineInput line in lines)
        {
            template.AddLine(line.AccountCode, line.IsDebit, line.Amount, line.CostCentre, line.Department);
        }

        if (!template.IsBalanced)
        {
            return Error.Rule("Gl.RecurringUnbalanced", $"The template does not balance (debits {template.TotalDebit:0.00} vs credits {template.TotalCredit:0.00}).");
        }

        template.Schedule(schedule.IntervalMonths, schedule.StartDate, schedule.EndDate, schedule.MaxOccurrences, schedule.AutoReverse, schedule.Currency);
        _db.Set<RecurringJournal>().Add(template);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return template.Id;
    }

    /// <inheritdoc />
    public async Task<Result> SetScheduleAsync(long companyId, long recurringJournalId, RecurringSchedule schedule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        RecurringJournal? template = await _db.Set<RecurringJournal>().FirstOrDefaultAsync(r => r.Id == recurringJournalId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Result.Failure(Error.NotFound("Gl.RecurringNotFound", $"Recurring journal {recurringJournalId} not found."));
        }

        Result scheduleCheck = await CheckScheduleAsync(schedule, ct).ConfigureAwait(false);
        if (scheduleCheck.IsFailure)
        {
            return scheduleCheck;
        }

        template.Schedule(schedule.IntervalMonths, schedule.StartDate, schedule.EndDate, schedule.MaxOccurrences, schedule.AutoReverse, schedule.Currency);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> CheckScheduleAsync(RecurringSchedule schedule, CancellationToken ct)
    {
        if (!Intervals.Contains(schedule.IntervalMonths))
        {
            return Result.Failure(Error.Validation("Gl.RecurringInterval", "A recurring journal posts monthly, quarterly, half-yearly or yearly (1, 3, 6 or 12 months)."));
        }

        if (schedule.StartDate is DateOnly start && schedule.EndDate is DateOnly end && end < start)
        {
            return Result.Failure(Error.Validation("Gl.RecurringDates", "The end date is before the start."));
        }

        if (schedule.MaxOccurrences is <= 0)
        {
            return Result.Failure(Error.Validation("Gl.RecurringTimes", "The number of times must be positive."));
        }

        if (schedule.Currency is CurrencyCode currency && !await _db.Currencies.AnyAsync(c => c.Code == currency, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Gl.RecurringCurrency", $"Currency {currency.Value} is not set up."));
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<string>> PostAsync(long companyId, long recurringJournalId, DateOnly date, string? documentNumber = null, CancellationToken ct = default)
        => _db.AtomicAsync<string>(async () =>
        {
            Result<(string Journal, string? Reversal)> posted = await PostCoreAsync(companyId, recurringJournalId, date, documentNumber, ct).ConfigureAwait(false);
            return posted.IsSuccess ? Result<string>.Success(posted.Value.Journal) : Result<string>.Failure(posted.Error);
        }, ct);

    // One unit of work: the posting, its reversal and the template's mark commit together or not at all.
    private async Task<Result<(string Journal, string? Reversal)>> PostCoreAsync(long companyId, long recurringJournalId, DateOnly date, string? documentNumber, CancellationToken ct)
    {
        RecurringJournal? template = await _db.Set<RecurringJournal>().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == recurringJournalId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound("Gl.RecurringNotFound", $"Recurring journal {recurringJournalId} not found.");
        }

        if (!template.IsActive)
        {
            return Error.Rule("Gl.RecurringInactive", $"Recurring journal {template.Code} is inactive.");
        }

        if (!template.IsBalanced)
        {
            return Error.Rule("Gl.RecurringUnbalanced", "The template does not balance.");
        }

        if (template.IsPostedFor(date))
        {
            return Error.Rule("Gl.RecurringAlreadyPosted",
                $"Recurring journal {template.Code} has already been posted for {template.LastPostedPeriod / 100}-{template.LastPostedPeriod % 100:00}; it cannot be posted again for {date:yyyy-MM} or an earlier month.");
        }

        if (template.NotDueReason(date) is string notDue)
        {
            return Error.Rule("Gl.RecurringNotDue", $"Recurring journal {template.Code} is not due on {date:yyyy-MM-dd}: {notDue}.");
        }

        // Recorded before posting so the engine's single SaveChanges persists the journal and the mark together.
        template.RecordPosted(date);

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        CurrencyCode currency = template.Currency ?? company.BaseCurrency;
        string document = documentNumber ?? template.Code;
        Result<PostingReceipt> posted = await _posting.PostAsync(Request(template, companyId, date, currency, "Recurring", document, template.Narrative ?? template.Description, reversed: false), ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            // Nothing was saved; discard the mark so the template is not left looking posted.
            await _db.Entry(template).ReloadAsync(ct).ConfigureAwait(false);
            return posted.Error;
        }

        if (!template.AutoReverse)
        {
            return (posted.Value.JournalNumber, null);
        }

        // An accrual template: the same lines the other way round on the first day of the following month, at the same rate so it clears exactly.
        DateOnly reversalDate = new DateOnly(date.Year, date.Month, 1).AddMonths(1);
        Result<PostingReceipt> reversal = await _posting.PostAsync(Request(template, companyId, reversalDate, currency, "RecurringReversal", $"{document}-REV",
            $"Reversal of {document} {date:yyyy-MM}", reversed: true), ct).ConfigureAwait(false);
        if (reversal.IsFailure)
        {
            await _db.Entry(template).ReloadAsync(ct).ConfigureAwait(false);
            return reversal.Error;
        }

        return (posted.Value.JournalNumber, reversal.Value.JournalNumber);
    }

    private static JournalRequest Request(RecurringJournal template, long companyId, DateOnly date, CurrencyCode currency, string documentType, string documentNumber, string narrative, bool reversed)
        => new()
        {
            CompanyId = companyId,
            SourceModule = template.SourceModule,
            PostingDate = date,
            TransactionCurrency = currency,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines = template.Lines.Select(l => new JournalLineRequest
            {
                AccountCode = l.AccountCode,
                CostCentre = l.CostCentre,
                Department = l.Department,
                Side = l.IsDebit != reversed ? JournalSide.Debit : JournalSide.Credit,
                TransactionAmount = l.Amount,
                Currency = currency,
            }).ToList(),
        };

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RecurringRunLine>>> PostAllActiveAsync(long companyId, DateOnly date, CancellationToken ct = default)
    {
        List<RecurringJournal> templates = await _db.Set<RecurringJournal>()
            .Where(r => r.CompanyId == companyId && r.IsActive)
            .OrderBy(r => r.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        List<RecurringRunLine> results = [];
        foreach (RecurringJournal template in templates)
        {
            if (template.IsPostedFor(date))
            {
                results.Add(new RecurringRunLine(template.Code, string.Empty, AlreadyPosted: true));
                continue;
            }

            if (template.NotDueReason(date) is string notDue)
            {
                results.Add(new RecurringRunLine(template.Code, string.Empty, Note: $"not due: {notDue}"));
                continue;
            }

            Result<(string Journal, string? Reversal)> posted = await _db.AtomicAsync(() => PostCoreAsync(companyId, template.Id, date, null, ct), ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            results.Add(new RecurringRunLine(template.Code, posted.Value.Journal, ReversalJournalNumber: posted.Value.Reversal));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<Result> SetActiveAsync(long companyId, long recurringJournalId, bool isActive, CancellationToken ct = default)
    {
        RecurringJournal? template = await _db.Set<RecurringJournal>().FirstOrDefaultAsync(r => r.Id == recurringJournalId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Result.Failure(Error.NotFound("Gl.RecurringNotFound", $"Recurring journal {recurringJournalId} not found."));
        }

        template.SetActive(isActive);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
