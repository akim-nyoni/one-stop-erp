using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Tracks employee leave balances (accrual and days taken).</summary>
public sealed class LeaveService : ILeaveService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public LeaveService(One4AllDbContext db) => _db = db;

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
