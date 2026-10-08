using One4All.Core.Common;

namespace One4All.Application.Abstractions.Onboarding;

/// <summary>A sub-ledger register against its general-ledger accounts after an opening import.</summary>
/// <param name="Register">The register (fixed assets, staff loans).</param>
/// <param name="LedgerBalance">The balance of the accounts it is held in.</param>
/// <param name="RegisterBalance">The register's own total.</param>
public sealed record RegisterCheck(string Register, decimal LedgerBalance, decimal RegisterBalance)
{
    /// <summary>Whether they agree (to the cent).</summary>
    public bool Agrees => Math.Abs(LedgerBalance - RegisterBalance) < 0.01m;
}

/// <summary>
/// Opening-balance importers for the module registers (brief §10 data migration): fixed assets with their cost and depreciation to date,
/// employees, staff loan balances, municipal properties with their services, and students. Each file is validated line by line first
/// (nothing is written while any line is wrong) and committed in one unit; balances post against the migration suspense account, which
/// the trial balance import and these registers must bring back to nil.
/// </summary>
public interface IVerticalImportService
{
    /// <summary>
    /// Fixed assets: <c>asset_number, description, class, acquired, cost, accumulated, life_months, cost_account, accum_account, expense_account</c>
    /// (optional <c>residual, method, rate</c>). Depreciation to date is recorded up to the as-at month, so the next run charges the month after;
    /// one journal debits cost and credits accumulated depreciation against suspense.
    /// </summary>
    Task<ImportReport> ImportFixedAssetsAsync(long companyId, string csv, DateOnly asAt, string suspenseAccountCode, bool commit, CancellationToken ct = default);

    /// <summary>Employees: <c>employee_number, name, basic_salary, engaged</c> (optional <c>national_id, department, cost_centre, grade</c>).</summary>
    Task<ImportReport> ImportEmployeesAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>Staff loan balances: <c>employee_number, reference, outstanding, instalment, loan_account</c>; posted to the loan accounts against suspense.</summary>
    Task<ImportReport> ImportEmployeeLoansAsync(long companyId, string csv, DateOnly asAt, string suspenseAccountCode, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Municipal properties: <c>stand, ward, ratepayer</c> (customer code) (optional <c>valuation, size, connected, services</c> — e.g.
    /// <c>Water:WAT1;Refuse:REF1</c>).
    /// </summary>
    Task<ImportReport> ImportPropertiesAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Payroll year-to-date openings: <c>employee_number, tax_year, code, amount</c> — <c>TAXABLE</c> for taxable pay so far, or a pay element's code
    /// (a tax-table element's figure is the tax already deducted). Cumulative tax counts them with this payroll's own payslips; a later import
    /// of the same employee, year and code replaces the figure.
    /// </summary>
    Task<ImportReport> ImportPayrollYtdAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>
    /// The meter register: <c>serial, stand, service, reading, date</c> (optional <c>digits, multiplier</c>) — each meter goes on the register with
    /// its opening reading as an accepted actual reading and is linked to the property's active service of that type (Water, Sewer…), which must
    /// not already be metered.
    /// </summary>
    Task<ImportReport> ImportMetersAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>Students: <c>admission, name, grade, guardian</c> (customer code) (optional <c>boarder, admitted</c>).</summary>
    Task<ImportReport> ImportStudentsAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>The fixed-asset and staff-loan registers against the accounts they are held in.</summary>
    Task<Result<IReadOnlyList<RegisterCheck>>> ReconcileRegistersAsync(long companyId, CancellationToken ct = default);
}
