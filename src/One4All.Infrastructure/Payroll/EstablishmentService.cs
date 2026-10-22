using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Salary scales and increments, acting appointments and the post register.</summary>
public sealed class EstablishmentService : IEstablishmentService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public EstablishmentService(One4AllDbContext db) => _db = db;

    /// <summary>
    /// Why an employee cannot hold a department and grade: once a company keeps a post register, the post must exist and have a vacancy (brief
    /// §4.15.1: "a block on engaging an employee against a post that does not exist or is already filled"). Companies without a register are not held to it.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="department">The department.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="exceptEmployeeId">The employee being moved (they do not count against their own post).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The problem, or null.</returns>
    public static async Task<Error?> PostProblemAsync(One4AllDbContext db, long companyId, string? department, string? grade, long? exceptEmployeeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!await db.EstablishmentPosts.AnyAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return null;
        }

        string dept = (department ?? string.Empty).Trim().ToUpperInvariant(), grd = (grade ?? string.Empty).Trim().ToUpperInvariant();
        EstablishmentPost? post = (await db.EstablishmentPosts.AsNoTracking().Where(p => p.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Department.Trim(), dept, StringComparison.OrdinalIgnoreCase) && string.Equals(p.Grade.Trim(), grd, StringComparison.OrdinalIgnoreCase));
        if (post is null)
        {
            return Error.Rule("Payroll.NoSuchPost", $"There is no approved post for grade {grd} in department {dept}.");
        }

        int filled = (await db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.Status == EmployeeStatus.Active && (exceptEmployeeId == null || e.Id != exceptEmployeeId))
            .Select(e => new { e.Department, e.Grade }).ToListAsync(ct).ConfigureAwait(false))
            .Count(e => string.Equals((e.Department ?? string.Empty).Trim(), dept, StringComparison.OrdinalIgnoreCase) && string.Equals((e.Grade ?? string.Empty).Trim(), grd, StringComparison.OrdinalIgnoreCase));
        return filled >= post.ApprovedPosts
            ? Error.Rule("Payroll.PostFilled", $"All {post.ApprovedPosts} approved {grd} post(s) in {dept} are filled.")
            : null;
    }

    /// <inheritdoc />
    public async Task<Result> SetScaleAsync(long companyId, string grade, int notch, decimal monthlyAmount, DateOnly effectiveFrom, CancellationToken ct = default)
    {
        string key = (grade ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0 || notch < 1 || monthlyAmount < 0m)
        {
            return Result.Failure(Error.Validation("Payroll.Scale", "A scale notch needs a grade, a notch from 1 and a salary that is not negative."));
        }

        SalaryScale? existing = await _db.SalaryScales.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Grade == key && s.Notch == notch && s.EffectiveFrom == effectiveFrom, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            _db.SalaryScales.Add(new SalaryScale(companyId, key, notch, monthlyAmount, effectiveFrom));
        }
        else
        {
            existing.MonthlyAmount = monthlyAmount;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IncrementResult>> RunIncrementsAsync(long companyId, DateOnly effectiveDate, CancellationToken ct = default)
    {
        List<SalaryScale> scales = await _db.SalaryScales.AsNoTracking().Where(s => s.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        if (scales.Count == 0)
        {
            return Error.Rule("Payroll.NoScales", "There are no salary scales to increment on.");
        }

        List<(string, int, int, decimal)> moved = [];
        List<string> atTop = [];
        foreach (Employee employee in await _db.Employees.Where(e => e.CompanyId == companyId && e.Status == EmployeeStatus.Active && e.Grade != null && e.Notch != null)
            .OrderBy(e => e.EmployeeNumber).ToListAsync(ct).ConfigureAwait(false))
        {
            if (employee.LastIncrementOn is DateOnly last && last.AddYears(1) > effectiveDate)
            {
                continue; // incremented within the year
            }

            int? top = SalaryScale.TopNotch(scales, employee.Grade, effectiveDate);
            if (top is null)
            {
                continue; // a grade without a scale
            }

            if (employee.Notch >= top)
            {
                atTop.Add(employee.EmployeeNumber);
                continue;
            }

            int from = employee.Notch!.Value, to = from + 1;
            if (SalaryScale.Amount(scales, employee.Grade, to, effectiveDate) is not decimal salary)
            {
                continue;
            }

            employee.Notch = to;
            PayrollSetupService.SetDecidedSalary(_db, employee, salary, effectiveDate, null, null, $"Annual increment to notch {to}");
            employee.LastIncrementOn = effectiveDate;
            moved.Add((employee.EmployeeNumber, from, to, salary));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IncrementResult(moved, atTop);
    }

    /// <inheritdoc />
    public async Task<Result<long>> AppointActingAsync(long companyId, string employeeNumber, string grade, int notch, DateOnly from, DateOnly? to, CancellationToken ct = default)
    {
        string number = (employeeNumber ?? string.Empty).Trim().ToUpperInvariant();
        Employee? employee = await _db.Employees.FirstOrDefaultAsync(e => e.CompanyId == companyId && e.EmployeeNumber == number, ct).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound("Payroll.EmployeeNotFound", $"Employee {number} not found.");
        }

        if (to is DateOnly end && end < from)
        {
            return Error.Validation("Payroll.ActingDates", "An acting appointment cannot end before it starts.");
        }

        List<SalaryScale> scales = await _db.SalaryScales.AsNoTracking().Where(s => s.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        if (SalaryScale.Amount(scales, grade, notch, from) is null)
        {
            return Error.Validation("Payroll.ActingScale", $"Grade {grade} has no scale at notch {notch}.");
        }

        ActingAppointment acting = new(companyId, employee.Id, grade, notch, from, to);
        _db.ActingAppointments.Add(acting);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return acting.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostVacancy>> VacanciesAsync(long companyId, CancellationToken ct = default)
    {
        List<EstablishmentPost> posts = await _db.EstablishmentPosts.AsNoTracking().Where(p => p.CompanyId == companyId).OrderBy(p => p.Department).ThenBy(p => p.Grade).ToListAsync(ct).ConfigureAwait(false);
        var staff = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.Status == EmployeeStatus.Active).Select(e => new { e.Department, e.Grade }).ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. posts.Select(p => new PostVacancy(p.Department, p.Grade, p.ApprovedPosts,
            staff.Count(e => string.Equals((e.Department ?? string.Empty).Trim(), p.Department.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals((e.Grade ?? string.Empty).Trim(), p.Grade.Trim(), StringComparison.OrdinalIgnoreCase))))];
    }
}
