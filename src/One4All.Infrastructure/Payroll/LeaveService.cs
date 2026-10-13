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
    public Task<Result<decimal>> AccrueAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct = default)
        => _db.AtomicAsync(() => AccrueCoreAsync(companyId, employeeId, leaveType, days, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<decimal>> AccrueCoreAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct)
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
    public Task<Result<decimal>> TakeAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct = default)
        => _db.AtomicAsync(() => TakeCoreAsync(companyId, employeeId, leaveType, days, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<decimal>> TakeCoreAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct)
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

        // A policy may let leave be taken in advance, down to so many days below nil.
        string type = balance.Value.LeaveType;
        decimal advance = await _db.Set<LeavePolicy>().Where(p => p.CompanyId == companyId && p.LeaveType == type && p.IsActive)
            .Select(p => (decimal?)p.NegativeBalanceDays).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? 0m;
        if (days > balance.Value.BalanceDays + advance)
        {
            return Error.Rule("Pay.InsufficientLeave", advance > 0m
                ? $"Only {balance.Value.BalanceDays} days of {type} leave are available, plus {advance} in advance."
                : $"Only {balance.Value.BalanceDays} days of {type} leave are available.");
        }

        balance.Value.Take(days);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return balance.Value.BalanceDays;
    }

    /// <inheritdoc />
    public Task<Result<decimal>> RaisePayrollProvisionAsync(PayrollProvisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.ExpenseAccountCode) || string.IsNullOrWhiteSpace(request.ProvisionAccountCode) || string.IsNullOrWhiteSpace(request.DocumentNumber)
                || request.Kind switch
                {
                    PayrollProvisionKind.Gratuity => request.DaysPerYear <= 0m || request.WorkingDaysPerMonth <= 0m,
                    PayrollProvisionKind.LongService => request.LongServiceAwards.Count == 0 || request.LongServiceAwards.Any(a => a.Years <= 0 || a.MonthsOfPay <= 0m),
                    _ => request.MonthsOfPay <= 0m,
                }
                || request.DiscountRatePercent < 0m || request.RetirementAge is < 40 or > 80)
            {
                return Error.Validation("Pay.Provision", "A provision needs its accounts, a document number and a positive rate (months' pay for a bonus, days per year for gratuity, "
                    + "milestones for long service) and a sensible discount rate and retirement age.");
            }

            // Target = what the active employees have earned to the date (gratuity at present value when a discount rate is given).
            var staff = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == request.CompanyId && e.Status == EmployeeStatus.Active)
                .Select(e => new { e.BasicSalary, e.EngagementDate, e.DateOfBirth }).ToListAsync(ct).ConfigureAwait(false);
            decimal target = staff.Sum(e => request.Kind switch
            {
                PayrollProvisionKind.Bonus => Application.Payroll.PayrollProvisions.BonusEarned(e.BasicSalary, e.EngagementDate, request.YearStart, request.Date, request.MonthsOfPay),
                PayrollProvisionKind.Gratuity => Application.Payroll.PayrollProvisions.PresentValue(
                    Application.Payroll.PayrollProvisions.GratuityEarned(e.BasicSalary, e.EngagementDate, request.Date, request.DaysPerYear, request.WorkingDaysPerMonth),
                    e.DateOfBirth, request.Date, request.RetirementAge, request.DiscountRatePercent, request.SalaryGrowthPercent),
                _ => Application.Payroll.PayrollProvisions.LongServiceEarned(e.BasicSalary, e.EngagementDate, request.Date, request.LongServiceAwards),
            });
            string provision = request.ProvisionAccountCode.Trim(), expense = request.ExpenseAccountCode.Trim();
            decimal provided = await (
                from l in _db.Set<JournalLine>()
                join j in _db.Journals on l.JournalEntryId equals j.Id
                where j.CompanyId == request.CompanyId && j.Status == JournalStatus.Posted && j.PostingDate <= request.Date && l.AccountCode == provision
                select (decimal?)(l.BaseCredit - l.BaseDebit)).SumAsync(ct).ConfigureAwait(false) ?? 0m;
            decimal delta = decimal.Round(target - provided, 2, MidpointRounding.AwayFromZero);
            if (delta == 0m)
            {
                return Result<decimal>.Success(target);
            }

            Company company = await _db.Set<Company>().FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
            (string debit, string credit) = delta > 0m ? (expense, provision) : (provision, expense);
            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = request.CompanyId, SourceModule = "GL", PostingDate = request.Date, TransactionCurrency = company.BaseCurrency,
                DocumentType = request.Kind switch { PayrollProvisionKind.Bonus => "PAY-BONUS-PROV", PayrollProvisionKind.Gratuity => "PAY-GRAT-PROV", _ => "PAY-LSA-PROV" },
                DocumentNumber = request.DocumentNumber.Trim(),
                Narrative = $"{request.Kind} provision true-up to {target:0.00}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = Math.Abs(delta) },
                    new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = Math.Abs(delta) },
                ],
            }, ct).ConfigureAwait(false);
            return posted.IsFailure ? Result<decimal>.Failure(posted.Error) : Result<decimal>.Success(target);
        }, ct);
    }

    /// <inheritdoc />
    public Task<Result<decimal>> RaiseLeaveProvisionAsync(long companyId, DateOnly date, decimal dailyRate, string leaveExpenseAccountCode, string leaveProvisionAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => RaiseLeaveProvisionCoreAsync(companyId, date, dailyRate, leaveExpenseAccountCode, leaveProvisionAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<decimal>> RaiseLeaveProvisionCoreAsync(long companyId, DateOnly date, decimal dailyRate, string leaveExpenseAccountCode, string leaveProvisionAccountCode, string documentNumber, CancellationToken ct)
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
