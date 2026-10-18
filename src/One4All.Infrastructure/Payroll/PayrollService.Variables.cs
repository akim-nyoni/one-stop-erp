using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <content>Variable input for a pay period: keyed, or imported with a preview-and-error step.</content>
public sealed partial class PayrollService : IPayrollVariableService
{
    private const string ManualSource = "MANUAL";
    private const string ImportSource = "IMPORT";

    /// <inheritdoc />
    public async Task<Result<long>> SetAsync(long companyId, long employeeId, string elementCode, string periodLabel, decimal? units, decimal? amount, CancellationToken ct = default)
    {
        Employee? employee = await _db.Set<Employee>().FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        string period = (periodLabel ?? string.Empty).Trim();
        if (employee is null)
        {
            return Error.NotFound("Payroll.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);
        string? error = await CheckVariableAsync(companyId, employee, ElementByCode(elements, elementCode ?? string.Empty), elementCode ?? string.Empty, period, units, amount, ct).ConfigureAwait(false);
        if (error is not null)
        {
            return Error.Validation("Payroll.Variable", error);
        }

        string code = ElementByCode(elements, elementCode!)!.Code;
        _db.PayrollVariables.RemoveRange(await _db.PayrollVariables
            .Where(v => v.CompanyId == companyId && v.EmployeeId == employeeId && v.ElementCode == code && v.PeriodLabel == period && v.Source == ManualSource).ToListAsync(ct).ConfigureAwait(false));
        PayrollVariable variable = new(companyId, employeeId, code, period, units, amount, ManualSource);
        _db.PayrollVariables.Add(variable);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return variable.Id;
    }

    /// <inheritdoc />
    public async Task<Result<VariableImportPreview>> PreviewImportAsync(long companyId, string periodLabel, string csv, CancellationToken ct = default)
    {
        string period = (periodLabel ?? string.Empty).Trim();
        if (period.Length == 0)
        {
            return Error.Validation("Payroll.PeriodRequired", "The import needs the period it is for.");
        }

        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, Employee> employees = (await _db.Set<Employee>().Where(e => e.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(e => e.EmployeeNumber, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        List<VariableImportLine> lines = [];
        HashSet<(string, string)> seen = [];

        // The template, named columns in any order, or a sheet with a column per element (Application.Payroll.PayrollVariableCsv).
        IReadOnlyList<ParsedVariable> parsed = PayrollVariableCsv.Parse(csv ?? string.Empty, code => ElementByCode(elements, code) is not null,
            code => ElementByCode(elements, code)?.Basis == CalculationBasis.RatePerUnit, employees.ContainsKey);
        foreach (ParsedVariable read in parsed)
        {
            employees.TryGetValue(read.EmployeeNumber, out Employee? employee);
            string? error = read.Error
                ?? (employee is null ? $"No employee '{read.EmployeeNumber}'."
                : await CheckVariableAsync(companyId, employee, ElementByCode(elements, read.ElementCode), read.ElementCode, period, read.Units, read.Amount, ct).ConfigureAwait(false));
            if (error is null && !seen.Add((read.EmployeeNumber.ToUpperInvariant(), read.ElementCode)))
            {
                error = "Duplicate line for this employee and element.";
            }

            lines.Add(new VariableImportLine(read.Line, read.EmployeeNumber, employee?.FullName, read.ElementCode, read.Units, read.Amount, error));
        }

        return new VariableImportPreview(lines);
    }

    /// <inheritdoc />
    public Task<Result<int>> ImportAsync(long companyId, string periodLabel, string csv, CancellationToken ct = default)
        => _db.AtomicAsync(() => ImportVariablesCoreAsync(companyId, periodLabel, csv, ct), ct);

    private async Task<Result<int>> ImportVariablesCoreAsync(long companyId, string periodLabel, string csv, CancellationToken ct)
    {
        Result<VariableImportPreview> preview = await PreviewImportAsync(companyId, periodLabel, csv, ct).ConfigureAwait(false);
        if (preview.IsFailure)
        {
            return preview.Error;
        }

        if (preview.Value.ErrorCount > 0)
        {
            VariableImportLine first = preview.Value.Lines.First(l => l.Error is not null);
            return Error.Validation("Payroll.VariableErrors", $"{preview.Value.ErrorCount} line(s) have errors - first, line {first.Line}: {first.Error}");
        }

        if (preview.Value.Lines.Count == 0)
        {
            return Error.Validation("Payroll.VariableEmpty", "The file has no lines.");
        }

        string period = periodLabel.Trim();
        Dictionary<string, long> ids = await _db.Set<Employee>().Where(e => e.CompanyId == companyId).GroupBy(e => e.EmployeeNumber).Select(g => new { g.Key, Id = g.Min(e => e.Id) })
            .ToDictionaryAsync(x => x.Key, x => x.Id, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);
        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);
        _db.PayrollVariables.RemoveRange(await _db.PayrollVariables.Where(v => v.CompanyId == companyId && v.PeriodLabel == period && v.Source == ImportSource).ToListAsync(ct).ConfigureAwait(false));
        foreach (VariableImportLine line in preview.Value.Lines)
        {
            _db.PayrollVariables.Add(new PayrollVariable(companyId, ids[line.EmployeeNumber], ElementByCode(elements, line.ElementCode)!.Code, period, line.Units, line.Amount, ImportSource));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return preview.Value.Lines.Count;
    }

    // Why a variable cannot be accepted, or null.
    private async Task<string?> CheckVariableAsync(long companyId, Employee employee, PayElement? element, string code, string period, decimal? units, decimal? amount, CancellationToken ct)
    {
        if (period.Length == 0)
        {
            return "The period is required.";
        }

        if (employee.Status != EmployeeStatus.Active)
        {
            return $"{employee.EmployeeNumber} is not active.";
        }

        if (element is null)
        {
            return $"No pay element '{code}'.";
        }

        if ((units is null) == (amount is null))
        {
            return "Give units or an amount (one of them).";
        }

        if (units is not null && element.Basis != CalculationBasis.RatePerUnit)
        {
            return $"{element.Code} is not paid per unit; give an amount.";
        }

        if (units < 0m || amount < 0m)
        {
            return "Units and amounts cannot be negative.";
        }

        return await RegularRunIsLiveAsync(companyId, period, PayGroupKey(employee.PayGroup), ct).ConfigureAwait(false) ? $"Period {period} has already been paid for {employee.EmployeeNumber}." : null;
    }

    // Each employee's variables for the period, by element code.
    private async Task<Dictionary<long, Dictionary<string, PayrollVariable>>> VariablesAsync(long companyId, string periodLabel, IReadOnlyCollection<Employee> employees, CancellationToken ct)
    {
        List<long> ids = [.. employees.Select(e => e.Id)];
        List<PayrollVariable> variables = await _db.PayrollVariables.AsNoTracking().Where(v => v.CompanyId == companyId && v.PeriodLabel == periodLabel && ids.Contains(v.EmployeeId))
            .OrderBy(v => v.Id).ToListAsync(ct).ConfigureAwait(false);

        // A keyed variable wins over an imported one for the same element.
        return employees.ToDictionary(e => e.Id, e => variables.Where(v => v.EmployeeId == e.Id).GroupBy(v => v.ElementCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Source == ManualSource ? 1 : 0).ThenBy(v => v.Id).Last(), StringComparer.OrdinalIgnoreCase));
    }
}
