using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>The signed-in employee as self-service shows them.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="EmployeeId">The employee.</param>
/// <param name="EmployeeNumber">The employee number.</param>
/// <param name="FullName">The name.</param>
/// <param name="Department">The department.</param>
/// <param name="PaymentMethod">How they are paid.</param>
/// <param name="BankAccountMasked">The bank account, all but the last four characters masked.</param>
public sealed record SelfServiceProfile(long CompanyId, long EmployeeId, string EmployeeNumber, string FullName, string? Department, string PaymentMethod, string? BankAccountMasked);

/// <summary>One period's pay as self-service lists it (all runs of the period together).</summary>
/// <param name="PeriodLabel">The period.</param>
/// <param name="PayDate">The first pay date in the period.</param>
/// <param name="Gross">Gross pay.</param>
/// <param name="Deductions">Deductions.</param>
/// <param name="Net">Net pay.</param>
public sealed record PayslipSummary(string PeriodLabel, DateOnly PayDate, decimal Gross, decimal Deductions, decimal Net);

/// <summary>The year's tax certificate figures.</summary>
/// <param name="Year">The tax year.</param>
/// <param name="EmployeeNumber">The employee number.</param>
/// <param name="FullName">The name.</param>
/// <param name="TaxNumber">The tax number.</param>
/// <param name="Gross">Gross pay.</param>
/// <param name="Taxable">Taxable pay.</param>
/// <param name="TaxDeducted">Income tax deducted.</param>
public sealed record TaxCertificate(int Year, string EmployeeNumber, string FullName, string? TaxNumber, decimal Gross, decimal Taxable, decimal TaxDeducted);

/// <summary>A leave balance.</summary>
/// <param name="LeaveType">The leave type.</param>
/// <param name="Accrued">Days accrued.</param>
/// <param name="Taken">Days taken.</param>
/// <param name="Balance">Days left.</param>
/// <param name="Pending">Days applied for and awaiting HR.</param>
public sealed record LeaveBalanceView(string LeaveType, decimal Accrued, decimal Taken, decimal Balance, decimal Pending);

/// <summary>A self-service request awaiting HR.</summary>
/// <param name="Kind">Leave or Detail.</param>
/// <param name="Id">The request.</param>
/// <param name="EmployeeNumber">The employee number.</param>
/// <param name="FullName">The name.</param>
/// <param name="Summary">What is asked for.</param>
public sealed record SelfServiceRequest(string Kind, long Id, string EmployeeNumber, string FullName, string Summary);

/// <summary>
/// Employee self-service (brief §4.15.7): the signed-in user's own payslips, tax certificate and leave, and leave applications and
/// detail changes that wait for HR. Everything is found through the user's employee record - never an employee id supplied by the
/// caller - and HR decisions are maker-checker: nobody decides their own request.
/// </summary>
public interface IEmployeeSelfService
{
    /// <summary>The signed-in user's employee record (<c>Ess.NotAnEmployee</c> when the login has none).</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The profile.</returns>
    Task<Result<SelfServiceProfile>> ProfileAsync(long userId, CancellationToken ct = default);

    /// <summary>The user's pay by period, newest first.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The periods.</returns>
    Task<Result<IReadOnlyList<PayslipSummary>>> PayslipsAsync(long userId, CancellationToken ct = default);

    /// <summary>The user's payslip for a period as committed (every run of the period together), as text.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The payslip text.</returns>
    Task<Result<string>> PayslipTextAsync(long userId, string periodLabel, CancellationToken ct = default);

    /// <summary>
    /// Sets the password that opens the user's payslip PDFs, for a company whose rule is <see cref="One4All.Core.Payroll.PayslipPasswordRule.EmployeeSet"/>
    /// (8 or more characters). Stored encrypted; never shown again.
    /// </summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="password">The new password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SetPayslipPasswordAsync(long userId, string password, CancellationToken ct = default);

    /// <summary>The user's tax certificate figures for a year (by pay date).</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="year">The year.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificate.</returns>
    Task<Result<TaxCertificate>> TaxCertificateAsync(long userId, int year, CancellationToken ct = default);

    /// <summary>The user's leave balances, with days already applied for.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The balances.</returns>
    Task<Result<IReadOnlyList<LeaveBalanceView>>> LeaveBalancesAsync(long userId, CancellationToken ct = default);

    /// <summary>Applies for leave; refused beyond the balance less pending applications (<c>Ess.LeaveBalance</c>) or for bad dates.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="leaveType">The leave type.</param>
    /// <param name="fromDate">The first day.</param>
    /// <param name="toDate">The last day.</param>
    /// <param name="days">The working days.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The application id.</returns>
    Task<Result<long>> ApplyForLeaveAsync(long userId, string leaveType, DateOnly fromDate, DateOnly toDate, decimal days, CancellationToken ct = default);

    /// <summary>Asks for one of the user's own details to change (BankName, BankBranch, BankAccountNumber, MobileMoneyNumber).</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="field">The detail.</param>
    /// <param name="newValue">The new value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The request id.</returns>
    Task<Result<long>> RequestDetailChangeAsync(long userId, string field, string newValue, CancellationToken ct = default);

    /// <summary>The company's requests awaiting HR.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The requests.</returns>
    Task<IReadOnlyList<SelfServiceRequest>> PendingAsync(long companyId, CancellationToken ct = default);

    /// <summary>Approves (taking the leave) or rejects a leave application; nobody decides their own (<c>Ess.OwnRequest</c>).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="applicationId">The application.</param>
    /// <param name="approve">Approve or reject.</param>
    /// <param name="deciderUserId">Who decides.</param>
    /// <param name="note">A note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status.</returns>
    Task<Result<SelfServiceStatus>> DecideLeaveAsync(long companyId, long applicationId, bool approve, long deciderUserId, string? note, CancellationToken ct = default);

    /// <summary>Approves (applying the change) or rejects a detail change; nobody decides their own (<c>Ess.OwnRequest</c>).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requestId">The request.</param>
    /// <param name="approve">Approve or reject.</param>
    /// <param name="deciderUserId">Who decides.</param>
    /// <param name="note">A note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status.</returns>
    Task<Result<SelfServiceStatus>> DecideDetailChangeAsync(long companyId, long requestId, bool approve, long deciderUserId, string? note, CancellationToken ct = default);
}
