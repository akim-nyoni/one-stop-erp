using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Stand leases: contractual rent through AR, income straight-lined against accrued lease income.</summary>
public sealed class StandLeaseService : IStandLeaseService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    /// <param name="engine">The posting engine.</param>
    public StandLeaseService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar, IPostingEngine engine)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<Result<string>> CreateAsync(StandLeaseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartDate.Day != 1 || request.TermYears is < 1 or > 99 || request.ReviewEveryYears < 1 || request.AnnualRent <= 0m || request.EscalationPercent <= -100m
            || string.IsNullOrWhiteSpace(request.IncomeAccountCode) || string.IsNullOrWhiteSpace(request.AccruedIncomeAccountCode))
        {
            return Error.Validation("Hse.LeaseTerms", "A lease starts on the first of a month, runs 1 to 99 years with a positive rent and reviews, and needs its income and accrued income accounts.");
        }

        if (!await _db.HousingStands.AnyAsync(s => s.Id == request.StandId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {request.StandId} not found.");
        }

        if (await StandClaims.AnyBlockedAsync(_db, [request.StandId], ct).ConfigureAwait(false))
        {
            return Error.Rule("Hse.StandBlocked", "The stand is blocked by a caveat and cannot be leased.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == request.LesseeCustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.LesseeNotFound", $"Customer {request.LesseeCustomerId} not found.");
        }

        List<StandLease> existing = await _db.Set<StandLease>().AsNoTracking().Where(l => l.StandId == request.StandId).ToListAsync(ct).ConfigureAwait(false);
        DateOnly end = request.StartDate.AddYears(request.TermYears).AddDays(-1);
        if (existing.Any(l => l.StartDate <= end && request.StartDate <= l.EndDate))
        {
            return Error.Conflict("Hse.StandLeased", "The stand is already leased for some of that term.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "LS", request.StartDate, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<StandLease>().Add(new StandLease(request.CompanyId, number.Value, request.StandId, request.LesseeCustomerId, request.Use, request.StartDate, request.TermYears,
            request.AnnualRent, request.ReviewEveryYears, request.EscalationPercent, request.IncomeAccountCode.Trim(), request.AccruedIncomeAccountCode.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LeaseYear>>> ScheduleAsync(long companyId, string leaseNumber, CancellationToken ct = default)
    {
        string number = (leaseNumber ?? string.Empty).Trim();
        StandLease? lease = await _db.Set<StandLease>().AsNoTracking().FirstOrDefaultAsync(l => l.CompanyId == companyId && l.LeaseNumber == number, ct).ConfigureAwait(false);
        if (lease is null)
        {
            return Error.NotFound("Hse.LeaseNotFound", $"Lease {number} not found.");
        }

        return Result<IReadOnlyList<LeaseYear>>.Success(LeaseSchedule.Years(lease.AnnualRent, lease.TermYears, lease.ReviewEveryYears, lease.EscalationPercent));
    }

    /// <inheritdoc />
    public Task<Result<LeaseRunResult>> RunMonthAsync(long companyId, DateOnly month, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunCoreAsync(companyId, month, ct), ct);

    private async Task<Result<LeaseRunResult>> RunCoreAsync(long companyId, DateOnly month, CancellationToken ct)
    {
        DateOnly first = new(month.Year, month.Month, 1);
        List<StandLease> leases = await _db.Set<StandLease>().AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.StartDate <= first)
            .OrderBy(l => l.LeaseNumber).ToListAsync(ct).ConfigureAwait(false);
        CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        int billed = 0;
        decimal rentTotal = 0m;
        decimal incomeTotal = 0m;
        foreach (StandLease lease in leases.Where(l => first <= l.EndDate))
        {
            string document = $"{lease.LeaseNumber}-{first:yyyyMM}";
            if (await _db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.DocumentNumber == document, ct).ConfigureAwait(false))
            {
                continue;
            }

            IReadOnlyList<LeaseYear> years = LeaseSchedule.Years(lease.AnnualRent, lease.TermYears, lease.ReviewEveryYears, lease.EscalationPercent);
            int monthIndex = ((first.Year - lease.StartDate.Year) * 12) + first.Month - lease.StartDate.Month + 1;
            decimal rent = LeaseSchedule.ContractualMonth(years, monthIndex);
            decimal income = LeaseSchedule.StraightLineMonth(years, monthIndex);

            Customer lessee = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == lease.LesseeCustomerId, ct).ConfigureAwait(false);
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = lessee.Id, DocumentNumber = document, DocumentDate = first, Currency = lessee.Currency,
                NetAmount = rent, SalesAccountCode = lease.IncomeAccountCode, Narrative = $"Lease {lease.LeaseNumber} rent, month {monthIndex} of {years.Count * 12}",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }

            // Straight-lining: move income between the month's rent and the straight-line amount through accrued lease income.
            decimal adjustment = income - rent;
            if (adjustment != 0m)
            {
                bool accrue = adjustment > 0m;
                Result<PostingReceipt> straightened = await _engine.PostAsync(new JournalRequest
                {
                    CompanyId = companyId, SourceModule = "AR", PostingDate = first, TransactionCurrency = currency, DocumentType = "LeaseStraightLining",
                    DocumentNumber = $"{document}-SL", Narrative = $"Lease {lease.LeaseNumber} straight-line income, month {monthIndex}",
                    Lines =
                    [
                        new JournalLineRequest { AccountCode = lease.AccruedIncomeAccountCode, Side = accrue ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(adjustment) },
                        new JournalLineRequest { AccountCode = lease.IncomeAccountCode, Side = accrue ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(adjustment) },
                    ],
                }, ct).ConfigureAwait(false);
                if (straightened.IsFailure)
                {
                    return straightened.Error;
                }
            }

            billed++;
            rentTotal += rent;
            incomeTotal += income;
        }

        return new LeaseRunResult(billed, rentTotal, incomeTotal);
    }
}
