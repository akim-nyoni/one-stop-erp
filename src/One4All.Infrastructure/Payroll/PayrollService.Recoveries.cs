using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Payroll;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Payroll;

/// <content>
/// Recoveries through payroll (brief §4.15.4): staff purchases charged to the staff account at the till and rent on the employee's
/// council / employer housing tenancy. Their outstanding balances are the formula variables STAFFPURCHASES and HOUSINGRENT; rent
/// deducted is receipted to the tenancy's account so the rent roll and the payroll agree.
/// </content>
public sealed partial class PayrollService
{
    /// <summary>The formula variable holding the employee's unrecovered staff purchases.</summary>
    public const string StaffPurchasesVariable = "STAFFPURCHASES";

    /// <summary>The formula variable holding the rent owed on the employee's housing tenancy.</summary>
    public const string HousingRentVariable = "HOUSINGRENT";

    // Each employee's balances to recover as at the date: staff tenders less staff-purchase deductions already made, and the tenancy
    // account's balance (never below zero — a tenant in credit has nothing to deduct).
    private async Task<Dictionary<long, Dictionary<string, object?>>> RecoveryVariablesAsync(long companyId, IReadOnlyCollection<Employee> employees, IReadOnlyDictionary<long, PayElement> elements,
        DateOnly asAt, CancellationToken ct)
    {
        List<long> ids = [.. employees.Select(e => e.Id)];
        var tendered = await _db.Set<Sale>().Where(s => s.CompanyId == companyId && s.Status == SaleStatus.Completed && s.SaleDate <= asAt)
            .SelectMany(s => s.Tenders).Where(t => t.StaffEmployeeId != null && ids.Contains(t.StaffEmployeeId.Value))
            .GroupBy(t => t.StaffEmployeeId!.Value).Select(g => new { EmployeeId = g.Key, Amount = g.Sum(t => t.BaseAmount) }).ToListAsync(ct).ConfigureAwait(false);
        List<string> staffCodes = [.. elements.Values.Where(e => e.Recovers == PayRecovery.StaffPurchases).Select(e => e.Code)];
        var recovered = await _db.Set<Payslip>().Where(p => p.CompanyId == companyId && ids.Contains(p.EmployeeId))
            .SelectMany(p => p.Lines.Where(l => staffCodes.Contains(l.ElementCode)).Select(l => new { p.EmployeeId, l.Amount }))
            .GroupBy(x => x.EmployeeId).Select(g => new { EmployeeId = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, long> tenantByEmployee = await TenantCustomersAsync(companyId, employees, ct).ConfigureAwait(false);
        Dictionary<long, decimal> owing = [];
        if (tenantByEmployee.Count > 0)
        {
            HashSet<long> customers = [.. tenantByEmployee.Values];
            owing = (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false))
                .Where(i => customers.Contains(i.Item.TraderId)).GroupBy(i => i.Item.TraderId).ToDictionary(g => g.Key, g => g.Sum(i => i.Signed(LedgerSign.Debit)));
        }

        return employees.ToDictionary(e => e.Id, e => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [StaffPurchasesVariable] = Math.Max(0m, (tendered.FirstOrDefault(t => t.EmployeeId == e.Id)?.Amount ?? 0m) - (recovered.FirstOrDefault(r => r.EmployeeId == e.Id)?.Amount ?? 0m)),
            [HousingRentVariable] = tenantByEmployee.TryGetValue(e.Id, out long customer) ? Math.Max(0m, owing.GetValueOrDefault(customer)) : 0m,
        });
    }

    private async Task<Dictionary<long, long>> TenantCustomersAsync(long companyId, IEnumerable<Employee> employees, CancellationToken ct)
    {
        Dictionary<long, long> tenancyByEmployee = employees.Where(e => e.HousingTenancyId != null).ToDictionary(e => e.Id, e => e.HousingTenancyId!.Value);
        List<long> tenancyIds = [.. tenancyByEmployee.Values];
        Dictionary<long, long> customerByTenancy = await _db.Set<Tenancy>().Where(t => t.CompanyId == companyId && tenancyIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TenantCustomerId, ct)
            .ConfigureAwait(false);
        return tenancyByEmployee.Where(kv => customerByTenancy.ContainsKey(kv.Value)).ToDictionary(kv => kv.Key, kv => customerByTenancy[kv.Value]);
    }

    // Rent deducted from pay is receipted to the tenancy's account (Dr the deduction's clearing account, Cr AR); a negative amount —
    // a reversal or a refund — reverses the receipt.
    private async Task<Result> SettleHousingRentAsync(Company company, IReadOnlyList<Payslip> slips, IReadOnlyDictionary<long, PayElement> elements, string documentNumber, DateOnly date,
        CancellationToken ct)
    {
        Dictionary<string, PayElement> rentElements = elements.Values.Where(e => e.Recovers == PayRecovery.HousingRent).GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (rentElements.Count == 0)
        {
            return Result.Success();
        }

        List<long> employeeIds = [.. slips.Select(s => s.EmployeeId).Distinct()];
        List<Employee> employees = await _db.Set<Employee>().Where(e => employeeIds.Contains(e.Id)).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, long> tenants = await TenantCustomersAsync(company.Id, employees, ct).ConfigureAwait(false);
        foreach (Payslip slip in slips)
        {
            foreach (IGrouping<string, PayslipLine> account in slip.Lines.Where(l => rentElements.ContainsKey(l.ElementCode) && l.Amount != 0m).GroupBy(l => rentElements[l.ElementCode].AccountCode))
            {
                if (!tenants.TryGetValue(slip.EmployeeId, out long customer))
                {
                    return Result.Failure(Error.Rule("Payroll.NoTenancy", "A housing-rent deduction needs the employee's tenancy (Employee.HousingTenancyId) to receipt it to."));
                }

                decimal amount = account.Sum(l => l.Amount);
                string employeeNumber = employees.First(e => e.Id == slip.EmployeeId).EmployeeNumber;
                string document = $"{documentNumber}-{employeeNumber}";
                document = document[..Math.Min(document.Length, 40)];
                Result<TradingPostingResult> posted = amount > 0m
                    ? await _sales.PostReceiptAsync(new ReceiptRequest
                    {
                        CompanyId = company.Id, CustomerId = customer, DocumentNumber = document, ReceiptDate = date, Currency = company.BaseCurrency, Amount = amount, BankAccountCode = account.Key,
                        Narrative = $"Rent deducted from pay ({slip.PeriodLabel})",
                    }, ct).ConfigureAwait(false)
                    : await _sales.PostReceiptReversalAsync(company.Id, customer, document, date, company.BaseCurrency, -amount, account.Key, ct: ct).ConfigureAwait(false);
                if (posted.IsFailure)
                {
                    return Result.Failure(posted.Error);
                }
            }
        }

        return Result.Success();
    }
}
