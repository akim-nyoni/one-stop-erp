using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>Tracks employee leave balances (accrual and days taken). Operational HR — no GL.</summary>
public interface ILeaveService
{
    /// <summary>Accrues leave days of a type for an employee (creating the balance if needed).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type (e.g. ANNUAL, SICK).</param>
    /// <param name="days">The days to accrue (positive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The balance available after accrual.</returns>
    Task<Result<decimal>> AccrueAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct = default);

    /// <summary>
    /// Records leave taken for an employee, reducing the available balance. Refuses more than the available
    /// balance.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type.</param>
    /// <param name="days">The days taken (positive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The balance available after the leave is taken.</returns>
    Task<Result<decimal>> TakeAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct = default);

    /// <summary>
    /// Trues up the leave provision (IAS 19) to the value of all outstanding leave (total balance days × the
    /// daily rate): posts the difference against the provision liability — an increase debits leave expense and
    /// credits the provision, a decrease releases it back. A nil change posts nothing. Idempotent across
    /// periods: re-running only posts the movement to the new target.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The provision date.</param>
    /// <param name="dailyRate">The value of one leave day.</param>
    /// <param name="leaveExpenseAccountCode">The leave-expense account.</param>
    /// <param name="leaveProvisionAccountCode">The leave-provision liability account.</param>
    /// <param name="documentNumber">The provision document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provision level (target liability) after the true-up.</returns>
    Task<Result<decimal>> RaiseLeaveProvisionAsync(long companyId, DateOnly date, decimal dailyRate, string leaveExpenseAccountCode, string leaveProvisionAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>Trues up the bonus or gratuity provision to what active employees have earned to the date, posting the movement.</summary>
    /// <param name="request">The provision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provision level (the target) after the true-up.</returns>
    Task<Result<decimal>> RaisePayrollProvisionAsync(PayrollProvisionRequest request, CancellationToken ct = default);
}

/// <summary>A payroll liability built up between paydays.</summary>
public enum PayrollProvisionKind
{
    /// <summary>The annual bonus (13th cheque), earned through the bonus year.</summary>
    Bonus = 0,

    /// <summary>Gratuity, earned for each year of service.</summary>
    Gratuity = 1,

    /// <summary>Long-service awards, earned towards each service milestone.</summary>
    LongService = 2,
}

/// <summary>Trues up a bonus or gratuity provision to what active employees have earned (brief §4.15).</summary>
public sealed class PayrollProvisionRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the provision.</summary>
    public PayrollProvisionKind Kind { get; init; }

    /// <summary>Gets the as-at date (the journal's date).</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the first day of the bonus year (bonus).</summary>
    public DateOnly YearStart { get; init; }

    /// <summary>Gets the months' pay a full year's bonus is worth (bonus; 1 = a 13th cheque).</summary>
    public decimal MonthsOfPay { get; init; } = 1m;

    /// <summary>Gets the days' pay earned per year of service (gratuity).</summary>
    public decimal DaysPerYear { get; init; }

    /// <summary>Gets the working days a month's pay covers (gratuity's daily rate).</summary>
    public decimal WorkingDaysPerMonth { get; init; } = 26m;

    /// <summary>Gets the long-service milestones: years of service and the months' pay awarded at each (long service).</summary>
    public IReadOnlyList<(int Years, decimal MonthsOfPay)> LongServiceAwards { get; init; } = [];

    /// <summary>Gets the annual discount rate the gratuity provision is discounted at to retirement (0: not discounted).</summary>
    public decimal DiscountRatePercent { get; init; }

    /// <summary>Gets the annual salary growth assumed when discounting.</summary>
    public decimal SalaryGrowthPercent { get; init; }

    /// <summary>Gets the normal retirement age the benefit is discounted from.</summary>
    public int RetirementAge { get; init; } = 65;

    /// <summary>Gets the expense account.</summary>
    public string ExpenseAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the provision (liability) account.</summary>
    public string ProvisionAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the journal's document number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;
}
