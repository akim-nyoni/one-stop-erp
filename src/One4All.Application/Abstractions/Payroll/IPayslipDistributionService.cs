using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>The outcome of e-mailing a run's payslips.</summary>
/// <param name="Queued">Payslips queued.</param>
/// <param name="NoEmail">Employees with no e-mail address.</param>
/// <param name="NoPassword">Employees with nothing to protect the PDF with under the company's rule.</param>
public sealed record PayslipEmailRun(int Queued, IReadOnlyList<string> NoEmail, IReadOnlyList<string> NoPassword);

/// <summary>
/// Payslips as password-protected PDFs (brief §4.15.7): opened with the national ID, date of birth or employee number, as the company's
/// <c>PayslipPasswordRule</c> says, and e-mailed to each employee of a run.
/// </summary>
public interface IPayslipDistributionService
{
    /// <summary>An employee's committed payslip for a period as a protected PDF (<c>Payroll.NoPassword</c> when the rule has nothing to use).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The PDF.</returns>
    Task<Result<byte[]>> PayslipPdfAsync(long companyId, long employeeId, string periodLabel, CancellationToken ct = default);

    /// <summary>Queues every employee of a run their payslip PDF by e-mail, listing who could not be sent one and why.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runNumber">The run's document or journal number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<Result<PayslipEmailRun>> EmailRunAsync(long companyId, string runNumber, CancellationToken ct = default);
}
