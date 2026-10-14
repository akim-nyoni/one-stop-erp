using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Banking;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Banking;

/// <summary>Bank payment-file layouts and payroll EFT files (brief §4.15.7).</summary>
public sealed class BankFileService : IBankFileService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="audit">Records each payroll bank file produced.</param>
    /// <param name="user">The user producing it.</param>
    public BankFileService(One4AllDbContext db, Application.Abstractions.Security.IAuditService? audit = null, Application.Abstractions.Security.ICurrentUser? user = null)
    {
        _db = db;
        _audit = audit;
        _user = user;
    }

    private readonly Application.Abstractions.Security.IAuditService? _audit;
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


    /// <inheritdoc />
    public async Task<Result<long>> SaveLayoutAsync(BankFileLayoutRequest request, CancellationToken ct = default)
    {
        string code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0 || string.IsNullOrWhiteSpace(request.DetailTemplate))
        {
            return Error.Validation("Bank.LayoutRequired", "A layout needs a code and a detail template.");
        }

        List<string> unknown = [.. BankFileFormatter.UnknownFields(request.HeaderTemplate, BankFileFormatter.FileFields), .. BankFileFormatter.UnknownFields(request.DetailTemplate, BankFileFormatter.DetailFields),
            .. BankFileFormatter.UnknownFields(request.TrailerTemplate, BankFileFormatter.FileFields)];
        if (unknown.Count > 0)
        {
            return Error.Validation("Bank.LayoutField", $"Unknown field(s) in the layout: {string.Join(", ", unknown)}.");
        }

        BankFileLayout? layout = await _db.BankFileLayouts.FirstOrDefaultAsync(l => l.CompanyId == request.CompanyId && l.Code == code, ct).ConfigureAwait(false);
        if (layout is null)
        {
            layout = new BankFileLayout(request.CompanyId, code, request.Name);
            _db.BankFileLayouts.Add(layout);
        }

        layout.Name = request.Name;
        layout.HeaderTemplate = string.IsNullOrWhiteSpace(request.HeaderTemplate) ? null : request.HeaderTemplate;
        layout.DetailTemplate = request.DetailTemplate;
        layout.TrailerTemplate = string.IsNullOrWhiteSpace(request.TrailerTemplate) ? null : request.TrailerTemplate;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return layout.Id;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BankFileOutput>>> PayrollFilesAsync(long companyId, string runNumber, string layoutCode, CancellationToken ct = default)
    {
        Result<IReadOnlyList<BankFileOutput>> files = await PayrollFilesCoreAsync(companyId, runNumber, layoutCode, ct).ConfigureAwait(false);
        if (files.IsSuccess)
        {
            await RecordExportAsync(companyId, "Payroll", "PayrollBankFile", (runNumber ?? string.Empty).Trim(), $"{files.Value.Count} file(s), layout {layoutCode}", ct).ConfigureAwait(false);
        }

        return files;
    }

    private async Task<Result<IReadOnlyList<BankFileOutput>>> PayrollFilesCoreAsync(long companyId, string runNumber, string layoutCode, CancellationToken ct)
    {
        BankFileLayout? layout = await _db.BankFileLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.CompanyId == companyId && l.Code == (layoutCode ?? string.Empty).Trim().ToUpper(), ct).ConfigureAwait(false);
        if (layout is null)
        {
            return Error.NotFound("Bank.LayoutNotFound", $"No bank file layout '{layoutCode}'.");
        }

        string key = (runNumber ?? string.Empty).Trim();
        PayrollRunRecord? run = await _db.Set<PayrollRunRecord>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && key != string.Empty && (r.DocumentNumber == key || r.JournalNumber == key), ct).ConfigureAwait(false);
        if (run is null || run.DocumentNumber.Length == 0 || run.Kind == PayRunKind.Reversal || run.ReversedOn is not null)
        {
            return Error.NotFound("Payroll.RunNotFound", $"No payable payroll run '{key}'.");
        }

        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        List<Payslip> slips = await _db.Payslips.AsNoTracking().Where(p => p.CompanyId == companyId && p.RunReference == run.DocumentNumber && p.NetPay > 0m).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. slips.Select(s => s.EmployeeId)];
        Dictionary<long, Employee> employees = await _db.Employees.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);
        List<EmployeePaymentSplit> splits = await _db.PaymentSplits.AsNoTracking().Where(s => s.CompanyId == companyId && ids.Contains(s.EmployeeId) && s.Method == PayMethod.BankTransfer)
            .OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false);

        List<(string Bank, string Reference, string Name, string Branch, string Account, decimal Amount)> payments = [];
        foreach (Payslip slip in slips.OrderBy(s => employees[s.EmployeeId].EmployeeNumber, StringComparer.Ordinal))
        {
            Employee employee = employees[slip.EmployeeId];
            if (employee.IsSalaryHeld)
            {
                continue;
            }

            string bank = employee.BankName ?? "UNKNOWN BANK";
            string reference = $"{run.PeriodLabel} {employee.EmployeeNumber}";
            decimal remaining = slip.NetPay;
            foreach (EmployeePaymentSplit split in splits.Where(s => s.EmployeeId == employee.Id))
            {
                decimal amount = Math.Min(split.AmountFrom(slip.NetPay), remaining);
                if (amount > 0m)
                {
                    payments.Add((bank, reference, employee.FullName, employee.BankBranch ?? string.Empty, split.Destination, amount));
                    remaining -= amount;
                }
            }

            if (remaining > 0m && employee.PaymentMethod == PayMethod.BankTransfer)
            {
                if (string.IsNullOrWhiteSpace(employee.BankAccountNumber))
                {
                    return Error.Validation("Bank.MissingAccount", $"{employee.EmployeeNumber} {employee.FullName} is paid by bank but has no account number.");
                }

                payments.Add((bank, reference, employee.FullName, employee.BankBranch ?? string.Empty, employee.BankAccountNumber, remaining));
            }
        }

        string currency = company.BaseCurrency.ToString();
        List<BankFileOutput> files = [.. payments.GroupBy(p => p.Bank).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            List<BankFileRow> rows = [.. g.Select((p, i) => new BankFileRow(i + 1, p.Reference, p.Name, p.Bank, p.Branch, p.Account, currency, p.Amount))];
            BankFileHeader header = new(run.PayDate, company.Code, currency, rows.Count, rows.Sum(r => r.Amount), BankFileFormatter.HashTotal(rows.Select(r => r.Account)));
            string name = new([.. $"{run.DocumentNumber}-{g.Key}-{currency}".Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_')]);
            return new BankFileOutput(g.Key, currency, name + ".txt", header.Count, header.Total, header.Hash,
                BankFileFormatter.Render(layout.HeaderTemplate, layout.DetailTemplate, layout.TrailerTemplate, header, rows, layout.LineEnding));
        })];
        return files;
    }
}
