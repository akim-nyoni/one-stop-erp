using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Payroll;
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

    /// <summary>The formula variable holding the taxable pay earlier in the tax year.</summary>
    public const string YtdTaxableVariable = "YTDTAXABLE";

    /// <summary>The formula variable holding the period number in the tax year (the pay date's month).</summary>
    public const string TaxPeriodVariable = "TAXPERIOD";

    // The employee's tax credits in force on the pay date (brief §4.15.3), taken off what the tax table works out.
    private const string TaxCreditsKey = "__TAXCREDITS";

    /// <summary>The variable holding the employee's pay periods a year (12, 26 or 52).</summary>
    public const string PeriodsPerYearVariable = "PERIODSPERYEAR";

    /// <summary>The variable holding the period's acting allowance: the acted grade's scale less the employee's own basic (brief §4.15.1).</summary>
    public const string ActingAllowanceVariable = "ACTINGALLOWANCE";

    private const string TaxTablesKey = "__TAXTABLES";
    private const string TaxableByCurrencyKey = "__TAXABLEBYCCY";
    private const string PayRatesKey = "__PAYRATES";
    private const string PayDateKey = "__PAYDATE";
    private const string BenefitsKey = "__BENEFITS";
    private const string LoansKey = "__LOANS";

    /// <summary>The prefix of a payslip line repaying a staff loan (followed by the loan's id).</summary>
    public const string LoanLinePrefix = "LOAN-";

    /// <summary>The prefix of a payslip memo line for the interest a staff loan was charged (followed by the loan's id).</summary>
    public const string LoanInterestLinePrefix = "LOANINT-";

    // Each employee's balances to recover as at the date: staff tenders less staff-purchase deductions already made, and the tenancy
    // account's balance (never below zero - a tenant in credit has nothing to deduct).
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

        // The tax year so far (from the company's tax-year start to the period before this pay date): taxable pay and the tax each tax-table element deducted.
        int taxYearStartMonth = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.TaxYearStartMonth).FirstAsync(ct).ConfigureAwait(false);
        DateOnly yearStart = PayPeriods.TaxYearStart(asAt, taxYearStartMonth);
        List<string> taxCodes = [.. elements.Values.Where(e => e.Basis == CalculationBasis.TaxTable).Select(e => e.Code)];
        var ytd = await _db.Set<Payslip>().Where(p => p.CompanyId == companyId && ids.Contains(p.EmployeeId) && p.PayDate >= yearStart && p.PayDate < asAt)
            .SelectMany(p => p.Lines.Where(l => ((l.Type == PayElementType.Earning || l.Type == PayElementType.Memo) && l.Taxable) || taxCodes.Contains(l.ElementCode))
                .Select(l => new { p.EmployeeId, l.ElementCode, l.Type, l.Amount }))
            .ToListAsync(ct).ConfigureAwait(false);
        List<TaxTable> tables = taxCodes.Count == 0 ? [] : await _db.Set<TaxTable>().AsNoTracking().Include(t => t.Bands).Where(t => t.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        List<TaxCredit> credits = taxCodes.Count == 0 ? [] : await _db.Set<TaxCredit>().AsNoTracking().Where(c => c.CompanyId == companyId && c.IsActive).ToListAsync(ct).ConfigureAwait(false);
        var given = credits.Count == 0 ? [] : await _db.Set<EmployeeTaxCredit>().AsNoTracking().Where(g => ids.Contains(g.EmployeeId)).Select(g => new { g.EmployeeId, g.TaxCreditId })
            .ToListAsync(ct).ConfigureAwait(false);

        // The pay date's rate for each currency elements are paid in (split-currency tax converts at it).
        Dictionary<string, decimal> rates = new(StringComparer.Ordinal);
        if (elements.Values.Any(e => e.SplitCurrencyTax || e.AmountsInPaidCurrency))
        {
            Core.Accounting.CurrencyCode baseCurrency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
            foreach (Core.Accounting.CurrencyCode currency in elements.Values.Where(e => e.PaidInCurrency is not null).Select(e => e.PaidInCurrency!.Value).Distinct())
            {
                if (await Accounting.ExchangeRateResolver.FindAsync(_db, currency, baseCurrency, ExchangeRateType.Daily, asAt, ct).ConfigureAwait(false) is decimal rate)
                {
                    rates[currency.Value] = rate;
                }
            }
        }

        // Benefits in kind in force on the pay date, valued for the month (brief §4.15.4): each benefit, and a cheap loan's benefit at the deemed rate.
        List<EmployeeBenefit> benefits = await _db.EmployeeBenefits.AsNoTracking().Where(b => b.CompanyId == companyId && ids.Contains(b.EmployeeId)).ToListAsync(ct).ConfigureAwait(false);
        List<VehicleBenefitScale> scales = benefits.Any(b => b.Kind == BenefitKind.MotorVehicle)
            ? await _db.VehicleBenefitScales.AsNoTracking().Where(s => s.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false)
            : [];
        decimal? deemedRate = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.DeemedLoanInterestRatePercent).FirstAsync(ct).ConfigureAwait(false);
        var loans = deemedRate is null ? [] : await _db.EmployeeLoans.AsNoTracking()
            .Where(l => l.CompanyId == companyId && ids.Contains(l.EmployeeId) && l.Outstanding > 0m && l.GrantedOn <= asAt)
            .Select(l => new { l.EmployeeId, l.Reference, l.Outstanding, l.InterestRatePercent }).ToListAsync(ct).ConfigureAwait(false);

        // Loans repaid from pay this period (brief §4.15.5): active, deducted from pay, not suspended - by hand, or while unpaid leave covers the pay date.
        List<string> unpaidTypes = await _db.Set<LeavePolicy>().AsNoTracking().Where(p => p.CompanyId == companyId && p.IsUnpaid).Select(p => p.LeaveType).ToListAsync(ct).ConfigureAwait(false);
        List<long> onUnpaidLeave = unpaidTypes.Count == 0 ? [] : await _db.LeaveRecords.AsNoTracking()
            .Where(r => r.CompanyId == companyId && ids.Contains(r.EmployeeId) && r.Kind == LeaveRecordKind.Taken && unpaidTypes.Contains(r.LeaveType.ToUpper()) && r.FromDate <= asAt && r.ToDate >= asAt)
            .Select(r => r.EmployeeId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var repaying = await _db.EmployeeLoans.AsNoTracking()
            .Where(l => l.CompanyId == companyId && ids.Contains(l.EmployeeId) && l.Status == EmployeeLoanStatus.Active && l.DeductFromPay && !l.Suspended && l.Outstanding > 0m && l.GrantedOn <= asAt
                && !onUnpaidLeave.Contains(l.EmployeeId))
            .OrderBy(l => l.GrantedOn).ThenBy(l => l.Id)
            .Select(l => new { l.Id, l.EmployeeId, l.Reference, l.Outstanding, l.Installment, l.InterestRatePercent }).ToListAsync(ct).ConfigureAwait(false);

        // What the previous payroll paid earlier in the tax year (migrated openings) counts with this payroll's own payslips.
        // Acting appointments covering the pay date, valued at the acted grade's scale.
        List<ActingAppointment> acting = await _db.ActingAppointments.AsNoTracking().Where(a => a.CompanyId == companyId && ids.Contains(a.EmployeeId) && a.From <= asAt && (a.To == null || a.To >= asAt))
            .ToListAsync(ct).ConfigureAwait(false);
        List<SalaryScale> salaryScales = acting.Count == 0 ? [] : await _db.SalaryScales.AsNoTracking().Where(s => s.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);

        var openings = await _db.Set<PayrollYtdOpening>().AsNoTracking().Where(o => ids.Contains(o.EmployeeId) && o.TaxYear == PayPeriods.TaxYear(asAt, taxYearStartMonth))
            .Select(o => new { o.EmployeeId, o.Code, o.Amount }).ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, Dictionary<string, object?>> result = [];
        foreach (Employee e in employees)
        {
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                [YtdTaxableVariable] = ytd.Where(l => l.EmployeeId == e.Id && l.Type is PayElementType.Earning or PayElementType.Memo).Sum(l => l.Amount)
                    + openings.Where(o => o.EmployeeId == e.Id && o.Code == PayrollYtdOpening.TaxableCode).Sum(o => o.Amount),
                [TaxPeriodVariable] = (decimal)PayPeriods.Number(e.PayFrequency, asAt, taxYearStartMonth),
                [TaxTablesKey] = tables,
                [TaxCreditsKey] = credits.Where(c => (c.AppliesToAll || given.Any(g => g.EmployeeId == e.Id && g.TaxCreditId == c.Id)) && TaxCredits.Eligible(c.MinimumAge, e.DateOfBirth, asAt))
                    .Select(c => new TaxCreditRule(c.Code, c.Basis, c.Amount, c.Percent, c.ElementCode)).ToList(),
                [PayDateKey] = asAt,
                [PayRatesKey] = rates,
                [StaffPurchasesVariable] = Math.Max(0m, (tendered.FirstOrDefault(t => t.EmployeeId == e.Id)?.Amount ?? 0m) - (recovered.FirstOrDefault(r => r.EmployeeId == e.Id)?.Amount ?? 0m)),
                [HousingRentVariable] = tenantByEmployee.TryGetValue(e.Id, out long customer) ? Math.Max(0m, owing.GetValueOrDefault(customer)) : 0m,
            };
            foreach (string code in taxCodes)
            {
                values["YTD_" + code] = ytd.Where(l => l.EmployeeId == e.Id && l.ElementCode == code && l.Type != PayElementType.Earning).Sum(l => l.Amount)
                    + openings.Where(o => o.EmployeeId == e.Id && string.Equals(o.Code, code, StringComparison.OrdinalIgnoreCase)).Sum(o => o.Amount);
            }

            List<(string Code, string Name, decimal Monthly)> valued = [.. benefits.Where(b => b.EmployeeId == e.Id)
                .Select(b => ($"BEN-{b.Kind.ToString().ToUpperInvariant()}", b.Description.Length > 0 ? $"{b.Kind} benefit ({b.Description})" : $"{b.Kind} benefit",
                    BenefitValuation.Monthly(b, scales, asAt)))
                .Where(b => b.Item3 > 0m)];
            foreach (var loan in loans.Where(l => l.EmployeeId == e.Id))
            {
                decimal loanBenefit = BenefitValuation.LoanBenefit(loan.Outstanding, loan.InterestRatePercent, deemedRate ?? 0m);
                if (loanBenefit > 0m)
                {
                    valued.Add(("BEN-LOAN", $"Loan benefit ({loan.Reference})", loanBenefit));
                }
            }

            decimal actingMonthly = acting.Where(a => a.EmployeeId == e.Id)
                .Select(a => Math.Max(0m, (SalaryScale.Amount(salaryScales, a.Grade, a.Notch, asAt) ?? 0m) - e.BasicSalary)).DefaultIfEmpty(0m).Max();
            values[ActingAllowanceVariable] = decimal.Round(actingMonthly * 12m / PayPeriods.PerYear(e.PayFrequency), 2, MidpointRounding.AwayFromZero);
            values[BenefitsKey] = valued;
            values[LoansKey] = repaying.Where(l => l.EmployeeId == e.Id).Select(l => (l.Id, l.Reference, l.Outstanding, l.Installment, l.InterestRatePercent)).ToList();
            result[e.Id] = values;
        }

        return result;
    }

    private async Task<Dictionary<long, long>> TenantCustomersAsync(long companyId, IEnumerable<Employee> employees, CancellationToken ct)
    {
        Dictionary<long, long> tenancyByEmployee = employees.Where(e => e.HousingTenancyId != null).ToDictionary(e => e.Id, e => e.HousingTenancyId!.Value);
        List<long> tenancyIds = [.. tenancyByEmployee.Values];
        Dictionary<long, long> customerByTenancy = await _db.Set<Tenancy>().Where(t => t.CompanyId == companyId && tenancyIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TenantCustomerId, ct)
            .ConfigureAwait(false);
        return tenancyByEmployee.Where(kv => customerByTenancy.ContainsKey(kv.Value)).ToDictionary(kv => kv.Key, kv => customerByTenancy[kv.Value]);
    }

    // Rent deducted from pay is receipted to the tenancy's account (Dr the deduction's clearing account, Cr AR); a negative amount -
    // a reversal or a refund - reverses the receipt.
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
