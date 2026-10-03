using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Manages recurring-journal templates and posts them through the central posting engine.</summary>
public sealed class RecurringJournalService : IRecurringJournalService
{
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
    public async Task<Result<long>> CreateAsync(long companyId, string code, string description, string sourceModule, IReadOnlyList<RecurringJournalLineInput> lines, string? narrative = null, CancellationToken ct = default)
    {
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

        _db.Set<RecurringJournal>().Add(template);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return template.Id;
    }

    /// <inheritdoc />
    public Task<Result<string>> PostAsync(long companyId, long recurringJournalId, DateOnly date, string? documentNumber = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostCoreAsync(companyId, recurringJournalId, date, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<string>> PostCoreAsync(long companyId, long recurringJournalId, DateOnly date, string? documentNumber, CancellationToken ct)
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

        // Recorded before posting so the engine's single SaveChanges persists the journal and the mark together.
        template.RecordPosted(date);

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = template.SourceModule,
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "Recurring",
            DocumentNumber = documentNumber ?? template.Code,
            Narrative = template.Narrative ?? template.Description,
            Lines = template.Lines.Select(l => new JournalLineRequest
            {
                AccountCode = l.AccountCode,
                CostCentre = l.CostCentre,
                Department = l.Department,
                Side = l.IsDebit ? JournalSide.Debit : JournalSide.Credit,
                TransactionAmount = l.Amount,
            }).ToList(),
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            // Nothing was saved; discard the mark so the template is not left looking posted.
            await _db.Entry(template).ReloadAsync(ct).ConfigureAwait(false);
            return posted.Error;
        }

        return posted.Value.JournalNumber;
    }

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

            Result<string> posted = await PostAsync(companyId, template.Id, date, ct: ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            results.Add(new RecurringRunLine(template.Code, posted.Value));
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
