using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.JobCosting;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Period-end payroll accounting: salary accruals and labour capitalised into contract WIP (brief §4.15.9).</summary>
public sealed class PayrollAccountingService : IPayrollAccountingService
{
    private readonly One4AllDbContext _db;
    private readonly IAccrualService _accruals;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="accruals">The accrual service (posts and auto-reverses).</param>
    /// <param name="posting">The central posting engine.</param>
    public PayrollAccountingService(One4AllDbContext db, IAccrualService accruals, IPostingEngine posting)
    {
        _db = db;
        _accruals = accruals;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<SalaryAccrual>> AccrueSalariesAsync(long companyId, DateOnly asAt, string expenseAccountCode, string accrualAccountCode, string documentNumber, CancellationToken ct = default)
    {
        DateOnly monthStart = new(asAt.Year, asAt.Month, 1);
        List<Employee> employed = [.. (await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.EngagementDate <= asAt).ToListAsync(ct).ConfigureAwait(false))
            .Where(e => e.Status != EmployeeStatus.Terminated && !(e.TerminationDate is DateOnly left && left < asAt))];
        List<long> ids = [.. employed.Select(e => e.Id)];
        HashSet<long> paid = [.. await _db.Payslips.Where(p => p.CompanyId == companyId && ids.Contains(p.EmployeeId) && p.PayDate >= monthStart && p.PayDate <= asAt)
            .Select(p => p.EmployeeId).Distinct().ToListAsync(ct).ConfigureAwait(false)];
        List<Employee> owed = [.. employed.Where(e => !paid.Contains(e.Id) && e.BasicSalary > 0m)];
        if (owed.Count == 0)
        {
            return Error.Rule("Payroll.NothingToAccrue", $"Everyone employed at {asAt:yyyy-MM-dd} has been paid for the month.");
        }

        decimal amount = owed.Sum(e => e.BasicSalary);
        Result<ReversingJournalResult> posted = await _accruals.PostAccrualAsync(companyId, asAt, monthStart.AddMonths(1), expenseAccountCode, accrualAccountCode, amount, documentNumber,
            $"Salaries accrued for {owed.Count} employee(s) not yet paid for {asAt:yyyy-MM}", ct).ConfigureAwait(false);
        return posted.IsFailure ? posted.Error : new SalaryAccrual(owed.Count, amount, posted.Value.PostingJournalNumber, posted.Value.ReversalJournalNumber);
    }

    /// <inheritdoc />
    public Task<Result<LabourCapitalised>> CapitaliseTimesheetsAsync(long companyId, long contractId, DateOnly upTo, string wipAccountCode, string labourRecoveryAccountCode, string documentNumber,
        CancellationToken ct = default)
        => _db.AtomicAsync(() => CapitaliseCoreAsync(companyId, contractId, upTo, wipAccountCode, labourRecoveryAccountCode, documentNumber, ct), ct);

    private async Task<Result<LabourCapitalised>> CapitaliseCoreAsync(long companyId, long contractId, DateOnly upTo, string wipAccountCode, string labourRecoveryAccountCode, string documentNumber,
        CancellationToken ct)
    {
        string? contract = await _db.JobContracts.Where(c => c.Id == contractId && c.CompanyId == companyId).Select(c => c.ContractNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        List<JobTimesheet> sheets = await _db.JobTimesheets.Where(t => t.CompanyId == companyId && t.JobContractId == contractId && t.Date <= upTo && t.CapitalisedJournal == null)
            .ToListAsync(ct).ConfigureAwait(false);
        decimal amount = sheets.Sum(t => t.Cost);
        if (amount <= 0m)
        {
            return Error.Rule("Payroll.NothingToCapitalise", $"Contract {contract} has no labour to capitalise up to {upTo:yyyy-MM-dd}.");
        }

        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = upTo, TransactionCurrency = company.BaseCurrency, DocumentType = "LABOURWIP", DocumentNumber = documentNumber,
            Narrative = $"Labour capitalised to {contract} WIP ({sheets.Sum(t => t.Hours):0.##} h)",
            Lines =
            [
                new JournalLineRequest { AccountCode = wipAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = labourRecoveryAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        foreach (JobTimesheet sheet in sheets)
        {
            sheet.MarkCapitalised(posted.Value.JournalNumber);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LabourCapitalised(sheets.Count, sheets.Sum(t => t.Hours), amount, posted.Value.JournalNumber);
    }
}
