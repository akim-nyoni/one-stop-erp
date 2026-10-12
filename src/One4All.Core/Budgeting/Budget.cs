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

    /// <summary>The base-case scenario.</summary>
    BaseCase = 7,

    /// <summary>The best-case scenario.</summary>
    BestCase = 8,

    /// <summary>The worst-case scenario.</summary>
    WorstCase = 9,

    /// <summary>A zero-based budget (every line justified from nil).</summary>
    ZeroBased = 10,

    /// <summary>A rolling forecast.</summary>
    RollingForecast = 11,

    /// <summary>A project budget.</summary>
    Project = 12,

    /// <summary>An establishment / staff budget.</summary>
    Establishment = 13,
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

    /// <summary>Finance is reviewing it (may annotate, propose figures, return or recommend).</summary>
    UnderReview = 6,

    /// <summary>Sent back to the department to accept the proposals or revise, then resubmit.</summary>
    Returned = 7,

    /// <summary>Recommended by the committee/executive for approval.</summary>
    Recommended = 8,

    /// <summary>A superseded version put away (its seal verified); retrievable exactly as approved.</summary>
    Archived = 9,

    /// <summary>Archived and locked: nothing about it may change again.</summary>
    Locked = 10,
}

/// <summary>
/// A budget version (brief §4.18, <c>bud.budget</c>). Budgets live in their own schema and never post to
/// the general ledger — they are joined to actuals by posting dimensions at report time. Versions are
/// retained permanently; a new version supersedes the previous active one for the same year/type/department.
/// </summary>
public sealed class Budget : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<BudgetLine> _lines = [];
    private readonly List<BudgetOuterYearLine> _outerYears = [];

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

    /// <summary>The currency the budget is prepared in, when not the base currency (brief §4.16 header currency).</summary>
    public CurrencyCode? Currency { get; private set; }

    /// <summary>The budget rate (base per unit of <see cref="Currency"/>) its base equivalents are kept at.</summary>
    public decimal? BudgetRate { get; private set; }

    /// <summary>
    /// Prepares the budget in a currency at a budget rate. Lines planned in the budget currency (or with no currency of their own) keep their
    /// budget-currency amount and have their base equivalent re-stated at the new rate; lines in another currency and driver-based lines keep theirs.
    /// Clearing the currency leaves every line at its base amount.
    /// </summary>
    /// <param name="currency">The currency, or null for the base currency.</param>
    /// <param name="rate">The budget rate.</param>
    public void SetCurrency(CurrencyCode? currency, decimal? rate)
    {
        foreach (BudgetLine line in _lines.Where(l => l.DriverCode is null))
        {
            bool onHeader = Currency is not null && line.Currency == Currency && line.PlannedRate == BudgetRate;
            if (currency is CurrencyCode to && rate is decimal r && (onHeader || line.Currency is null))
            {
                decimal inCurrency = onHeader ? line.Amount / BudgetRate!.Value : line.Amount / r;
                line.SetAmount(decimal.Round(inCurrency * r, 2, MidpointRounding.AwayFromZero));
                line.Currency = to;
                line.PlannedRate = r;
            }
            else if (currency is null && onHeader)
            {
                line.Currency = null;
                line.PlannedRate = null;
            }
        }

        Currency = currency;
        BudgetRate = currency is null ? null : rate;
    }

    /// <summary>
    /// The seal: a SHA-256 hash of the lines as approved, set when the budget is approved or activated, so an approved
    /// budget can be proved unaltered years later (brief §4.18.3). Virement lines are not part of it.
    /// </summary>
    public string? SealHash { get; private set; }

    /// <summary>Whether the budget is sealed, after which its approved lines cannot change.</summary>
    public bool IsSealed => SealHash is not null;

    /// <summary>Gets the negotiation round (incremented at each submission).</summary>
    public int Round { get; private set; }

    /// <summary>Gets who last submitted it.</summary>
    public long? SubmittedByUserId { get; private set; }

    /// <summary>Gets who approved it.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>Gets the board or council minute/resolution approving it.</summary>
    public string? ResolutionReference { get; private set; }

    /// <summary>Moves it through preparation (the service checks the transition is allowed).</summary>
    /// <param name="status">The new status.</param>
    public void MoveTo(BudgetStatus status) => Status = status;

    /// <summary>Records a submission, starting a new negotiation round.</summary>
    /// <param name="userId">Who submitted.</param>
    public void Submit(long userId)
    {
        Round++;
        SubmittedByUserId = userId;
        Status = BudgetStatus.Submitted;
    }

    /// <summary>Approves it with the resolution that did so, sealing the lines.</summary>
    /// <param name="userId">Who approved.</param>
    /// <param name="resolutionReference">The minute or resolution.</param>
    public void Approve(long userId, string resolutionReference)
    {
        ApprovedByUserId = userId;
        ResolutionReference = resolutionReference;
        Approve();
    }

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
    /// <param name="rephaseId">The approved re-phasing adding the line, if any.</param>
    /// <param name="carryForwardId">The approved capital carry-forward adding the line, if any.</param>
    public BudgetLine AddLine(string accountCode, int period, decimal amount, string? costCentre = null, long? virementId = null, long? rephaseId = null, long? carryForwardId = null)
    {
        if (IsSealed && virementId is null && rephaseId is null && carryForwardId is null)
        {
            throw new InvalidOperationException("An approved budget cannot be edited; create a new version or a virement.");
        }

        if (IsPutAway)
        {
            throw new InvalidOperationException($"Budget {Label} v{VersionNumber} is {Status}; nothing about it can change.");
        }

        BudgetLine line = new(accountCode, period, amount, costCentre, virementId) { RephaseId = rephaseId, CarryForwardId = carryForwardId };
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
        foreach (BudgetLine line in _lines.Where(l => l.VirementId is null && l.RephaseId is null && l.CarryForwardId is null)
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

    /// <summary>Whether it is archived or locked (closed to every change).</summary>
    public bool IsPutAway => Status is BudgetStatus.Archived or BudgetStatus.Locked;

    /// <summary>The budget this one was rolled forward from (multi-year framework), if any.</summary>
    public long? RolledFromBudgetId { get; private set; }

    /// <summary>Records the framework it was rolled forward from.</summary>
    /// <param name="budgetId">The earlier year's budget.</param>
    public void MarkRolledFrom(long budgetId) => RolledFromBudgetId = budgetId;

    /// <summary>The indicative outer years of the multi-year framework (year 1 is the budget's own lines).</summary>
    public IReadOnlyList<BudgetOuterYearLine> OuterYears => _outerYears.AsReadOnly();

    /// <summary>Replaces the outer years (only before the budget is sealed).</summary>
    /// <param name="lines">The lines.</param>
    public void SetOuterYears(IEnumerable<BudgetOuterYearLine> lines)
    {
        if (IsSealed || IsPutAway)
        {
            throw new InvalidOperationException("An approved budget's framework cannot be edited; roll it forward or create a new version.");
        }

        _outerYears.Clear();
        _outerYears.AddRange(lines);
    }

    /// <summary>Archives a superseded version, provided its approved lines still match the seal.</summary>
    /// <returns>Null when archived, else why not.</returns>
    public string? Archive()
    {
        if (Status != BudgetStatus.Superseded)
        {
            return $"Only a superseded budget can be archived; this one is {Status}.";
        }

        if (IsSealed && !VerifySeal())
        {
            return "Its lines no longer match the approval seal; investigate before archiving.";
        }

        Status = BudgetStatus.Archived;
        return null;
    }

    /// <summary>Locks an archived version for good.</summary>
    /// <returns>Null when locked, else why not.</returns>
    public string? Lock()
    {
        if (Status != BudgetStatus.Archived)
        {
            return $"Only an archived budget can be locked; this one is {Status}.";
        }

        Status = BudgetStatus.Locked;
        return null;
    }
}

/// <summary>One budget line — account × period × amount (<c>bud.budget_line</c>).</summary>
public sealed class BudgetLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BudgetLine() => AccountCode = string.Empty;

    /// <summary>Changes the amount during preparation (never once the budget is sealed: the service checks).</summary>
    /// <param name="amount">The new amount.</param>
    public void SetAmount(decimal amount) => Amount = amount;

    /// <summary>Creates a line.</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="period">The period.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="costCentre">The cost centre.</param>
    /// <param name="virementId">The virement that added it, if any.</param>
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

    /// <summary>Gets the approved re-phasing that added this line (a timing move, never a change of value).</summary>
    public long? RephaseId { get; init; }

    /// <summary>Gets the approved capital carry-forward that added this line, if any.</summary>
    public long? CarryForwardId { get; init; }

    /// <summary>Gets the project the line budgets for.</summary>
    public string? Project { get; private set; }

    /// <summary>Gets the fund (vote) the line budgets for.</summary>
    public string? Fund { get; private set; }

    /// <summary>Gets the branch the line budgets for.</summary>
    public string? Branch { get; private set; }

    /// <summary>Keys the line on its project, fund and branch (codes upper-cased; blank for none).</summary>
    /// <param name="project">The project.</param>
    /// <param name="fund">The fund.</param>
    /// <param name="branch">The branch.</param>
    public void SetAnalysis(string? project, string? fund, string? branch)
    {
        static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        Project = Code(project);
        Fund = Code(fund);
        Branch = Code(branch);
    }

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

/// <summary>What happened in a budget's preparation.</summary>
public enum BudgetReviewAction
{
    /// <summary>The department submitted it.</summary>
    Submitted = 1,

    /// <summary>Finance started reviewing it.</summary>
    ReviewStarted = 2,

    /// <summary>Finance proposed figures.</summary>
    Proposed = 3,

    /// <summary>Returned to the department.</summary>
    Returned = 4,

    /// <summary>The department accepted Finance's proposals.</summary>
    ProposalsAccepted = 5,

    /// <summary>The committee/executive recommended it.</summary>
    Recommended = 6,

    /// <summary>The board or council approved it.</summary>
    Approved = 7,

    /// <summary>The department revised a figure.</summary>
    Revised = 8,
}

/// <summary>One step in a budget's preparation (<c>bud.review_event</c>); the trail is never rewritten.</summary>
public sealed class BudgetReviewEvent : Entity
{
    private BudgetReviewEvent()
    {
    }

    /// <summary>Records the step.</summary>
    /// <param name="budgetId">The budget.</param>
    /// <param name="round">The negotiation round.</param>
    /// <param name="action">What happened.</param>
    /// <param name="userId">Who.</param>
    /// <param name="occurredOnUtc">When.</param>
    /// <param name="comment">Why, or notes.</param>
    public BudgetReviewEvent(long budgetId, int round, BudgetReviewAction action, long userId, DateTimeOffset occurredOnUtc, string? comment)
    {
        BudgetId = budgetId;
        Round = round;
        Action = action;
        UserId = userId;
        OccurredOnUtc = occurredOnUtc;
        Comment = comment;
    }

    /// <summary>Gets the budget.</summary>
    public long BudgetId { get; private set; }

    /// <summary>Gets the round.</summary>
    public int Round { get; private set; }

    /// <summary>Gets what happened.</summary>
    public BudgetReviewAction Action { get; private set; }

    /// <summary>Gets who.</summary>
    public long UserId { get; private set; }

    /// <summary>Gets when.</summary>
    public DateTimeOffset OccurredOnUtc { get; private set; }

    /// <summary>Gets the comment.</summary>
    public string? Comment { get; private set; }
}

/// <summary>A figure Finance proposed against the department's request in a round (<c>bud.proposal</c>); both are kept.</summary>
public sealed class BudgetProposal : Entity
{
    private BudgetProposal() => AccountCode = string.Empty;

    /// <summary>Records the proposal.</summary>
    /// <param name="budgetId">The budget.</param>
    /// <param name="round">The round.</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="costCentre">The cost centre.</param>
    /// <param name="period">The period.</param>
    /// <param name="requested">The department's figure.</param>
    /// <param name="proposed">Finance's figure.</param>
    /// <param name="comment">Why.</param>
    public BudgetProposal(long budgetId, int round, string accountCode, string? costCentre, int period, decimal requested, decimal proposed, string? comment)
    {
        BudgetId = budgetId;
        Round = round;
        AccountCode = accountCode;
        CostCentre = costCentre;
        Period = period;
        Requested = requested;
        Proposed = proposed;
        Comment = comment;
    }

    /// <summary>Gets the budget.</summary>
    public long BudgetId { get; private set; }

    /// <summary>Gets the round.</summary>
    public int Round { get; private set; }

    /// <summary>Gets the account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>Gets the cost centre.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>Gets the period.</summary>
    public int Period { get; private set; }

    /// <summary>Gets the department's figure.</summary>
    public decimal Requested { get; private set; }

    /// <summary>Gets Finance's figure.</summary>
    public decimal Proposed { get; private set; }

    /// <summary>Gets why.</summary>
    public string? Comment { get; private set; }
}

/// <summary>Where a re-phasing request stands.</summary>
public enum RephaseStatus
{
    /// <summary>Awaiting a decision.</summary>
    Pending = 0,

    /// <summary>Approved and applied.</summary>
    Approved = 1,

    /// <summary>Rejected.</summary>
    Rejected = 2,
}

/// <summary>
/// A re-phasing of an approved budget (<c>bud.rephase</c>, brief §4.18.6): moves an amount on an account from one period to another
/// within the year — a timing change kept apart from virements, with its own request and approval.
/// </summary>
public sealed class BudgetRephase : Entity, ICompanyScoped
{
    private BudgetRephase()
    {
        AccountCode = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="costCentre">The cost centre.</param>
    /// <param name="fromPeriod">The period giving the amount.</param>
    /// <param name="toPeriod">The period receiving it.</param>
    /// <param name="amount">The amount moved.</param>
    /// <param name="reason">Why.</param>
    /// <param name="requestedByUserId">Who asked.</param>
    public BudgetRephase(long companyId, long budgetId, string accountCode, string? costCentre, int fromPeriod, int toPeriod, decimal amount, string reason, long requestedByUserId)
    {
        CompanyId = companyId;
        BudgetId = budgetId;
        AccountCode = accountCode;
        CostCentre = costCentre;
        FromPeriod = fromPeriod;
        ToPeriod = toPeriod;
        Amount = amount;
        Reason = reason;
        RequestedByUserId = requestedByUserId;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the budget.</summary>
    public long BudgetId { get; private set; }

    /// <summary>Gets the account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>Gets the cost centre.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>Gets the period giving the amount.</summary>
    public int FromPeriod { get; private set; }

    /// <summary>Gets the period receiving it.</summary>
    public int ToPeriod { get; private set; }

    /// <summary>Gets the amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets why.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Gets or sets the status.</summary>
    public RephaseStatus Status { get; set; }

    /// <summary>Gets or sets who decided.</summary>
    public long? DecidedByUserId { get; set; }
}

/// <summary>An indicative outer-year amount in a multi-year (MTEF) framework (<c>bud.budget_outer_year</c>): year 2 is offset 1, and so on.</summary>
public sealed class BudgetOuterYearLine : Entity
{
    private BudgetOuterYearLine() => AccountCode = string.Empty;

    /// <summary>Creates the line.</summary>
    /// <param name="yearOffset">Years after the budget year (1–4).</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="costCentre">The cost centre, if any.</param>
    /// <param name="amount">The indicative annual amount.</param>
    public BudgetOuterYearLine(int yearOffset, string accountCode, string? costCentre, decimal amount)
    {
        YearOffset = yearOffset;
        AccountCode = accountCode;
        CostCentre = costCentre;
        Amount = amount;
    }

    /// <summary>The budget.</summary>
    public long BudgetId { get; private set; }

    /// <summary>Years after the budget year (1–4).</summary>
    public int YearOffset { get; private set; }

    /// <summary>The account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The cost centre, if any.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>The indicative annual amount.</summary>
    public decimal Amount { get; private set; }
}
