using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Termination and final pay: worked out, posted in one journal, the employee terminated, a clearance checklist and a certificate of service.</summary>
public sealed class TerminationService : ITerminationService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">Posts the final pay.</param>
    public TerminationService(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<Result<FinalPayStatement>> PreviewAsync(TerminationRequest request, CancellationToken ct = default)
    {
        Result<(Employee Employee, FinalPayStatement Statement, List<EmployeeLoan> Loans)> worked = await WorkOutAsync(request, ct).ConfigureAwait(false);
        return worked.IsFailure ? worked.Error : worked.Value.Statement;
    }

    /// <inheritdoc />
    public Task<Result<TerminationResult>> TerminateAsync(TerminationRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => TerminateCoreAsync(request, ct), ct);

    private async Task<Result<TerminationResult>> TerminateCoreAsync(TerminationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation("Payroll.TerminationReason", "A termination needs its reason.");
        }

        if (new[] { request.SalaryExpenseAccount, request.TerminalBenefitsAccount, request.PayeLiabilityAccount, request.NetPayAccount }.Any(string.IsNullOrWhiteSpace))
        {
            return Error.Validation("Payroll.TerminationAccounts", "The final pay needs the salary, terminal benefits, PAYE and net pay accounts.");
        }

        Result<(Employee Employee, FinalPayStatement Statement, List<EmployeeLoan> Loans)> worked = await WorkOutAsync(request, ct).ConfigureAwait(false);
        if (worked.IsFailure)
        {
            return worked.Error;
        }

        (Employee employee, FinalPayStatement statement, List<EmployeeLoan> loans) = worked.Value;

        // The final pay journal: pay to its expense, tax and loans recovered to their accounts, the net owed to the leaver.
        List<JournalLineRequest> lines = [];
        void Line(string account, JournalSide side, decimal amount, string narrative)
        {
            if (amount > 0m)
            {
                lines.Add(new JournalLineRequest { AccountCode = account, Side = side, TransactionAmount = amount, Department = employee.Department, CostCentre = employee.CostCentre, Narrative = narrative });
            }
        }

        Line(request.SalaryExpenseAccount, JournalSide.Debit, statement.Ordinary, "Final salary, notice and leave");
        Line(request.TerminalBenefitsAccount, JournalSide.Debit, statement.Terminal, "Gratuity and severance");
        Line(request.PayeLiabilityAccount, JournalSide.Credit, statement.OrdinaryTax + statement.TerminalTax, "PAYE on final pay");
        decimal toRecover = statement.Recoveries;
        foreach (EmployeeLoan loan in loans.OrderBy(l => l.GrantedOn))
        {
            decimal recovered = Math.Min(loan.Outstanding, toRecover);
            if (recovered <= 0m)
            {
                continue;
            }

            Line(loan.LoanReceivableAccountCode, JournalSide.Credit, recovered, $"Loan {loan.Reference} recovered");
            loan.Repay(recovered);
            toRecover -= recovered;
        }

        if (statement.OtherRecoveries > 0m)
        {
            if (string.IsNullOrWhiteSpace(request.RecoveriesAccount))
            {
                return Error.Validation("Payroll.RecoveriesAccount", "Property or overpayments are recovered from the final pay: give the account they are credited to.");
            }

            Line(request.RecoveriesAccount.Trim(), JournalSide.Credit, statement.OtherRecoveries, "Property and overpayments recovered");
        }

        Line(request.NetPayAccount, JournalSide.Credit, statement.Net, "Net final pay");
        string? journal = null;
        if (lines.Count > 0)
        {
            Core.System.Company company = await _db.Companies.SingleAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
            Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = request.CompanyId, SourceModule = "GL", PostingDate = request.PayDate ?? request.LastDay, TransactionCurrency = company.BaseCurrency, DocumentType = "FinalPay",
                DocumentNumber = employee.EmployeeNumber, Narrative = $"Final pay {employee.EmployeeNumber} {employee.FullName} ({request.Type})", Lines = lines,
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            journal = posted.Value.JournalNumber;
        }

        employee.Terminate(request.LastDay, $"{request.Type}: {request.Reason.Trim()}");
        Termination termination = new(request.CompanyId, employee.Id, (int)request.Type, request.LastDay, request.Reason.Trim())
        {
            ApprovedByUserId = request.ApprovedByUserId, JournalNumber = journal, YearsOfService = statement.YearsOfService, ProRataSalary = statement.ProRataSalary, Notice = statement.Notice,
            LeaveDays = statement.LeaveDays, Leave = statement.Leave, Gratuity = statement.Gratuity, Severance = statement.Severance, OrdinaryTax = statement.OrdinaryTax,
            TerminalTax = statement.TerminalTax, Recoveries = statement.Recoveries, UnrecoveredLoans = statement.UnrecoveredLoans, NetPay = statement.Net,
            NetPayAccountCode = request.NetPayAccount.Trim(),
        };
        termination.AddClearance("Final pay calculated and accrued" + (journal is null ? " (nothing due)" : $" ({journal}); paid once the checklist is cleared"), cleared: true);
        termination.AddClearance(statement.UnrecoveredLoans > 0m ? $"Loans still owed: {statement.UnrecoveredLoans:N2} — arrange repayment" : "Staff loans settled", statement.UnrecoveredLoans == 0m);
        termination.AddClearance("Company property returned (keys, ID card, equipment, uniform)", cleared: false);
        foreach (EmployeeAssetIssue issued in await _db.Set<EmployeeAssetIssue>().AsNoTracking().Where(a => a.EmployeeId == employee.Id && a.ReturnedOn == null)
            .OrderBy(a => a.IssuedOn).ToListAsync(ct).ConfigureAwait(false))
        {
            termination.AddClearance($"Return {issued.Description}{(issued.SerialNumber is null ? string.Empty : $" ({issued.SerialNumber})")}"
                + (issued.Value > 0m ? $", worth {issued.Value:N2}" : string.Empty), cleared: false);
        }

        termination.AddClearance("System access withdrawn", cleared: employee.UserId is null);
        if (employee.HousingTenancyId is not null)
        {
            termination.AddClearance("Staff housing vacated and the tenancy ended", cleared: false);
        }

        _db.Set<Termination>().Add(termination);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new TerminationResult(termination.Id, journal, statement, Items(termination));
    }

    /// <inheritdoc />
    public async Task<Result> ClearItemAsync(long companyId, long itemId, long clearedByUserId, DateOnly date, CancellationToken ct = default)
    {
        TerminationClearanceItem? item = await (from i in _db.Set<TerminationClearanceItem>()
                                                join t in _db.Set<Termination>() on i.TerminationId equals t.Id
                                                where i.Id == itemId && t.CompanyId == companyId
                                                select i).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Payroll.ClearanceNotFound", $"Clearance item {itemId} not found."));
        }

        item.Clear(clearedByUserId, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ClearanceItem>>> ClearanceAsync(long companyId, string employeeNumber, CancellationToken ct = default)
    {
        Termination? termination = await LatestAsync(companyId, employeeNumber, ct).ConfigureAwait(false);
        return termination is null ? NotTerminated(employeeNumber) : Result<IReadOnlyList<ClearanceItem>>.Success(Items(termination));
    }

    /// <inheritdoc />
    public async Task<Result<string>> CertificateOfServiceAsync(long companyId, string employeeNumber, CancellationToken ct = default)
    {
        Termination? termination = await LatestAsync(companyId, employeeNumber, ct).ConfigureAwait(false);
        if (termination is null)
        {
            return NotTerminated(employeeNumber);
        }

        Employee employee = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == termination.EmployeeId, ct).ConfigureAwait(false);
        string company = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).SingleAsync(ct).ConfigureAwait(false);
        TerminationType type = (TerminationType)termination.Type;
        StringBuilder sb = new();
        sb.AppendLine(company);
        sb.AppendLine("CERTIFICATE OF SERVICE");
        sb.AppendLine(new string('-', 48));
        sb.AppendLine(CultureInfo.InvariantCulture, $"This certifies that {employee.FullName} (employee {employee.EmployeeNumber}{(employee.NationalIdNumber is null ? string.Empty : $", ID {employee.NationalIdNumber}")})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"was employed from {employee.EngagementDate:d MMMM yyyy} to {termination.LastDay:d MMMM yyyy} ({termination.YearsOfService} completed year(s)),");
        sb.AppendLine(CultureInfo.InvariantCulture, $"last as {(employee.Grade is null ? "an employee" : $"grade {employee.Grade}")}{(employee.Department is null ? string.Empty : $" in {employee.Department}")}.");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Employment ended by {type switch { TerminationType.EndOfContract => "end of contract", _ => type.ToString().ToLowerInvariant() }}.");
        sb.AppendLine();
        sb.AppendLine("______________________________");
        sb.AppendLine("For the employer");
        return sb.ToString();
    }

    // The leaver, the statement and their open loans.
    private async Task<Result<(Employee, FinalPayStatement, List<EmployeeLoan>)>> WorkOutAsync(TerminationRequest request, CancellationToken ct)
    {
        string number = (request.EmployeeNumber ?? string.Empty).Trim().ToUpperInvariant();
        Employee? employee = await _db.Employees.FirstOrDefaultAsync(e => e.CompanyId == request.CompanyId && e.EmployeeNumber.ToUpper() == number, ct).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound("Payroll.EmployeeNotFound", $"Employee {number} not found.");
        }

        if (employee.Status == EmployeeStatus.Terminated)
        {
            return Error.Rule("Payroll.AlreadyTerminated", $"Employee {number} has already left.");
        }

        if (request.LastDay < employee.EngagementDate)
        {
            return Error.Validation("Payroll.LastDay", "The last day cannot be before the engagement date.");
        }

        string period = request.LastDay.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        bool lastMonthPaid = await _db.Payslips.AnyAsync(p => p.CompanyId == request.CompanyId && p.EmployeeId == employee.Id && p.PeriodLabel == period, ct).ConfigureAwait(false);
        decimal leaveDays = await _db.EmployeeLeaveBalances.Where(b => b.CompanyId == request.CompanyId && b.EmployeeId == employee.Id).SumAsync(b => b.AccruedDays - b.TakenDays, ct).ConfigureAwait(false);
        List<EmployeeLoan> loans = await _db.EmployeeLoans.Where(l => l.CompanyId == request.CompanyId && l.EmployeeId == employee.Id && l.Outstanding > 0m).ToListAsync(ct).ConfigureAwait(false);
        string tableCode = request.TaxTableCode.Trim().ToUpperInvariant();
        DateOnly payDate = request.PayDate ?? request.LastDay;
        TaxTable? table = (await _db.Set<TaxTable>().Include(t => t.Bands).Where(t => t.CompanyId == request.CompanyId && t.Code == tableCode && t.Currency == null && t.EffectiveFrom <= payDate)
            .ToListAsync(ct).ConfigureAwait(false)).OrderBy(t => t.EffectiveFrom).LastOrDefault();
        decimal Tax(decimal ordinary) => table is null ? 0m : PayeTable.Tax(table.Bands.Select(b => new PayeBand(b.From, b.To, b.RatePercent)), ordinary);
        // Company property still out with the leaver is recovered at its recorded value, with any overpayment.
        decimal unreturned = await _db.Set<EmployeeAssetIssue>().Where(a => a.EmployeeId == employee.Id && a.ReturnedOn == null).SumAsync(a => (decimal?)a.Value, ct).ConfigureAwait(false) ?? 0m;
        FinalPayStatement statement = FinalPay.Calculate(request.Type, employee.BasicSalary, employee.EngagementDate, request.LastDay, lastMonthPaid, leaveDays, loans.Sum(l => l.Outstanding),
            request.Policy, Tax, request.AnnualBonus, unreturned + Math.Max(0m, request.Overpayment));
        return (employee, statement, loans);
    }

    /// <inheritdoc />
    public Task<Result<string?>> ReleaseFinalPayAsync(long companyId, string employeeNumber, DateOnly date, string bankAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Termination? termination = await LatestAsync(companyId, employeeNumber, ct).ConfigureAwait(false);
            if (termination is null)
            {
                return NotTerminated(employeeNumber);
            }

            if (termination.PaidOn is DateOnly paid)
            {
                return Error.Conflict("Payroll.FinalPayReleased", $"The final pay of {employeeNumber} was already released on {paid:yyyy-MM-dd}.");
            }

            // The final payment is held until the leaver has cleared: property returned, loans arranged, access withdrawn, housing vacated.
            List<string> outstanding = [.. termination.Clearance.Where(i => !i.IsCleared).Select(i => i.Description)];
            if (outstanding.Count > 0)
            {
                return Error.Rule("Payroll.ClearanceOutstanding", $"The final pay is held until the clearance is complete; still to clear: {string.Join("; ", outstanding)}.");
            }

            string? journal = null;
            if (termination.NetPay > 0m)
            {
                if (string.IsNullOrWhiteSpace(bankAccountCode) || string.IsNullOrWhiteSpace(termination.NetPayAccountCode))
                {
                    return Error.Validation("Payroll.FinalPayAccounts", "Give the bank account the final pay is paid from.");
                }

                Employee employee = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == termination.EmployeeId, ct).ConfigureAwait(false);
                Core.System.Company company = await _db.Companies.SingleAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
                Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
                {
                    CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = company.BaseCurrency, DocumentType = "FinalPayment",
                    DocumentNumber = employee.EmployeeNumber, Narrative = $"Final pay released to {employee.EmployeeNumber} {employee.FullName}",
                    Lines =
                    [
                        new JournalLineRequest { AccountCode = termination.NetPayAccountCode, Side = JournalSide.Debit, TransactionAmount = termination.NetPay, Narrative = "Net final pay" },
                        new JournalLineRequest { AccountCode = bankAccountCode.Trim(), Side = JournalSide.Credit, TransactionAmount = termination.NetPay, Narrative = "Final pay paid" },
                    ],
                }, ct).ConfigureAwait(false);
                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                journal = posted.Value.JournalNumber;
            }

            termination.PaidOn = date;
            termination.PaymentJournalNumber = journal;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<string?>.Success(journal);
        }, ct);

    private Task<Termination?> LatestAsync(long companyId, string employeeNumber, CancellationToken ct)
    {
        string number = (employeeNumber ?? string.Empty).Trim().ToUpperInvariant();
        return (from t in _db.Set<Termination>().Include(t => t.Clearance)
                join e in _db.Employees on t.EmployeeId equals e.Id
                where t.CompanyId == companyId && e.EmployeeNumber.ToUpper() == number
                orderby t.Id descending
                select t).FirstOrDefaultAsync(ct);
    }

    private static List<ClearanceItem> Items(Termination termination)
        => [.. termination.Clearance.OrderBy(i => i.Id).Select(i => new ClearanceItem(i.Id, i.Description, i.IsCleared, i.ClearedByUserId, i.ClearedOn))];

    private static Error NotTerminated(string number) => Error.NotFound("Payroll.NotTerminated", $"Employee {number} has no termination on record.");
}
