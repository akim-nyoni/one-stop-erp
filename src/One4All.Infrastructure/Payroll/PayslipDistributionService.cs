using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Documents;
using One4All.Application.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>
/// Payslips as password-protected PDFs (brief §4.15.7): the committed payslip for a period (every run of it together) written as a
/// PDF opened by the password the company's rule gives - national ID, date of birth or employee number - and queued by e-mail to each
/// employee of a run through the messaging service.
/// </summary>
public sealed class PayslipDistributionService : IPayslipDistributionService
{
    private readonly One4AllDbContext _db;
    private readonly INotificationService _notifications;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="notifications">The messaging service the e-mails are queued through.</param>
    public PayslipDistributionService(One4AllDbContext db, INotificationService notifications, Application.Abstractions.Security.IAuditService? audit = null,
        Application.Abstractions.Security.ICurrentUser? user = null, Application.Abstractions.Reporting.IReportRunner? reports = null)
    {
        _audit = audit;
        _user = user;
        _db = db;
        _notifications = notifications;
        _reports = reports;
    }

    /// <inheritdoc />
    private readonly Application.Abstractions.Security.IAuditService? _audit;
    private readonly Application.Abstractions.Reporting.IReportRunner? _reports;
    private readonly Application.Abstractions.Security.ICurrentUser? _user;

    // Every export of personal or payroll data is recorded (brief §4.15.10: a payroll export is a data-breach risk).
    private async Task RecordExportAsync(long companyId, string module, string entityType, string? entityId, string details, CancellationToken ct)
    {
        if (_audit is not null)
        {
            await _audit.RecordAsync(new Application.Abstractions.Security.AuditEvent(companyId, _user?.UserId, _user?.UserName ?? "system", module, entityType, entityId,
                Core.Audit.AuditAction.Export, Details: details, Terminal: _user?.Terminal), ct).ConfigureAwait(false);
        }
    }

    public async Task<Result<byte[]>> PayslipPdfAsync(long companyId, long employeeId, string periodLabel, CancellationToken ct = default)
    {
        Result<byte[]> pdf = await PayslipPdfCoreAsync(companyId, employeeId, periodLabel, ct).ConfigureAwait(false);
        if (pdf.IsSuccess)
        {
            await RecordExportAsync(companyId, "Payroll", "PayslipPdf", $"{employeeId}/{periodLabel}", $"{pdf.Value.Length} bytes", ct).ConfigureAwait(false);
        }

        return pdf;
    }

    private async Task<Result<byte[]>> PayslipPdfCoreAsync(long companyId, long employeeId, string periodLabel, CancellationToken ct)
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
            PayslipPasswordRule.EmployeeSet => employee.PayslipPassword,
            _ => null,
        };
        if (company.PayslipPasswordRule != PayslipPasswordRule.None && string.IsNullOrEmpty(password))
        {
            return Error.Validation("Payroll.NoPassword", $"{employee.EmployeeNumber} has no {company.PayslipPasswordRule} to protect the payslip with.");
        }

        string? text = await StoredPayslips.TextAsync(_db, employee, period, ct).ConfigureAwait(false);
        if (text is null)
        {
            return Error.NotFound("Ess.NoPayslip", $"No payslip for {period}.");
        }

        if (!string.IsNullOrWhiteSpace(company.PayslipLayoutCode) && _reports is not null)
        {
            // The company's own design: the layout report runs for this employee and the period's pay dates, and its pages become the PDF.
            List<DateOnly> payDates = await _db.Payslips.AsNoTracking().Where(p => p.CompanyId == company.Id && p.EmployeeId == employee.Id && p.PeriodLabel == period)
                .Select(p => p.PayDate).ToListAsync(ct).ConfigureAwait(false);
            Dictionary<string, object?> parameters = new(StringComparer.OrdinalIgnoreCase)
            {
                ["FromDate"] = payDates.Min(), ["ToDate"] = payDates.Max(), ["EmployeeNumber"] = employee.EmployeeNumber, ["Period"] = period,
            };
            Result<Application.Abstractions.Reporting.RenderedPages> pages = await _reports.RenderPageImagesAsync(company.Id, company.PayslipLayoutCode.Trim(), parameters, 150, ct).ConfigureAwait(false);
            if (pages.IsFailure)
            {
                return pages.Error;
            }

            return PdfTextDocument.BuildFromImages(pages.Value.Jpegs, pages.Value.WidthPoints, password);
        }

        return PdfTextDocument.Build(text, password);
    }

    // National IDs are typed with and without dashes and spaces; the password is the letters and digits only, upper case.
    private static string? Normalise(string? id) => id is null ? null : new string([.. id.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static string PasswordHint(PayslipPasswordRule rule) => rule switch
    {
        PayslipPasswordRule.NationalId => "Open it with your national ID number (letters and digits only).",
        PayslipPasswordRule.DateOfBirth => "Open it with your date of birth as YYYYMMDD.",
        PayslipPasswordRule.EmployeeNumber => "Open it with your employee number.",
        PayslipPasswordRule.EmployeeSet => "Open it with the payslip password you set (self-service, or ask HR).",
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
        var company = await db.Companies.Where(c => c.Id == employee.CompanyId).Select(c => new { c.Name, c.TaxYearStartMonth }).FirstAsync(ct).ConfigureAwait(false);
        DateOnly payDate = slips.Max(p => p.PayDate);

        // The tax elements (tax-table deductions), the tax year so far, leave and loans.
        HashSet<string> taxCodes = [.. await db.PayElements.AsNoTracking().Where(e => e.CompanyId == employee.CompanyId && e.Basis == CalculationBasis.TaxTable).Select(e => e.Code).ToListAsync(ct)
            .ConfigureAwait(false)];
        DateOnly yearStart = PayPeriods.TaxYearStart(payDate, company.TaxYearStartMonth);
        List<Payslip> year = await db.Payslips.AsNoTracking().Include(p => p.Lines)
            .Where(p => p.CompanyId == employee.CompanyId && p.EmployeeId == employee.Id && p.PayDate >= yearStart && p.PayDate <= payDate).ToListAsync(ct).ConfigureAwait(false);
        static decimal Taxable(IEnumerable<PayslipLine> ls) => ls.Where(l => l.Taxable && l.Type is PayElementType.Earning or PayElementType.Memo).Sum(l => l.Amount);
        decimal Tax(IEnumerable<PayslipLine> ls) => ls.Where(l => taxCodes.Contains(l.ElementCode)).Sum(l => l.Amount);
        List<(string, decimal)> leave = [.. (await db.Set<EmployeeLeaveBalance>().AsNoTracking().Where(b => b.EmployeeId == employee.Id).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(b => b.LeaveType, StringComparer.Ordinal).Select(b => (b.LeaveType, b.BalanceDays))];
        List<(string, decimal)> loans = [.. (await db.EmployeeLoans.AsNoTracking().Where(l => l.EmployeeId == employee.Id && l.Outstanding > 0m).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(l => l.Reference, StringComparer.Ordinal).Select(l => (l.Reference, l.Outstanding))];
        // Net pay by currency: earnings paid in another currency go out in it at the pay date's rate; the rest in the base currency.
        Dictionary<string, CurrencyCode> paidIn = await db.PayElements.AsNoTracking().Where(e => e.CompanyId == employee.CompanyId && e.PaidInCurrency != null)
            .ToDictionaryAsync(e => e.Code, e => e.PaidInCurrency!.Value, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);
        CurrencyCode baseCurrency = await db.Companies.Where(c => c.Id == employee.CompanyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        // Splits paid in another currency then move their share of what is left into it, as the settlement and the bank file do.
        List<(string Currency, decimal Amount, decimal BaseAmount, decimal Rate)> byCurrency = [];
        decimal net = slips.Sum(p => p.NetPay);
        decimal netLeft = net;
        Dictionary<CurrencyCode, decimal> foreignBase = [];
        foreach (IGrouping<CurrencyCode, PayslipLine> foreign in lines.Where(l => l.Type == PayElementType.Earning && paidIn.TryGetValue(l.ElementCode, out CurrencyCode c) && c != baseCurrency)
            .GroupBy(l => paidIn[l.ElementCode]))
        {
            decimal portion = Math.Max(Math.Min(foreign.Sum(l => l.Amount), netLeft), 0m);
            foreignBase[foreign.Key] = foreignBase.GetValueOrDefault(foreign.Key) + portion;
            netLeft -= portion;
        }

        List<EmployeePaymentSplit> splits = await db.PaymentSplits.AsNoTracking().Where(s => s.CompanyId == employee.CompanyId && s.EmployeeId == employee.Id).OrderBy(s => s.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach ((EmployeePaymentSplit split, decimal amount) in EmployeePaymentSplit.Allocate(splits, net, netLeft).Where(x => x.Amount > 0m && x.Split.Currency is { } c && c != baseCurrency).ToList())
        {
            foreignBase[split.Currency!.Value] = foreignBase.GetValueOrDefault(split.Currency!.Value) + amount;
            netLeft -= amount;
        }

        foreach ((CurrencyCode foreign, decimal portion) in foreignBase.Where(f => f.Value > 0m).OrderBy(f => f.Key.Value, StringComparer.Ordinal))
        {
            decimal? rate = await Accounting.ExchangeRateResolver.FindAsync(db, foreign, baseCurrency, ExchangeRateType.Daily, payDate, ct).ConfigureAwait(false);
            if (rate is > 0m)
            {
                byCurrency.Add((foreign.Value, decimal.Round(portion / rate.Value, 2, MidpointRounding.AwayFromZero), portion, rate.Value));
            }
            else
            {
                netLeft += portion;
            }
        }

        if (byCurrency.Count > 0)
        {
            byCurrency.Insert(0, (baseCurrency.Value, netLeft, netLeft, 1m));
        }

        string? paidBy = employee.PaymentMethod switch
        {
            PayMethod.BankTransfer when employee.BankAccountNumber is { Length: > 0 } account => $"Paid by bank transfer to {employee.BankName} …{(account.Length > 4 ? account[^4..] : account)}",
            PayMethod.MobileMoney when employee.MobileMoneyNumber is { Length: > 0 } wallet => $"Paid to mobile wallet …{(wallet.Length > 4 ? wallet[^4..] : wallet)}",
            PayMethod.Cash => "Paid in cash",
            PayMethod.Cheque => "Paid by cheque",
            _ => null,
        };

        return PayslipFormatter.ToPlainText(new PayslipDocument
        {
            CompanyName = company.Name, EmployeeName = employee.FullName, EmployeeNumber = employee.EmployeeNumber, PeriodLabel = period, PayDate = slips.Min(p => p.PayDate),
            Earnings = Items(PayElementType.Earning), Deductions = Items(PayElementType.Deduction), GrossEarnings = slips.Sum(p => p.GrossEarnings), TotalDeductions = slips.Sum(p => p.TotalDeductions),
            NetPay = slips.Sum(p => p.NetPay), EmployerContributions = Items(PayElementType.EmployerContribution), MemoItems = Items(PayElementType.Memo), PaidBy = paidBy,
            TaxablePay = Taxable(lines), TaxDeducted = Tax(lines),
            YearToDate = (year.Sum(p => p.GrossEarnings), Taxable(year.SelectMany(p => p.Lines)), Tax(year.SelectMany(p => p.Lines)), year.Sum(p => p.NetPay)),
            LeaveBalances = leave, LoanBalances = loans, NetPayByCurrency = byCurrency,
        });
    }
}
