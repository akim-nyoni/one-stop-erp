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
    public BudgetLine AddLine(string accountCode, int period, decimal amount, string? costCentre = null, long? virementId = null)
    {
        BudgetLine line = new(accountCode, period, amount, costCentre, virementId);
        _lines.Add(line);
        return line;
    }

    /// <summary>Marks the budget approved.</summary>
    public void Approve() => Status = BudgetStatus.Approved;

    /// <summary>Activates this version as the live budget.</summary>
    public void Activate()
    {
        Status = BudgetStatus.Active;
        IsActive = true;
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
}
