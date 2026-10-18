using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>One line of a variable-input import as validated.</summary>
/// <param name="Line">The line number in the file.</param>
/// <param name="EmployeeNumber">The employee number.</param>
/// <param name="EmployeeName">The employee's name, when found.</param>
/// <param name="ElementCode">The element.</param>
/// <param name="Units">Units.</param>
/// <param name="Amount">Amount.</param>
/// <param name="Error">Why the line cannot be imported, or null.</param>
public sealed record VariableImportLine(int Line, string EmployeeNumber, string? EmployeeName, string ElementCode, decimal? Units, decimal? Amount, string? Error);

/// <summary>The preview of a variable-input import: every line, with its error if any.</summary>
/// <param name="Lines">The lines.</param>
public sealed record VariableImportPreview(IReadOnlyList<VariableImportLine> Lines)
{
    /// <summary>Gets the number of lines with errors.</summary>
    public int ErrorCount => Lines.Count(l => l.Error is not null);
}

/// <summary>
/// Variable input for a pay period (brief §4.15.6): overtime, absences and once-off earnings or deductions, keyed one at a time or
/// imported from a file with a preview-and-error step first - the CSV template ("employee number, element code, units, amount"), the same with
/// named columns in any order, or a sheet with a row per employee and a column per pay element (see <see cref="Payroll.PayrollVariableCsv"/>).
/// The run uses the period's variables; an import replaces the period's earlier import.
/// </summary>
public interface IPayrollVariableService
{
    /// <summary>Keys one variable (replacing any manual one for the same employee, element and period).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="elementCode">The element.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="units">Units (rate-per-unit elements).</param>
    /// <param name="amount">An amount replacing the calculated one.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The variable id.</returns>
    Task<Result<long>> SetAsync(long companyId, long employeeId, string elementCode, string periodLabel, decimal? units, decimal? amount, CancellationToken ct = default);

    /// <summary>Validates an import without saving it.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="csv">The file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every line with its error, if any.</returns>
    Task<Result<VariableImportPreview>> PreviewImportAsync(long companyId, string periodLabel, string csv, CancellationToken ct = default);

    /// <summary>Imports the file when every line is valid (<c>Payroll.VariableErrors</c> otherwise), replacing the period's earlier import.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="csv">The file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of variables imported.</returns>
    Task<Result<int>> ImportAsync(long companyId, string periodLabel, string csv, CancellationToken ct = default);
}
