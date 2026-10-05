using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;

namespace One4All.Infrastructure.Payroll;

/// <content>
/// Net-pay protection (brief §4.15.4): deductions suspended by the minimum take-home are carried forward as arrears and attempted
/// again with the next run, and every suspension and recovery is recorded for the PR71 register.
/// </content>
public sealed partial class PayrollService
{
    /// <summary>One deduction's outcome under protection.</summary>
    /// <param name="Code">The deduction.</param>
    /// <param name="Attempted">This period's amount plus arrears brought forward.</param>
    /// <param name="Deducted">What was deducted.</param>
    private readonly record struct DeductionOutcome(string Code, decimal Attempted, decimal Deducted);

    // Each employee's arrears outstanding per deduction (suspended earlier, not yet recovered).
    private async Task<Dictionary<long, Dictionary<string, decimal>>> ArrearsAsync(long companyId, IReadOnlyCollection<Employee> employees, CancellationToken ct)
    {
        List<long> ids = [.. employees.Select(e => e.Id)];
        var balances = await _db.DeductionArrears.Where(a => a.CompanyId == companyId && ids.Contains(a.EmployeeId)).GroupBy(a => new { a.EmployeeId, a.ElementCode })
            .Select(g => new { g.Key.EmployeeId, g.Key.ElementCode, Outstanding = g.Sum(a => a.Movement) }).ToListAsync(ct).ConfigureAwait(false);
        return employees.ToDictionary(e => e.Id, e => balances.Where(b => b.EmployeeId == e.Id && b.Outstanding != 0m)
            .ToDictionary(b => b.ElementCode, b => b.Outstanding, StringComparer.OrdinalIgnoreCase));
    }

    // Builds the payslip with arrears brought forward; when net pay would fall below the company's minimum take-home, the suspendable
    // deductions are cut — highest suspension priority first — until it does not (or nothing suspendable is left).
    private (Result<Payslip> Slip, IReadOnlyList<DeductionOutcome> Outcomes, decimal Floor) BuildProtected(Company company, Employee employee, IReadOnlyDictionary<long, PayElement> elements,
        string periodLabel, DateOnly payDate, IReadOnlyDictionary<string, object?> recoveries, IReadOnlyDictionary<string, decimal> arrears, string documentNumber = "")
    {
        Result<Payslip> first = BuildPayslip(company.Id, employee, elements, periodLabel, payDate, documentNumber: documentNumber, recoveries: recoveries, arrears: arrears);
        if (first.IsFailure)
        {
            return (first, [], 0m);
        }

        Payslip slip = first.Value;
        bool protecting = company.MinimumTakeHomeAmount is not null || company.MinimumTakeHomePercent is not null;
        decimal floor = protecting ? Math.Max(company.MinimumTakeHomeAmount ?? 0m, decimal.Round(slip.GrossEarnings * (company.MinimumTakeHomePercent ?? 0m) / 100m, 2, MidpointRounding.ToEven)) : 0m;
        if (protecting && slip.NetPay < floor)
        {
            decimal shortfall = floor - slip.NetPay;
            Dictionary<string, decimal> caps = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PayElement> byCode = elements.Values.GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (PayslipLine line in slip.Lines.Where(l => l.Type == PayElementType.Deduction && l.Amount > 0m && byCode.TryGetValue(l.ElementCode, out PayElement? e) && e.SuspensionPriority > 0)
                         .OrderByDescending(l => byCode[l.ElementCode].SuspensionPriority).ThenByDescending(l => byCode[l.ElementCode].Sequence))
            {
                if (shortfall <= 0m)
                {
                    break;
                }

                decimal cut = Math.Min(line.Amount, shortfall);
                caps[line.ElementCode] = line.Amount - cut;
                shortfall -= cut;
            }

            if (caps.Count > 0)
            {
                Result<Payslip> protectedSlip = BuildPayslip(company.Id, employee, elements, periodLabel, payDate, documentNumber: documentNumber, recoveries: recoveries, arrears: arrears, caps: caps);
                if (protectedSlip.IsFailure)
                {
                    return (protectedSlip, [], floor);
                }

                slip = protectedSlip.Value;
            }
        }

        Dictionary<string, decimal> deducted = slip.Lines.Where(l => l.Type == PayElementType.Deduction).GroupBy(l => l.ElementCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount), StringComparer.OrdinalIgnoreCase);
        List<DeductionOutcome> outcomes = [.. first.Value.Lines.Where(l => l.Type == PayElementType.Deduction).GroupBy(l => l.ElementCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeductionOutcome(g.Key, g.Sum(l => l.Amount), deducted.GetValueOrDefault(g.Key)))];
        return (slip, outcomes, floor);
    }

    // The arrears entries for one employee's run: a suspension where less than attempted was deducted, a recovery where arrears brought
    // forward were cleared.
    private static IEnumerable<DeductionArrear> ArrearEntries(long companyId, Employee employee, string periodLabel, string runReference, DateOnly payDate, IReadOnlyList<DeductionOutcome> outcomes,
        IReadOnlyDictionary<string, decimal> arrears, decimal floor)
    {
        foreach (DeductionOutcome outcome in outcomes)
        {
            decimal brought = arrears.GetValueOrDefault(outcome.Code);
            decimal movement = outcome.Attempted - outcome.Deducted - brought;
            if (movement == 0m && outcome.Attempted == outcome.Deducted)
            {
                continue;
            }

            string reason = outcome.Attempted > outcome.Deducted
                ? $"Suspended {(outcome.Attempted - outcome.Deducted).ToString("N2", CultureInfo.InvariantCulture)}: net pay held at the minimum take-home of {floor.ToString("N2", CultureInfo.InvariantCulture)}"
                : "Arrears recovered";
            yield return new DeductionArrear(companyId, employee.Id, outcome.Code, periodLabel, runReference, payDate, outcome.Attempted, outcome.Deducted, movement, reason);
        }
    }

    // A reversed run's arrears movements are undone so the arrears stand as they were before it.
    private async Task ReverseArrearsAsync(long companyId, string runReference, string reversalReference, DateOnly date, CancellationToken ct)
    {
        if (runReference.Length == 0)
        {
            return;
        }

        foreach (DeductionArrear entry in await _db.DeductionArrears.AsNoTracking().Where(a => a.CompanyId == companyId && a.RunReference == runReference).ToListAsync(ct).ConfigureAwait(false))
        {
            _db.DeductionArrears.Add(new DeductionArrear(companyId, entry.EmployeeId, entry.ElementCode, entry.PeriodLabel, reversalReference, date, -entry.Attempted, -entry.Deducted, -entry.Movement,
                $"Reversed with run {runReference}"));
        }
    }
}
