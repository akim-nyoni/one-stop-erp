using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
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
            .SelectMany(p => p.Lines.Select(l => new { p.EmployeeId, l.ElementCode, l.Amount, l.CurrencySplit })).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);

        // The base amounts of some lines that belong to the foreign currencies the earnings were paid in, summed.
        static Dictionary<string, decimal> Foreign(IEnumerable<string?> splits)
        {
            Dictionary<string, decimal> total = new(StringComparer.Ordinal);
            foreach ((string currency, decimal portion) in splits.SelectMany(PayslipCurrencySplit.Parse))
            {
                total[currency] = total.GetValueOrDefault(currency) + portion;
            }

            return total;
        }

        // What each payee is owed from this run, per liability account, and how much of it (in base) belongs to each foreign currency.
        List<(string Payee, long SupplierId, string Account, decimal Amount, Dictionary<string, decimal> Foreign)> owed = [];
        foreach (StatutoryScheme scheme in await _db.Set<StatutoryScheme>().Where(s => s.CompanyId == companyId && s.SupplierId != null).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false))
        {
            HashSet<string> codes = new(new[] { scheme.EmployeeElementCode, scheme.EmployerElementCode }.OfType<string>(), StringComparer.OrdinalIgnoreCase);
            owed.AddRange(byEmployee.Where(l => codes.Contains(l.ElementCode)).GroupBy(l => ElementByCode(elements, l.ElementCode)?.AccountCode ?? string.Empty)
                .Where(g => g.Key.Length > 0).Select(g => (scheme.Code, scheme.SupplierId!.Value, g.Key, g.Sum(l => l.Amount), Foreign(g.Select(l => l.CurrencySplit)))));
        }

        foreach (GarnisheeOrder order in await _db.Set<GarnisheeOrder>().Where(g => g.CompanyId == companyId && g.SupplierId != null).OrderBy(g => g.Reference).ToListAsync(ct).ConfigureAwait(false))
        {
            var lines = byEmployee.Where(l => l.EmployeeId == order.EmployeeId && string.Equals(l.ElementCode, order.ElementCode, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ElementByCode(elements, order.ElementCode) is PayElement element)
            {
                // A court order is owed to its creditor in the base currency, whatever currency the employee was paid in.
                owed.Add((order.Reference, order.SupplierId!.Value, element.AccountCode, lines.Sum(l => l.Amount), new Dictionary<string, decimal>(StringComparer.Ordinal)));
            }
        }

        owed = [.. owed.Where(o => o.Amount > 0m)];
        if (owed.Count == 0)
        {
            return Error.Rule("Payroll.NothingToRemit", "No scheme or garnishee creditor with a supplier has anything to be paid from this run.");
        }

        List<RemittanceRaised> raised = [];
        foreach (IGrouping<string, (string Payee, long SupplierId, string Account, decimal Amount, Dictionary<string, decimal> Foreign)> payee in owed.GroupBy(o => o.Payee))
        {
            int n = 0;
            foreach ((string name, long supplierId, string account, decimal amount, Dictionary<string, decimal> foreign) in payee)
            {
                string document = n++ == 0 ? $"{run.DocumentNumber}-{name}" : $"{run.DocumentNumber}-{name}-{n}";

                // Tax and levies go to the authority in the currency the earnings were paid in: each foreign currency's part is an invoice of
                // its own in that currency, at the pay date's rate; the rest is invoiced in base as before.
                decimal inBase = amount - foreign.Values.Sum();
                if (inBase > 0m)
                {
                    string baseDocument = document[..Math.Min(document.Length, 40)];
                    Result<TradingPostingResult> posted = await _purchases.PostInvoiceAsync(new PurchaseInvoiceRequest
                    {
                        CompanyId = companyId, SupplierId = supplierId, DocumentNumber = baseDocument, DocumentDate = run.PayDate, Currency = company.BaseCurrency, NetAmount = inBase, TaxAmount = 0m,
                        ExpenseAccountCode = account, Narrative = $"Payroll {run.PeriodLabel} remittance: {name}",
                    }, ct).ConfigureAwait(false);
                    if (posted.IsFailure)
                    {
                        return posted.Error;
                    }

                    _db.RemittanceInvoices.Add(new RemittanceInvoice(companyId, run.DocumentNumber, run.PeriodLabel, name, supplierId, account, inBase, baseDocument, run.PayDate));
                    raised.Add(new RemittanceRaised(name, baseDocument, account, inBase));
                }

                foreach ((string code, decimal portion) in foreign.Where(f => f.Value > 0m).OrderBy(f => f.Key, StringComparer.Ordinal))
                {
                    CurrencyCode currency = CurrencyCode.Parse(code);
                    decimal? rate = await Accounting.ExchangeRateResolver.FindAsync(_db, currency, company.BaseCurrency, ExchangeRateType.Daily, run.PayDate, ct).ConfigureAwait(false);
                    if (rate is not decimal payRate || payRate <= 0m)
                    {
                        return Error.Rule("Payroll.NoRate", $"No {code} rate on {run.PayDate:yyyy-MM-dd} to remit the {code} part of {name}; enter the day's rate first.");
                    }

                    string suffix = "-" + code;
                    string foreignDocument = document[..Math.Min(document.Length, 40 - suffix.Length)] + suffix;
                    Result<TradingPostingResult> posted = await _purchases.PostInvoiceAsync(new PurchaseInvoiceRequest
                    {
                        CompanyId = companyId, SupplierId = supplierId, DocumentNumber = foreignDocument, DocumentDate = run.PayDate, Currency = currency,
                        NetAmount = decimal.Round(portion / payRate, 2, MidpointRounding.AwayFromZero), TaxAmount = 0m,

                        // A keyed rate needs the override permission where the company controls overrides; the table then gives the same daily rate.
                        ExchangeRate = company.RateOverrideControl ? null : payRate,
                        ExpenseAccountCode = account, Narrative = $"Payroll {run.PeriodLabel} remittance: {name}, the {code} part",
                    }, ct).ConfigureAwait(false);
                    if (posted.IsFailure)
                    {
                        return posted.Error;
                    }

                    _db.RemittanceInvoices.Add(new RemittanceInvoice(companyId, run.DocumentNumber, run.PeriodLabel, name, supplierId, account, portion, foreignDocument, run.PayDate));
                    raised.Add(new RemittanceRaised(name, foreignDocument, account, portion));
                }
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return raised;
    }
}
