using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <content>
/// Corrections after commit (brief §4.15.6): supplementary runs, formally approved reversals and backpay - each a visible run with
/// its own payslips and journal, never an edit - and the legacy import for the go-live parallel run.
/// </content>
public sealed partial class PayrollService
{
    /// <inheritdoc />
    public Task<Result<PayrollRunResult>> RunSupplementaryAsync(SupplementaryRunRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunSupplementaryCoreAsync(request, ct), ct);

    /// <inheritdoc />
    public Task<Result<PayrollRunResult>> ReverseRunAsync(long companyId, string runNumber, DateOnly date, string reason, long approvedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReverseRunCoreAsync(companyId, runNumber, date, reason, approvedByUserId, ct), ct);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BackpayPreviewLine>>> CalculateBackpayAsync(long companyId, long employeeId, decimal newBasicSalary, DateOnly effectiveFrom, CancellationToken ct = default)
    {
        if (newBasicSalary < 0m)
        {
            return Error.Validation("Payroll.BasicNegative", "The new basic salary cannot be negative.");
        }

        Employee? employee = await _db.Set<Employee>().Include(e => e.PayElements).FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound("Payroll.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        // Every period paid on or after the effective date whose regular pay still stands (backpay runs are counted through their lines below).
        List<Payslip> slips = await _db.Set<Payslip>().AsNoTracking().Include(p => p.Lines)
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId && p.PayDate >= effectiveFrom && p.RunKind != PayRunKind.Backpay)
            .ToListAsync(ct).ConfigureAwait(false);
        List<IGrouping<string, Payslip>> periods = [.. slips.GroupBy(p => p.PeriodLabel)
            .Where(g => g.Count(p => p.RunKind == PayRunKind.Regular) > g.Count(p => p.RunKind == PayRunKind.Reversal))
            .OrderBy(g => g.Min(p => p.PayDate))];
        List<string> labels = [.. periods.Select(g => g.Key)];
        List<BackpayLine> earlier = await _db.Set<BackpayLine>().AsNoTracking().Where(b => b.CompanyId == companyId && b.EmployeeId == employeeId && labels.Contains(b.ForPeriod))
            .ToListAsync(ct).ConfigureAwait(false);

        // Recoveries (staff purchases, rent) follow balances, not pay, so backpay leaves them alone.
        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);
        HashSet<string> recoveryCodes = new(elements.Values.Where(e => e.Recovers != PayRecovery.None).Select(e => e.Code), StringComparer.OrdinalIgnoreCase);
        List<BackpayPreviewLine> lines = [];
        foreach (IGrouping<string, Payslip> period in periods)
        {
            Dictionary<long, Dictionary<string, PayrollVariable>> variables = await VariablesAsync(companyId, period.Key, [employee], ct).ConfigureAwait(false);
            Dictionary<long, Dictionary<string, object?>> context = await RecoveryVariablesAsync(companyId, [employee], elements, period.Min(p => p.PayDate), ct).ConfigureAwait(false);
            Result<Payslip> due = BuildPayslip(companyId, employee, elements, period.Key, period.Min(p => p.PayDate), newBasicSalary, recoveries: context[employee.Id], variables: variables[employee.Id]);
            if (due.IsFailure)
            {
                return due.Error;
            }

            Dictionary<string, decimal> paid = new(StringComparer.OrdinalIgnoreCase);
            foreach (PayslipLine line in period.SelectMany(p => p.Lines))
            {
                paid[line.ElementCode] = paid.GetValueOrDefault(line.ElementCode) + line.Amount;
            }

            foreach (BackpayLine settled in earlier.Where(b => b.ForPeriod == period.Key))
            {
                paid[settled.ElementCode] = paid.GetValueOrDefault(settled.ElementCode) + settled.Difference;
            }

            Dictionary<string, decimal> owed = due.Value.Lines.GroupBy(l => l.ElementCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount), StringComparer.OrdinalIgnoreCase);
            foreach (string code in paid.Keys.Union(owed.Keys, StringComparer.OrdinalIgnoreCase).Where(c => !recoveryCodes.Contains(c)).Order(StringComparer.Ordinal))
            {
                BackpayPreviewLine line = new(period.Key, code, paid.GetValueOrDefault(code), owed.GetValueOrDefault(code));
                if (line.Difference != 0m)
                {
                    lines.Add(line);
                }
            }
        }

        return lines;
    }

    /// <inheritdoc />
    public Task<Result<PayrollRunResult>> PayBackpayAsync(BackpayRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => PayBackpayCoreAsync(request, ct), ct);

    /// <inheritdoc />
    public Task<Result<int>> ImportLegacyPayrollAsync(long companyId, string periodLabel, string csv, CancellationToken ct = default)
        => _db.AtomicAsync(() => ImportLegacyCoreAsync(companyId, periodLabel, csv, ct), ct);

    // Where payroll needs approval (brief §4.15.10), a correction is approved by a second user too; and nobody prepares or approves one paying themselves.
    private async Task<Error?> AdjustmentApprovalAsync(Company company, long requestedBy, long? approvedBy, IReadOnlyCollection<long> employeeIds, CancellationToken ct)
    {
        long approver = approvedBy ?? 0;
        if (company.PayrollApprovalRequired)
        {
            if (approver == 0 || !await _db.Set<Core.Security.User>().AnyAsync(u => u.Id == approver, ct).ConfigureAwait(false))
            {
                return Error.Rule("Payroll.ApprovalRequired", "This company's payroll corrections must be approved by a second user.");
            }

            if (approver == requestedBy)
            {
                return Error.Rule("Payroll.SelfApproval", "A payroll correction is approved by someone other than whoever prepared it.");
            }
        }

        foreach (long user in new[] { requestedBy, approver })
        {
            if (await OwnRecordAsync(user, employeeIds, ct).ConfigureAwait(false) is string own)
            {
                return OwnPay(own);
            }
        }

        return null;
    }

    private async Task<Result<PayrollRunResult>> RunSupplementaryCoreAsync(SupplementaryRunRequest request, CancellationToken ct)
    {
        Result<(Company Company, string Period, string Document)> header = await CheckAdjustmentAsync(request.CompanyId, request.PeriodLabel, request.DocumentNumber, request.Reason, ct).ConfigureAwait(false);
        if (header.IsFailure)
        {
            return header.Error;
        }

        (Company company, string period, string document) = header.Value;
        if (request.Lines.Count == 0)
        {
            return Error.Validation("Payroll.NoLines", "A supplementary run needs at least one line.");
        }

        if (!await RegularRunIsLiveAsync(company.Id, period, null, ct).ConfigureAwait(false))
        {
            return Error.Rule("Payroll.PeriodNotRun", $"Period {period} has no committed run to correct.");
        }

        Dictionary<long, PayElement> elements = await LoadElementsAsync(company.Id, ct).ConfigureAwait(false);
        List<long> employeeIds = [.. request.Lines.Select(l => l.EmployeeId).Distinct()];
        if (await AdjustmentApprovalAsync(company, request.RequestedByUserId, request.ApprovedByUserId, employeeIds, ct).ConfigureAwait(false) is Error refused)
        {
            return refused;
        }

        HashSet<long> known = [.. await _db.Set<Employee>().Where(e => e.CompanyId == company.Id && employeeIds.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct).ConfigureAwait(false)];
        List<Payslip> slips = [];
        foreach (IGrouping<long, SupplementaryLine> employeeLines in request.Lines.GroupBy(l => l.EmployeeId))
        {
            if (!known.Contains(employeeLines.Key))
            {
                return Error.NotFound("Payroll.EmployeeNotFound", $"Employee {employeeLines.Key} not found.");
            }

            Payslip slip = new(company.Id, employeeLines.Key, period, request.PayDate, PayRunKind.Supplementary, document);
            List<(PayElement Element, decimal Amount)> resolved = [];
            foreach (SupplementaryLine line in employeeLines)
            {
                PayElement? element = ElementByCode(elements, line.ElementCode);
                if (element is null)
                {
                    return Error.NotFound("Payroll.ElementNotFound", $"Pay element {line.ElementCode} not found.");
                }

                resolved.Add((element, decimal.Round(line.Amount, 2, MidpointRounding.ToEven)));
            }

            foreach ((PayElement element, decimal amount) in resolved.OrderBy(x => x.Element.Sequence))
            {
                slip.AddLine(element.Code, element.Name, element.Type, amount, element.Taxable);
            }

            slips.Add(slip);
        }

        return await PostRunAsync(company, period, request.PayDate, request.NetPayAccountCode, document, slips, elements, PayRunKind.Supplementary, request.Reason.Trim(), string.Empty, ct).ConfigureAwait(false);
    }

    private async Task<Result<PayrollRunResult>> ReverseRunCoreAsync(long companyId, string runNumber, DateOnly date, string reason, long approvedByUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Payroll.ReasonRequired", "A payroll reversal needs a reason.");
        }

        string key = (runNumber ?? string.Empty).Trim();
        PayrollRunRecord? run = await _db.Set<PayrollRunRecord>()
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && key != string.Empty && (r.DocumentNumber == key || r.JournalNumber == key), ct).ConfigureAwait(false);
        if (run is null)
        {
            return Error.NotFound("Payroll.RunNotFound", $"No payroll run '{key}'.");
        }

        if (run.Kind == PayRunKind.Reversal)
        {
            return Error.Rule("Payroll.CannotReverseReversal", "A reversal cannot itself be reversed - run the period again instead.");
        }

        if (run.IsReversed)
        {
            return Error.Conflict("Payroll.AlreadyReversed", $"Run {key} was already reversed by {run.ReversedBy}.");
        }

        if (!await _db.Users.AnyAsync(u => u.Id == approvedByUserId, ct).ConfigureAwait(false))
        {
            return Error.Validation("Payroll.ApproverNotFound", "The reversal must be approved by a user.");
        }

        // Maker-checker: whoever committed the run cannot approve undoing it.
        if (run.CreatedByUserId != 0 && run.CreatedByUserId == approvedByUserId)
        {
            return Error.Rule("Payroll.SameApprover", "The reversal must be approved by someone other than the person who ran the payroll.");
        }

        List<long> paid = await _db.Set<Payslip>().Where(p => p.CompanyId == companyId && p.PeriodLabel == run.PeriodLabel && (p.RunReference == run.DocumentNumber || run.DocumentNumber == string.Empty))
            .Select(p => p.EmployeeId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        if (await OwnRecordAsync(approvedByUserId, paid, ct).ConfigureAwait(false) is string own)
        {
            return OwnPay(own);
        }

        long journalId = await _db.Journals.Where(j => j.CompanyId == companyId && j.JournalNumber == run.JournalNumber).Select(j => j.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (journalId == 0)
        {
            return Error.NotFound("Payroll.JournalNotFound", $"Journal {run.JournalNumber} not found.");
        }

        Result<PostingReceipt> reversed = await _posting.ReverseAsync(companyId, journalId, date, $"Payroll reversal {run.PeriodLabel}: {reason.Trim()}", ct).ConfigureAwait(false);
        if (reversed.IsFailure)
        {
            return reversed.Error;
        }

        IQueryable<Payslip> source = _db.Set<Payslip>().AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId && p.PeriodLabel == run.PeriodLabel);
        List<Payslip> original = run.DocumentNumber.Length > 0
            ? await source.Where(p => p.RunReference == run.DocumentNumber).ToListAsync(ct).ConfigureAwait(false)
            : await source.Where(p => p.RunReference == string.Empty && p.RunKind == run.Kind && p.PayDate == run.PayDate).ToListAsync(ct).ConfigureAwait(false);

        string reversalDocument = "REV-" + (run.DocumentNumber.Length > 0 ? run.DocumentNumber : run.JournalNumber);
        reversalDocument = reversalDocument[..Math.Min(reversalDocument.Length, 40)];
        decimal gross = 0m, deductions = 0m, net = 0m;
        List<Payslip> negatives = [];
        foreach (Payslip slip in original)
        {
            Payslip negative = new(companyId, slip.EmployeeId, slip.PeriodLabel, date, PayRunKind.Reversal, reversalDocument, slip.PayGroup);
            foreach (PayslipLine line in slip.Lines)
            {
                negative.AddLine(line.ElementCode, line.Description, line.Type, -line.Amount, line.Taxable);
            }

            _db.Set<Payslip>().Add(negative);
            negatives.Add(negative);
            gross += negative.GrossEarnings;
            deductions += negative.TotalDeductions;
            net += negative.NetPay;
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result rent = await SettleHousingRentAsync(company, negatives, await LoadElementsAsync(companyId, ct).ConfigureAwait(false), reversalDocument, date, ct).ConfigureAwait(false);
        if (rent.IsFailure)
        {
            return rent.Error;
        }

        await ReverseArrearsAsync(companyId, run.DocumentNumber, reversalDocument, date, ct).ConfigureAwait(false);

        // Loans the run repaid get the repayment back and lose the interest it charged (the journal reversal unwinds the postings).
        List<PayslipLine> loanLines = [.. original.SelectMany(s => s.Lines).Where(l => LoanIdOf(l.ElementCode) is not null)];
        List<long> reversedLoanIds = [.. loanLines.Select(l => LoanIdOf(l.ElementCode)!.Value).Distinct()];
        Dictionary<long, EmployeeLoan> reversedLoans = await _db.EmployeeLoans.Where(l => reversedLoanIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct).ConfigureAwait(false);
        foreach (PayslipLine line in loanLines)
        {
            if (reversedLoans.TryGetValue(LoanIdOf(line.ElementCode)!.Value, out EmployeeLoan? loan))
            {
                if (line.ElementCode.StartsWith(LoanInterestLinePrefix, StringComparison.Ordinal))
                {
                    loan.ChargeInterest(-line.Amount);
                }
                else
                {
                    loan.Restore(line.Amount);
                }
            }
        }
        run.MarkReversed(date, reversalDocument, approvedByUserId);
        _db.Set<PayrollRunRecord>().Add(new PayrollRunRecord(companyId, run.PeriodLabel, date, original.Count, gross, deductions, net, reversed.Value.JournalNumber, PayRunKind.Reversal,
            reversalDocument, reason.Trim(), run.PayGroup));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PayrollRunResult(original.Count, gross, deductions, net, reversed.Value.JournalNumber);
    }

    private async Task<Result<PayrollRunResult>> PayBackpayCoreAsync(BackpayRequest request, CancellationToken ct)
    {
        Result<(Company Company, string Period, string Document)> header = await CheckAdjustmentAsync(request.CompanyId, request.PeriodLabel, request.DocumentNumber, request.Reason, ct).ConfigureAwait(false);
        if (header.IsFailure)
        {
            return header.Error;
        }

        (Company company, string period, string document) = header.Value;
        if (await AdjustmentApprovalAsync(company, request.RequestedByUserId, request.ApprovedByUserId, [request.EmployeeId], ct).ConfigureAwait(false) is Error refused)
        {
            return refused;
        }

        Result<IReadOnlyList<BackpayPreviewLine>> calculated = await CalculateBackpayAsync(company.Id, request.EmployeeId, request.NewBasicSalary, request.EffectiveFrom, ct).ConfigureAwait(false);
        if (calculated.IsFailure)
        {
            return calculated.Error;
        }

        if (calculated.Value.Count == 0)
        {
            return Error.Rule("Payroll.NoBackpay", "Nothing is owed either way for the periods since the effective date.");
        }

        Dictionary<long, PayElement> elements = await LoadElementsAsync(company.Id, ct).ConfigureAwait(false);
        Payslip slip = new(company.Id, request.EmployeeId, period, request.PayDate, PayRunKind.Backpay, document);
        foreach (IGrouping<string, BackpayPreviewLine> code in calculated.Value.GroupBy(l => l.ElementCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => ElementByCode(elements, g.Key)?.Sequence ?? int.MaxValue))
        {
            PayElement? element = ElementByCode(elements, code.Key);
            if (element is null)
            {
                return Error.NotFound("Payroll.ElementNotFound", $"Pay element {code.Key} no longer exists; correct the period with a supplementary run.");
            }

            slip.AddLine(element.Code, element.Name, element.Type, code.Sum(l => l.Difference), element.Taxable);
        }

        Result<PayrollRunResult> run = await PostRunAsync(company, period, request.PayDate, request.NetPayAccountCode, document, [slip], elements, PayRunKind.Backpay, request.Reason.Trim(), string.Empty, ct)
            .ConfigureAwait(false);
        if (run.IsFailure)
        {
            return run.Error;
        }

        foreach (BackpayPreviewLine line in calculated.Value)
        {
            _db.Set<BackpayLine>().Add(new BackpayLine(company.Id, request.EmployeeId, line.ForPeriod, line.ElementCode, line.Paid, line.Due, document, request.PayDate, request.Reason.Trim()));
        }

        Employee employee = await _db.Set<Employee>().FirstAsync(e => e.Id == request.EmployeeId, ct).ConfigureAwait(false);
        employee.BasicSalary = request.NewBasicSalary;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    private async Task<Result<int>> ImportLegacyCoreAsync(long companyId, string periodLabel, string csv, CancellationToken ct)
    {
        string period = (periodLabel ?? string.Empty).Trim();
        if (period.Length == 0)
        {
            return Error.Validation("Payroll.PeriodRequired", "The legacy import needs the period it covers.");
        }

        List<LegacyPayslipLine> lines = [];
        string[] rows = (csv ?? string.Empty).Split('\n');
        for (int n = 0; n < rows.Length; n++)
        {
            string row = rows[n].Trim();
            if (row.Length == 0)
            {
                continue;
            }

            string[] cells = row.Split([',', ';', '\t']).Select(c => c.Trim().Trim('"')).ToArray();
            bool parsed = cells.Length >= 3 && decimal.TryParse(cells[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) && cells[0].Length > 0 && cells[1].Length > 0;
            if (!parsed)
            {
                // The first line may be a header; anything else unreadable stops the import.
                if (lines.Count == 0 && n == Array.FindIndex(rows, r => r.Trim().Length > 0))
                {
                    continue;
                }

                return Error.Validation("Payroll.LegacyLine", $"Line {n + 1} ('{row}') should be: employee number, element code, amount.");
            }

            lines.Add(new LegacyPayslipLine(companyId, period, cells[0], cells[1], decimal.Parse(cells[2], NumberStyles.Number, CultureInfo.InvariantCulture)));
        }

        if (lines.Count == 0)
        {
            return Error.Validation("Payroll.LegacyEmpty", "The legacy file has no lines.");
        }

        _db.Set<LegacyPayslipLine>().RemoveRange(await _db.Set<LegacyPayslipLine>().Where(l => l.CompanyId == companyId && l.PeriodLabel == period).ToListAsync(ct).ConfigureAwait(false));
        _db.Set<LegacyPayslipLine>().AddRange(lines);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return lines.Count;
    }

    // The checks every correction run shares: the company, a period, a reason and an unused document number.
    private async Task<Result<(Company Company, string Period, string Document)>> CheckAdjustmentAsync(long companyId, string periodLabel, string documentNumber, string reason, CancellationToken ct)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Payroll.CompanyNotFound", $"Company {companyId} not found.");
        }

        string period = (periodLabel ?? string.Empty).Trim();
        string document = (documentNumber ?? string.Empty).Trim();
        if (period.Length == 0)
        {
            return Error.Validation("Payroll.PeriodRequired", "The run needs a period label.");
        }

        if (document.Length == 0)
        {
            return Error.Validation("Payroll.DocumentRequired", "The run needs a document number.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Payroll.ReasonRequired", "A correction run needs a reason.");
        }

        if (await _db.Set<PayrollRunRecord>().AnyAsync(r => r.CompanyId == companyId && r.DocumentNumber == document, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Payroll.DuplicateRun", $"Run document {document} has already been used.");
        }

        return (company, period, document);
    }

    private static PayElement? ElementByCode(Dictionary<long, PayElement> elements, string code)
        => elements.Values.FirstOrDefault(e => string.Equals(e.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}
