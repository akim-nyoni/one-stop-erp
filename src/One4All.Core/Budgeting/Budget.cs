using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Budgeting;

/// <summary>A budget type / scenario (brief §4.18.1); many coexist for the same year.</summary>
public enum BudgetType
{
    /// <summary>The original / approved budget.</summary>
    OriginalApproved = 1,

    /// <summary>A supplementary / revised budget.</summary>
    SupplementaryRevised = 2,

    /// <summary>A reforecast.</summary>
    Reforecast = 3,

    /// <summary>A draft / submission.</summary>
    DraftSubmission = 4,

    /// <summary>A capital budget.</summary>
    Capital = 5,

    /// <summary>A cash budget.</summary>
    Cash = 6,
}

/// <summary>The lifecycle of a budget version (brief §4.18.4).</summary>
public enum BudgetStatus
{
    /// <summary>Being prepared.</summary>
    Draft = 1,

    /// <summary>Submitted for review.</summary>
    Submitted = 2,

    /// <summary>Approved.</summary>
    Approved = 3,

    /// <summary>Active (the live budget for control and reporting).</summary>
    Active = 4,

    /// <summary>Superseded by a later version (retained, never deleted).</summary>
    Superseded = 5,
}

/// <summary>
/// A budget version (brief §4.18, <c>bud.budget</c>). Budgets live in their own schema and never post to
/// the general ledger — they are joined to actuals by posting dimensions at report time. Versions are
/// retained permanently; a new version supersedes the previous active one for the same year/type/department.
/// </summary>
public sealed class Budget : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<BudgetLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Budget()
    {
        Label = string.Empty;
    }

    /// <summary>Creates a budget version.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="label">The budget label.</param>
    /// <param name="type">The budget type/scenario.</param>
    /// <param name="department">The preparing department (null for company-wide).</param>
    /// <param name="versionNumber">The version number.</param>
    public Budget(long companyId, long fiscalYearId, string label, BudgetType type, string? department, int versionNumber)
    {
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        Label = label;
        Type = type;
        Department = department;
        VersionNumber = versionNumber;
        Status = BudgetStatus.Draft;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The fiscal year.</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>The budget label.</summary>
    public string Label { get; set; }

    /// <summary>The budget type/scenario.</summary>
    public BudgetType Type { get; private set; }

    /// <summary>The preparing department (null for company-wide).</summary>
    public string? Department { get; private set; }

    /// <summary>The version number.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>The status.</summary>
    public BudgetStatus Status { get; private set; }

    /// <summary>Whether this is the active version.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The reason for this version (audit trail).</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// The seal: a SHA-256 hash of the lines as approved, set when the budget is approved or activated, so an approved
    /// budget can be proved unaltered years later (brief §4.18.3). Virement lines are not part of it.
    /// </summary>
    public string? SealHash { get; private set; }

    /// <summary>Whether the budget is sealed, after which its approved lines cannot change.</summary>
    public bool IsSealed => SealHash is not null;

    /// <summary>The budget lines.</summary>
    public IReadOnlyList<BudgetLine> Lines => _lines.AsReadOnly();

    /// <summary>The total annual budget across all lines.</summary>
    public decimal AnnualTotal => _lines.Sum(l => l.Amount);

    /// <summary>Adds a budget line (account × period × amount).</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="period">The period (month 1–12).</param>
    /// <param name="amount">The budgeted amount.</param>
    /// <param name="costCentre">The cost centre, if any.</param>
    /// <returns>The created line.</returns>
    /// <param name="virementId">The approved virement adding the line, if any.</param>
    /// <exception cref="InvalidOperationException">Thrown when adding an ordinary line to a sealed budget: changes go in a new version or a virement.</exception>
    public BudgetLine AddLine(string accountCode, int period, decimal amount, string? costCentre = null, long? virementId = null)
    {
        if (IsSealed && virementId is null)
        {
            throw new InvalidOperationException("An approved budget cannot be edited; create a new version or a virement.");
        }

        BudgetLine line = new(accountCode, period, amount, costCentre, virementId);
        _lines.Add(line);
        return line;
    }

    /// <summary>Marks the budget approved and seals its lines.</summary>
    public void Approve()
    {
        Status = BudgetStatus.Approved;
        SealHash ??= ComputeSeal();
    }

    /// <summary>Activates this version as the live budget, sealing it if it was not already.</summary>
    public void Activate()
    {
        Status = BudgetStatus.Active;
        IsActive = true;
        SealHash ??= ComputeSeal();
    }

    /// <summary>Whether the approved lines still match the seal.</summary>
    /// <returns>True when sealed and unaltered.</returns>
    public bool VerifySeal() => SealHash is not null && SealHash == ComputeSeal();

    // The approved lines in a fixed order and format, hashed: virement lines are separate documents and left out.
    private string ComputeSeal()
    {
        StringBuilder canonical = new();
        canonical.Append(CultureInfo.InvariantCulture, $"{CompanyId}|{FiscalYearId}|{(int)Type}|{Department}|{VersionNumber}\n");
        foreach (BudgetLine line in _lines.Where(l => l.VirementId is null)
                     .OrderBy(l => l.AccountCode, StringComparer.Ordinal).ThenBy(l => l.CostCentre, StringComparer.Ordinal).ThenBy(l => l.Period).ThenBy(l => l.Amount))
        {
            canonical.Append(CultureInfo.InvariantCulture, $"{line.AccountCode}|{line.CostCentre}|{line.Period}|{line.Amount:0.0000}\n");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>Supersedes this version (retained, never deleted).</summary>
    public void Supersede()
    {
        Status = BudgetStatus.Superseded;
        IsActive = false;
    }
}

/// <summary>One budget line — account × period × amount (<c>bud.budget_line</c>).</summary>
public sealed class BudgetLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BudgetLine() => AccountCode = string.Empty;

    /// <summary>Creates a budget line.</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="period">The period (month 1–12).</param>
    /// <param name="amount">The budgeted amount.</param>
    /// <param name="costCentre">The cost centre, if any.</param>
    /// <param name="virementId">The approved virement that added the line, if any.</param>
    public BudgetLine(string accountCode, int period, decimal amount, string? costCentre, long? virementId = null)
    {
        AccountCode = accountCode;
        Period = period;
        Amount = amount;
        CostCentre = costCentre;
        VirementId = virementId;
    }

    /// <summary>The owning budget id.</summary>
    public long BudgetId { get; private set; }

    /// <summary>The account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The cost centre, if any.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>The period (month 1–12).</summary>
    public int Period { get; private set; }

    /// <summary>The budgeted amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The approved virement that added the line; null for the budget as approved.</summary>
    public long? VirementId { get; private set; }

    /// <summary>Gets or sets how the amount was arrived at.</summary>
    public BudgetBasis Basis { get; set; }

    /// <summary>Gets or sets the driver the line is built from (driver-based lines).</summary>
    public string? DriverCode { get; set; }

    /// <summary>Gets or sets the budgeted volume behind the amount (units, tonnes, hours…), for the volume / price split (B35).</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Gets or sets the currency the line was planned in, when it is bought or sold in a foreign currency.</summary>
    public CurrencyCode? Currency { get; set; }

    /// <summary>Gets or sets the planned rate (base per unit of <see cref="Currency"/>) the amount was budgeted at.</summary>
    public decimal? PlannedRate { get; set; }
}

/// <summary>
/// An actual volume against a budgeted account (<c>bud.actual_volume</c>): units sold, tonnes hauled, hours worked — what the volume
/// variance (B35) compares with the budget lines' quantities.
/// </summary>
public sealed class ActualVolume : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ActualVolume()
    {
        AccountCode = string.Empty;
        Reference = string.Empty;
    }

    /// <summary>Creates the record.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="accountCode">The budgeted account.</param>
    /// <param name="department">The department, if the budget is departmental.</param>
    /// <param name="date">The date.</param>
    /// <param name="quantity">The volume.</param>
    /// <param name="reference">Where the figure came from.</param>
    public ActualVolume(long companyId, string accountCode, string? department, DateOnly date, decimal quantity, string reference)
    {
        CompanyId = companyId;
        AccountCode = accountCode;
        Department = department;
        Date = date;
        Quantity = quantity;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The budgeted account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The department, if any.</summary>
    public string? Department { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The volume.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Where the figure came from.</summary>
    public string Reference { get; private set; }
}
