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
    /// Opening leave balances: <c>employee_number, leave_type, days</c> (optional <c>taken</c>) — each employee's leave owed when the system takes over,
    /// accrued as the opening and less what was taken; an employee already holding a balance of that type is skipped.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file.</param>
    /// <param name="commit">False to check only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was (or would be) imported.</returns>
    Task<ImportReport> ImportLeaveBalancesAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>Cost centres or departments: <c>code, name</c> (codes of at most three characters); codes already there are skipped.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file.</param>
    /// <param name="departments">True for departments, false for cost centres.</param>
    /// <param name="commit">False to check only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was (or would be) imported.</returns>
    Task<ImportReport> ImportDimensionsAsync(long companyId, string csv, bool departments, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Tax codes: <c>code, name, type, rate, effective_from</c> (optional <c>output_account, input_account</c>) — the type one of the tax types, the rate a
    /// percentage in force from the date; codes already there are skipped.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file.</param>
    /// <param name="commit">False to check only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was (or would be) imported.</returns>
    Task<ImportReport> ImportTaxCodesAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>
    /// The meter register: <c>serial, stand, service, reading, date</c> (optional <c>digits, multiplier</c>) — each meter goes on the register with
    /// its opening reading as an accepted actual reading and is linked to the property's active service of that type (Water, Sewer…), which must
    /// not already be metered.
    /// </summary>
    Task<ImportReport> ImportMetersAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>Students: <c>admission, name, grade, guardian</c> (customer code) (optional <c>boarder, admitted</c>).</summary>
    Task<ImportReport> ImportStudentsAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Housing stands: <c>stand, parcel, size</c> (optional <c>cost, scheme, status</c>). A parcel not yet on the register is created from its stands (area and cost
    /// their totals); nothing is posted — the land's value comes in with the trial balance.
    /// </summary>
    Task<ImportReport> ImportStandsAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>A budget: <c>account, period, amount</c> (optional <c>cost_centre, quantity</c>), created for the fiscal year containing the date.</summary>
    Task<ImportReport> ImportBudgetAsync(long companyId, string csv, DateOnly yearDate, string label, Core.Budgeting.BudgetType type, string? department, bool commit, CancellationToken ct = default);

    /// <summary>
    /// The chart of accounts: <c>code, name, type</c> (Asset, Liability, Equity, Income or Expense) (optional <c>category, manual, budget</c>; manual and budget
    /// true / false, both true when blank). Accounts already on the chart are left alone.
    /// </summary>
    Task<ImportReport> ImportAccountsAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>Warehouses and stores: <c>code, name</c>. Codes already there are left alone.</summary>
    Task<ImportReport> ImportWarehousesAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Council rental units: <c>code, description, type, rooms, rent, rent_account</c> (optional <c>vacant_since, suburb, max_occupants</c>). A unit with no
    /// vacant date is taken as let; let units get their tenancies through the rental screens. Codes already there are left alone.
    /// </summary>
    Task<ImportReport> ImportRentalUnitsAsync(long companyId, string csv, DateOnly asAt, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Ownership history of stands already on the register: <c>stand, date, change, holder</c> (optional <c>customer, consideration</c>); change is Allocation, Sale, Cession,
    /// Surrender or Repossession. The rows join each stand's hash chain in date order; a stand that already has history beyond its origination is skipped.
    /// </summary>
    Task<ImportReport> ImportOwnershipHistoryAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Waiting-list applicants with their legacy numbers and seniority: <c>id_number, name, number, registered</c> (optional <c>category, date_of_birth, resident_since,
    /// dependants, disability, income, tenure, status, marital, regime, spouse_id, spouse_name, verified</c>). An ID number already on the list is skipped.
    /// </summary>
    Task<ImportReport> ImportApplicantsAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Municipal tariffs: <c>code, name, service, type, effective, revenue_account</c> (optional <c>fixed, rate, minimum, rate_per_valuation_cents, rate_per_sqm, category, ward,
    /// blocks</c> as <c>0-10:0.5;10-30:0.8;30-:1.2</c> for stepped tariffs). A code already on the register is skipped.
    /// </summary>
    Task<ImportReport> ImportTariffsAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Price lists: <c>price_list, item, price</c> (optional <c>name, currency, min_qty, effective</c>). A price list not yet on file is created (name and currency from its
    /// first row, the company's base currency when none); an item already priced on the list at that quantity and date is skipped.
    /// </summary>
    Task<ImportReport> ImportPriceListsAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>The fixed-asset and staff-loan registers against the accounts they are held in.</summary>
    Task<Result<IReadOnlyList<RegisterCheck>>> ReconcileRegistersAsync(long companyId, CancellationToken ct = default);
}
