using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Tracks employee leave balances (accrual and days taken) and trues up the leave provision.</summary>
public sealed class LeaveService : ILeaveService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public LeaveService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> AccrueAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct = default)
    {
        if (days <= 0m)
        {
            return Error.Rule("Pay.LeaveNonPositive", "The days to accrue must be positive.");
        }

        Result<EmployeeLeaveBalance> balance = await LoadOrCreateAsync(companyId, employeeId, leaveType, ct).ConfigureAwait(false);
        if (balance.IsFailure)
        {
            return balance.Error;
        }

        balance.Value.Accrue(days);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return balance.Value.BalanceDays;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> TakeAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct = default)
    {
        if (days <= 0m)
        {
            return Error.Rule("Pay.LeaveNonPositive", "The days taken must be positive.");
        }

        Result<EmployeeLeaveBalance> balance = await LoadOrCreateAsync(companyId, employeeId, leaveType, ct).ConfigureAwait(false);
        if (balance.IsFailure)
        {
            return balance.Error;
        }

        if (days > balance.Value.BalanceDays)
        {
            return Error.Rule("Pay.InsufficientLeave", $"Only {balance.Value.BalanceDays} days of {balance.Value.LeaveType} leave are available.");
        }

        balance.Value.Take(days);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return balance.Value.BalanceDays;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> RaiseLeaveProvisionAsync(long companyId, DateOnly date, decimal dailyRate, string leaveExpenseAccountCode, string leaveProvisionAccountCode, string documentNumber, CancellationToken ct = default)
    {
        if (dailyRate < 0m)
        {
            return Error.Rule("Pay.NegativeRate", "The daily rate cannot be negative.");
        }

        // Target provision = value of all outstanding leave.
        decimal totalDays = await _db.Set<EmployeeLeaveBalance>()
            .Where(b => b.CompanyId == companyId)
            .SumAsync(b => (decimal?)(b.AccruedDays - b.TakenDays), ct).ConfigureAwait(false) ?? 0m;
        decimal target = decimal.Round(totalDays * dailyRate, 4, MidpointRounding.ToEven);

        // Current provision = net credit balance on the provision account across posted journals to date.
        decimal provided = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate <= date && l.AccountCode == leaveProvisionAccountCode
            select (decimal?)(l.BaseCredit - l.BaseDebit)).SumAsync(ct).ConfigureAwait(false) ?? 0m;

        decimal delta = decimal.Round(target - provided, 4, MidpointRounding.ToEven);
        if (delta == 0m)
        {
            return target;
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        List<JournalLineRequest> lines = delta > 0m
            ?
            [
                new JournalLineRequest { AccountCode = leaveExpenseAccountCode, Side = JournalSide.Debit, TransactionAmount = delta },
                new JournalLineRequest { AccountCode = leaveProvisionAccountCode, Side = JournalSide.Credit, TransactionAmount = delta },
            ]
            :
            [
                new JournalLineRequest { AccountCode = leaveProvisionAccountCode, Side = JournalSide.Debit, TransactionAmount = -delta },
                new JournalLineRequest { AccountCode = leaveExpenseAccountCode, Side = JournalSide.Credit, TransactionAmount = -delta },
            ];

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "PAY-LEAVE-PROV",
            DocumentNumber = documentNumber,
            Narrative = "Leave provision true-up",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        return posted.IsFailure ? posted.Error : target;
    }

    private async Task<Result<EmployeeLeaveBalance>> LoadOrCreateAsync(long companyId, long employeeId, string leaveType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(leaveType))
        {
            return Error.Validation("Pay.NoLeaveType", "A leave type is required.");
        }

        bool employeeExists = await _db.Set<Employee>().AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!employeeExists)
        {
            return Error.NotFound("Pay.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        string normalized = leaveType.Trim().ToUpperInvariant();
        EmployeeLeaveBalance? balance = await _db.Set<EmployeeLeaveBalance>()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.EmployeeId == employeeId && b.LeaveType == normalized, ct).ConfigureAwait(false);
        if (balance is null)
        {
            balance = new EmployeeLeaveBalance(companyId, employeeId, normalized);
            _db.Set<EmployeeLeaveBalance>().Add(balance);
        }

        return balance;
    }
}
