using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Documents;
using One4All.Application.Payroll;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>
/// Payslips as password-protected PDFs (brief §4.15.7): the committed payslip for a period (every run of it together) written as a
/// PDF opened by the password the company's rule gives — national ID, date of birth or employee number — and queued by e-mail to each
/// employee of a run through the messaging service.
/// </summary>
public sealed class PayslipDistributionService : IPayslipDistributionService
{
    private readonly One4AllDbContext _db;
    private readonly INotificationService _notifications;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="notifications">The messaging service the e-mails are queued through.</param>
    public PayslipDistributionService(One4AllDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    /// <inheritdoc />
    public async Task<Result<byte[]>> PayslipPdfAsync(long companyId, long employeeId, string periodLabel, CancellationToken ct = default)
    {
        Employee? employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound("Payroll.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        return await PdfAsync(company, employee, (periodLabel ?? string.Empty).Trim(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<PayslipEmailRun>> EmailRunAsync(long companyId, string runNumber, CancellationToken ct = default)
    {
        string key = (runNumber ?? string.Empty).Trim();
        PayrollRunRecord? run = await _db.Set<PayrollRunRecord>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && key != string.Empty && (r.DocumentNumber == key || r.JournalNumber == key), ct).ConfigureAwait(false);
        if (run is null || run.DocumentNumber.Length == 0 || run.Kind == PayRunKind.Reversal)
        {
            return Error.NotFound("Payroll.RunNotFound", $"No payroll run '{key}'.");
        }

        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        List<long> ids = await _db.Payslips.Where(p => p.CompanyId == companyId && p.RunReference == run.DocumentNumber).Select(p => p.EmployeeId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        List<Employee> employees = await _db.Employees.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => ids.Contains(e.Id)).OrderBy(e => e.EmployeeNumber).ToListAsync(ct).ConfigureAwait(false);
        int queued = 0;
        List<string> noEmail = [], noPassword = [];
        foreach (Employee employee in employees)
        {
            if (string.IsNullOrWhiteSpace(employee.Email))
            {
                noEmail.Add(employee.EmployeeNumber);
                continue;
            }

            Result<byte[]> pdf = await PdfAsync(company, employee, run.PeriodLabel, ct).ConfigureAwait(false);
            if (pdf.IsFailure)
            {
                if (pdf.Error.Code != "Payroll.NoPassword")
                {
                    return pdf.Error;
                }

                noPassword.Add(employee.EmployeeNumber);
                continue;
            }

            string subject = $"{company.Name}: payslip {run.PeriodLabel}";
            string body = $"Dear {employee.FullName},\n\nYour payslip for {run.PeriodLabel} is attached. {PasswordHint(company.PayslipPasswordRule)}";
            Result<long> batch = await _notifications.QueueDirectAsync(companyId, $"Payslip {run.PeriodLabel} {employee.EmployeeNumber}", MessageChannel.Email, [employee.Email], subject, body,
                new MessageAttachment($"Payslip-{run.PeriodLabel}-{employee.EmployeeNumber}.pdf", "application/pdf", pdf.Value), ct).ConfigureAwait(false);
            if (batch.IsFailure)
            {
                return batch.Error;
            }

            queued++;
        }

        return new PayslipEmailRun(queued, noEmail, noPassword);
    }

    private async Task<Result<byte[]>> PdfAsync(Company company, Employee employee, string period, CancellationToken ct)
    {
        string? password = company.PayslipPasswordRule switch
        {
            PayslipPasswordRule.NationalId => Normalise(employee.NationalIdNumber),
            PayslipPasswordRule.DateOfBirth => employee.DateOfBirth?.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
            PayslipPasswordRule.EmployeeNumber => employee.EmployeeNumber,
            _ => null,
        };
        if (company.PayslipPasswordRule != PayslipPasswordRule.None && string.IsNullOrEmpty(password))
        {
            return Error.Validation("Payroll.NoPassword", $"{employee.EmployeeNumber} has no {company.PayslipPasswordRule} to protect the payslip with.");
        }

        string? text = await StoredPayslips.TextAsync(_db, employee, period, ct).ConfigureAwait(false);
        return text is null ? Error.NotFound("Ess.NoPayslip", $"No payslip for {period}.") : PdfTextDocument.Build(text, password);
    }

    // National IDs are typed with and without dashes and spaces; the password is the letters and digits only, upper case.
    private static string? Normalise(string? id) => id is null ? null : new string([.. id.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static string PasswordHint(PayslipPasswordRule rule) => rule switch
    {
        PayslipPasswordRule.NationalId => "Open it with your national ID number (letters and digits only).",
        PayslipPasswordRule.DateOfBirth => "Open it with your date of birth as YYYYMMDD.",
        PayslipPasswordRule.EmployeeNumber => "Open it with your employee number.",
        _ => string.Empty,
    };
}

/// <summary>A committed payslip for a period, rebuilt from what was saved (every run of the period together).</summary>
internal static class StoredPayslips
{
    /// <summary>The payslip text, or null when the employee was not paid in the period.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="employee">The employee.</param>
    /// <param name="period">The period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The text.</returns>
    public static async Task<string?> TextAsync(One4AllDbContext db, Employee employee, string period, CancellationToken ct)
    {
        List<Payslip> slips = await db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == employee.CompanyId && p.EmployeeId == employee.Id && p.PeriodLabel == period)
            .ToListAsync(ct).ConfigureAwait(false);
        if (slips.Count == 0)
        {
            return null;
        }

        List<PayslipLine> lines = [.. slips.SelectMany(p => p.Lines)];
        IReadOnlyList<PayslipItem> Items(PayElementType type) => [.. lines.Where(l => l.Type == type).GroupBy(l => l.ElementCode).Select(g => new PayslipItem(g.First().Description, g.Sum(l => l.Amount)))
            .Where(i => i.Amount != 0m)];
        string company = await db.Companies.Where(c => c.Id == employee.CompanyId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        return PayslipFormatter.ToPlainText(new PayslipDocument
        {
            CompanyName = company, EmployeeName = employee.FullName, EmployeeNumber = employee.EmployeeNumber, PeriodLabel = period, PayDate = slips.Min(p => p.PayDate),
            Earnings = Items(PayElementType.Earning), Deductions = Items(PayElementType.Deduction), GrossEarnings = slips.Sum(p => p.GrossEarnings), TotalDeductions = slips.Sum(p => p.TotalDeductions),
            NetPay = slips.Sum(p => p.NetPay),
        });
    }
}
