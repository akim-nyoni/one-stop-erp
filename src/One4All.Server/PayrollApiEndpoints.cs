using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Security;
using One4All.Application.Api;
using One4All.Application.Security;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using static One4All.Server.TradingApiEndpoints;

namespace One4All.Server;

/// <summary>
/// The REST API's employee, payroll run and stand history endpoints (brief §11): engage, change and read an employee; list the payroll runs; read
/// an employee's payslip; read a stand's status history and lineage. Each step runs through the same application service the desktop uses.
/// Pay is sensitive (brief §4.15.10): pay fields leave the JSON for a user without Payroll › View Salary, a payslip is refused to them, and setting
/// a salary needs the same right. Bank details are set at engagement only; later changes go through an approved request. Where the company's payroll
/// needs approval, bank details given at engagement and a change of salary or payment method wait for another user's approval (202 on a change).
/// </summary>
internal static class PayrollApiEndpoints
{
    /// <summary>Maps the endpoints onto the API group.</summary>
    /// <param name="api">The API route group (authorisation is required on every route).</param>
    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        MapEmployees(api);
        MapRuns(api);
        MapStands(api);
    }

    private static void MapEmployees(RouteGroupBuilder api)
    {
        api.MapGet("/api/hr/employees/{number}", async (string number, long companyId, long userId, One4AllDbContext db, IPermissionService permissions, CancellationToken ct) =>
        {
            // The record carries national ID, tax and social security numbers, date of birth and next of kin: any payroll right opens the area
            // (a leave clerk's included), so the record itself needs the employee record's own view right.
            if (!(await permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has("Payroll.Employee", OperationFlags.View))
            {
                return Value(Result<bool>.Failure(Error.Forbidden("Api.EmployeeRecord", "Reading an employee's record needs the employee record right (Payroll.Employee, view).")));
            }

            string key = OrdersApiMapping.Key(number);
            Employee? employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.CompanyId == companyId && e.EmployeeNumber.ToUpper() == key, ct).ConfigureAwait(false);
            return employee is null ? NotFound($"No employee {key}.") : Results.Ok(await ViewAsync(db, employee, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ApiGetEmployee");

        api.MapPost("/api/hr/employees", async (long companyId, long userId, EmployeeApi request, One4AllDbContext db, IPermissionService permissions, IPayrollSetupService setup,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (await RefusedAsync(permissions, userId, request, OperationFlags.Create, ct).ConfigureAwait(false) is IResult refused)
            {
                return refused;
            }

            Result<NewEmployee> mapped = PayrollApiMapping.NewEmployee(request);
            if (mapped.IsFailure)
            {
                return Value(mapped);
            }

            // Where payroll needs approval, bank details given here wait for another user (the record is created without them).
            Result<long> created = await setup.CreateEmployeeAsync(companyId, mapped.Value with { RequestedByUserId = userId }, ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return Value(created);
            }

            Employee employee = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == created.Value, ct).ConfigureAwait(false);
            return Results.Created($"/api/hr/employees/{employee.EmployeeNumber}", await ViewAsync(db, employee, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ApiCreateEmployee");

        api.MapPut("/api/hr/employees/{number}", async (string number, long companyId, long userId, EmployeeApi request, One4AllDbContext db, IPermissionService permissions,
            IPayrollSetupService setup, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (await RefusedAsync(permissions, userId, request, OperationFlags.Edit, ct).ConfigureAwait(false) is IResult refused)
            {
                return refused;
            }

            Result<EmployeeUpdate> mapped = PayrollApiMapping.Update(number, request);
            if (mapped.IsFailure)
            {
                return Value(mapped);
            }

            Result updated = await setup.UpdateEmployeeAsync(companyId, number, mapped.Value with { RequestedByUserId = userId }, ct).ConfigureAwait(false);
            if (updated.IsFailure)
            {
                return Action(updated);
            }

            // A salary or payment-method change not yet in force (waiting for another user's approval, or dated later) answers 202 with the record as it is.
            string key = OrdersApiMapping.Key(number);
            Employee employee = await db.Employees.AsNoTracking().FirstAsync(e => e.CompanyId == companyId && e.EmployeeNumber.ToUpper() == key, ct).ConfigureAwait(false);
            bool waiting = (mapped.Value.BasicSalary is decimal salary && salary != employee.BasicSalary)
                || (mapped.Value.PaymentMethod is PayMethod method && method != employee.PaymentMethod);
            EmployeeViewApi view = await ViewAsync(db, employee, ct).ConfigureAwait(false);
            return waiting ? Results.Accepted($"/api/hr/employees/{employee.EmployeeNumber}", view) : Results.Ok(view);
        }).RequireAuthorization().WithName("ApiUpdateEmployee");
    }

    private static void MapRuns(RouteGroupBuilder api)
    {
        api.MapGet("/api/payroll/runs", async (long companyId, int? take, One4AllDbContext db, CancellationToken ct) =>
        {
            List<PayrollRunRecord> runs = await db.Set<PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == companyId).OrderByDescending(r => r.PayDate).ThenByDescending(r => r.Id)
                .Take(Math.Clamp(take ?? 24, 1, 200)).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(runs.Select(PayrollApiMapping.Run).ToList());
        }).RequireAuthorization().WithName("PayrollRuns");

        // The payslip shows pay, which the response filter cannot strip from text: it needs Payroll › View Salary as well as the payroll area.
        api.MapGet("/api/payroll/payslips/{employeeNumber}", async (string employeeNumber, long companyId, long userId, string? period, One4AllDbContext db, IPermissionService permissions,
            IPayrollService payroll, CancellationToken ct) =>
        {
            if (FieldSensitivity.HiddenFor(await permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Contains(SensitiveField.Salary))
            {
                return Value(Result<bool>.Failure(Error.Forbidden("Api.ViewSalary", "Reading a payslip needs the right to view salaries (Payroll.ViewSalary).")));
            }

            string key = OrdersApiMapping.Key(employeeNumber);
            long? employee = await db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.EmployeeNumber.ToUpper() == key).Select(e => (long?)e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (employee is null)
            {
                return NotFound($"No employee {key}.");
            }

            string? wanted = string.IsNullOrWhiteSpace(period) ? null : period.Trim();
            var slip = await db.Payslips.AsNoTracking().Where(p => p.CompanyId == companyId && p.EmployeeId == employee && (wanted == null || p.PeriodLabel == wanted))
                .OrderByDescending(p => p.PayDate).ThenByDescending(p => p.Id).Select(p => new { p.PeriodLabel, p.PayDate }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (slip is null)
            {
                return NotFound(wanted is null ? $"Employee {key} has no payslip." : $"Employee {key} has no payslip for {wanted}.");
            }

            Result<string> text = await payroll.RenderPayslipTextAsync(companyId, employee.Value, slip.PeriodLabel, slip.PayDate, ct: ct).ConfigureAwait(false);
            return text.IsFailure ? Value(text) : Results.Text(text.Value, "text/plain; charset=utf-8");
        }).RequireAuthorization().WithName("ApiPayslipText");
    }

    private static void MapStands(RouteGroupBuilder api)
    {
        api.MapGet("/api/housing/stands/{number}/history", async (string number, long companyId, One4AllDbContext db, IStandLifecycleService stands, CancellationToken ct) =>
        {
            Result<StandHistory> history = await stands.HistoryAsync(companyId, number, ct).ConfigureAwait(false);
            if (history.IsFailure)
            {
                return Value(history);
            }

            List<long> ids = [.. history.Value.Moves.Where(m => m.UserId is not null).Select(m => m.UserId!.Value).Distinct()];
            Dictionary<long, string> users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
            return Results.Ok(PayrollApiMapping.StandHistory(history.Value, users));
        }).RequireAuthorization().WithName("ApiStandHistory");
    }

    // Engaging needs Payroll › Employee Master to create, changing it to edit; a salary in the body needs Payroll › View Salary too (pay is not set blind).
    private static async Task<IResult?> RefusedAsync(IPermissionService permissions, long userId, EmployeeApi request, OperationFlags operation, CancellationToken ct)
    {
        EffectivePermissions granted = await permissions.ResolveAsync(userId, ct).ConfigureAwait(false);
        if (!granted.Has("Payroll.Employee", operation))
        {
            return Value(Result<bool>.Failure(Error.Forbidden("Api.Employee", $"{(operation == OperationFlags.Create ? "Engaging" : "Changing")} an employee needs the employee master right to {operation.ToString().ToLowerInvariant()} (Payroll.Employee).")));
        }

        return request.BasicSalary is not null && FieldSensitivity.HiddenFor(granted).Contains(SensitiveField.Salary)
            ? Value(Result<bool>.Failure(Error.Forbidden("Api.ViewSalary", "Setting a salary needs the right to view salaries (Payroll.ViewSalary).")))
            : null;
    }

    private static async Task<EmployeeViewApi> ViewAsync(One4AllDbContext db, Employee employee, CancellationToken ct)
    {
        string? branch = employee.BranchId is long branchId
            ? await db.Branches.AsNoTracking().Where(b => b.Id == branchId).Select(b => b.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        string? reportsTo = employee.ReportsToEmployeeId is long managerId
            ? await db.Employees.AsNoTracking().Where(e => e.Id == managerId).Select(e => e.EmployeeNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        string? tenancy = employee.HousingTenancyId is long tenancyId
            ? await db.Set<Core.Housing.Tenancy>().AsNoTracking().Where(t => t.Id == tenancyId).Select(t => t.TenancyNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        return PayrollApiMapping.Employee(employee, branch, reportsTo, tenancy);
    }
}
