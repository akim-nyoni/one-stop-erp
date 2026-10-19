using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Posts auto-reversing accrual and prepayment journals through the central posting engine, reusing
/// <see cref="IPostingEngine.ReverseAsync"/> for the next-period reversal so the pair is linked and nets to
/// zero exactly like any other reversal.
/// </summary>
public sealed class AccrualService : IAccrualService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public AccrualService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreatePrepaymentScheduleAsync(NewPrepaymentSchedule schedule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (string.IsNullOrWhiteSpace(schedule.Reference) || string.IsNullOrWhiteSpace(schedule.PrepaymentAccountCode) || string.IsNullOrWhiteSpace(schedule.ExpenseAccountCode))
        {
            return Error.Validation("GL.PrepaymentSchedule", "A prepayment schedule needs a reference, the prepayment account and the expense account.");
        }

        if (schedule.Total <= 0m || schedule.Months is < 1 or > 120)
        {
            return Error.Validation("GL.PrepaymentTerms", "A prepayment schedule releases a positive amount over 1 to 120 months.");
        }

        string reference = schedule.Reference.Trim().ToUpperInvariant();
        if (await _db.PrepaymentSchedules.AnyAsync(s => s.CompanyId == schedule.CompanyId && s.Reference == reference, ct).ConfigureAwait(false))
        {
            return Error.Conflict("GL.PrepaymentExists", $"Prepayment schedule {reference} already exists.");
        }

        PrepaymentSchedule created = new(schedule.CompanyId, reference, schedule.Description ?? string.Empty, schedule.PrepaymentAccountCode, schedule.ExpenseAccountCode, schedule.Total,
            schedule.FirstMonth, schedule.Months)
        {
            CostCentre = string.IsNullOrWhiteSpace(schedule.CostCentre) ? null : schedule.CostCentre.Trim().ToUpperInvariant(),
            Department = string.IsNullOrWhiteSpace(schedule.Department) ? null : schedule.Department.Trim().ToUpperInvariant(),
        };
        _db.PrepaymentSchedules.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created.Id;
    }

    /// <inheritdoc />
    public Task<Result<PrepaymentRelease>> ReleasePrepaymentsAsync(long companyId, DateOnly asAt, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
            List<PrepaymentSchedule> schedules = await _db.PrepaymentSchedules.Where(s => s.CompanyId == companyId && s.ReleasedMonths < s.Months && s.FirstMonth <= asAt)
                .OrderBy(s => s.Reference).ToListAsync(ct).ConfigureAwait(false);
            List<string> journals = [];
            int released = 0, touched = 0;
            decimal amount = 0m;
            foreach (PrepaymentSchedule schedule in schedules)
            {
                bool any = false;
                while (!schedule.IsFinished && schedule.MonthEnd(schedule.ReleasedMonths) <= asAt)
                {
                    int index = schedule.ReleasedMonths;
                    decimal month = schedule.AmountFor(index);
                    Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
                    {
                        CompanyId = companyId, SourceModule = "GL", PostingDate = schedule.MonthEnd(index), TransactionCurrency = company.BaseCurrency, DocumentType = "GL-PREPREL",
                        DocumentNumber = $"{schedule.Reference}-{index + 1:00}", Narrative = $"{schedule.Description} - month {index + 1} of {schedule.Months}",
                        Lines =
                        [
                            new JournalLineRequest { AccountCode = schedule.ExpenseAccountCode, Side = JournalSide.Debit, TransactionAmount = month, CostCentre = schedule.CostCentre, Department = schedule.Department },
                            new JournalLineRequest { AccountCode = schedule.PrepaymentAccountCode, Side = JournalSide.Credit, TransactionAmount = month },
                        ],
                    }, ct).ConfigureAwait(false);
                    if (posted.IsFailure)
                    {
                        return posted.Error;
                    }

                    schedule.MarkReleased(month);
                    journals.Add(posted.Value.JournalNumber);
                    released++;
                    amount += month;
                    any = true;
                }

                touched += any ? 1 : 0;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<PrepaymentRelease>.Success(new PrepaymentRelease(touched, released, amount, journals));
        }, ct);

    /// <inheritdoc />
    public Task<Result<ReversingJournalResult>> PostAccrualAsync(long companyId, DateOnly date, DateOnly reversalDate, string expenseAccountCode, string accrualAccountCode, decimal amount, string documentNumber, string narrative, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostReversingAsync(companyId, date, reversalDate, expenseAccountCode, accrualAccountCode, amount, "GL-ACCR", documentNumber, narrative, ct), ct);

    /// <inheritdoc />
    public Task<Result<ReversingJournalResult>> PostPrepaymentAsync(long companyId, DateOnly date, DateOnly reversalDate, string prepaymentAccountCode, string expenseAccountCode, decimal amount, string documentNumber, string narrative, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostReversingAsync(companyId, date, reversalDate, prepaymentAccountCode, expenseAccountCode, amount, "GL-PREP", documentNumber, narrative, ct), ct);

    // The journal and its reversal stand or fall together: an accrual whose reversal cannot post (a closed period, say)
    // must not be left on the ledger un-reversed. Run inside AtomicWork by the public methods.
    private async Task<Result<ReversingJournalResult>> PostReversingAsync(long companyId, DateOnly date, DateOnly reversalDate, string debitAccountCode, string creditAccountCode, decimal amount, string documentType, string documentNumber, string narrative, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Rule("GL.ReversingNonPositive", "The amount must be positive.");
        }

        if (reversalDate <= date)
        {
            return Error.Rule("GL.ReversalNotAfter", "The reversal date must be after the posting date.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        Result<PostingReceipt> reversal = await _posting.ReverseAsync(companyId, posted.Value.JournalId, reversalDate, $"Auto-reversal of {narrative}", ct).ConfigureAwait(false);
        return reversal.IsFailure
            ? reversal.Error
            : new ReversingJournalResult(posted.Value.JournalNumber, reversal.Value.JournalNumber);
    }
}

/// <summary>Scheduled task: releases the prepayment months that have ended (key <c>gl.prepayment-release</c>); a month already released is skipped.</summary>
public sealed class PrepaymentReleaseTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly IAccrualService _accruals;

    /// <summary>Creates the task.</summary>
    /// <param name="accruals">The accrual service.</param>
    public PrepaymentReleaseTask(IAccrualService accruals) => _accruals = accruals;

    /// <inheritdoc />
    public string Key => "gl.prepayment-release";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        Result<PrepaymentRelease> run = await _accruals.ReleasePrepaymentsAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), ct).ConfigureAwait(false);
        return run.IsFailure ? run.Error : $"{run.Value.Months} prepayment month(s) released from {run.Value.Schedules} schedule(s), {run.Value.Amount:N2} charged to expense.";
    }
}
