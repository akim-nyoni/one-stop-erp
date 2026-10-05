using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <content>Third-party payments: AP invoices for schemes and garnishee creditors after a run.</content>
public sealed partial class PayrollService : IPayrollRemittanceService
{
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<RemittanceRaised>>> RaiseRemittancesAsync(long companyId, string runNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => RaiseRemittancesCoreAsync(companyId, runNumber, ct), ct);

    private async Task<Result<IReadOnlyList<RemittanceRaised>>> RaiseRemittancesCoreAsync(long companyId, string runNumber, CancellationToken ct)
    {
        string key = (runNumber ?? string.Empty).Trim();
        PayrollRunRecord? run = await _db.Set<PayrollRunRecord>()
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && key != string.Empty && (r.DocumentNumber == key || r.JournalNumber == key), ct).ConfigureAwait(false);
        if (run is null || run.DocumentNumber.Length == 0)
        {
            return Error.NotFound("Payroll.RunNotFound", $"No payroll run '{key}' with a document number.");
        }

        if (run.Kind == PayRunKind.Reversal || run.IsReversed)
        {
            return Error.Rule("Payroll.RunReversed", "A reversed run, or a reversal, has nothing to remit.");
        }

        if (await _db.RemittanceInvoices.AnyAsync(r => r.CompanyId == companyId && r.RunReference == run.DocumentNumber, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Payroll.RemittancesRaised", $"The third-party invoices for run {run.DocumentNumber} have already been raised.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        var byEmployee = await _db.Set<Payslip>().Where(p => p.CompanyId == companyId && p.RunReference == run.DocumentNumber)
            .SelectMany(p => p.Lines.Select(l => new { p.EmployeeId, l.ElementCode, l.Amount })).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);

        // What each payee is owed from this run, per liability account.
        List<(string Payee, long SupplierId, string Account, decimal Amount)> owed = [];
        foreach (StatutoryScheme scheme in await _db.Set<StatutoryScheme>().Where(s => s.CompanyId == companyId && s.SupplierId != null).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false))
        {
            HashSet<string> codes = new(new[] { scheme.EmployeeElementCode, scheme.EmployerElementCode }.OfType<string>(), StringComparer.OrdinalIgnoreCase);
            owed.AddRange(byEmployee.Where(l => codes.Contains(l.ElementCode)).GroupBy(l => ElementByCode(elements, l.ElementCode)?.AccountCode ?? string.Empty)
                .Where(g => g.Key.Length > 0).Select(g => (scheme.Code, scheme.SupplierId!.Value, g.Key, g.Sum(l => l.Amount))));
        }

        foreach (GarnisheeOrder order in await _db.Set<GarnisheeOrder>().Where(g => g.CompanyId == companyId && g.SupplierId != null).OrderBy(g => g.Reference).ToListAsync(ct).ConfigureAwait(false))
        {
            decimal amount = byEmployee.Where(l => l.EmployeeId == order.EmployeeId && string.Equals(l.ElementCode, order.ElementCode, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Amount);
            if (ElementByCode(elements, order.ElementCode) is PayElement element)
            {
                owed.Add((order.Reference, order.SupplierId!.Value, element.AccountCode, amount));
            }
        }

        owed = [.. owed.Where(o => o.Amount > 0m)];
        if (owed.Count == 0)
        {
            return Error.Rule("Payroll.NothingToRemit", "No scheme or garnishee creditor with a supplier has anything to be paid from this run.");
        }

        List<RemittanceRaised> raised = [];
        foreach (IGrouping<string, (string Payee, long SupplierId, string Account, decimal Amount)> payee in owed.GroupBy(o => o.Payee))
        {
            int n = 0;
            foreach ((string name, long supplierId, string account, decimal amount) in payee)
            {
                string document = n++ == 0 ? $"{run.DocumentNumber}-{name}" : $"{run.DocumentNumber}-{name}-{n}";
                document = document[..Math.Min(document.Length, 40)];
                Result<TradingPostingResult> posted = await _purchases.PostInvoiceAsync(new PurchaseInvoiceRequest
                {
                    CompanyId = companyId, SupplierId = supplierId, DocumentNumber = document, DocumentDate = run.PayDate, Currency = company.BaseCurrency, NetAmount = amount, TaxAmount = 0m,
                    ExpenseAccountCode = account, Narrative = $"Payroll {run.PeriodLabel} remittance: {name}",
                }, ct).ConfigureAwait(false);
                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                _db.RemittanceInvoices.Add(new RemittanceInvoice(companyId, run.DocumentNumber, run.PeriodLabel, name, supplierId, account, amount, document, run.PayDate));
                raised.Add(new RemittanceRaised(name, document, account, amount));
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return raised;
    }
}
