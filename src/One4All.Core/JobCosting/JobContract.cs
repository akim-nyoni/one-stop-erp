using One4All.Core.Common;

namespace One4All.Core.JobCosting;

/// <summary>The lifecycle of a construction contract/job (brief §4.12).</summary>
public enum JobStatus
{
    /// <summary>Open and in progress.</summary>
    Open = 1,

    /// <summary>Practically complete (defects-liability period).</summary>
    Completed = 2,

    /// <summary>Closed.</summary>
    Closed = 3,
}

/// <summary>The source a captured job cost came from (brief §4.12).</summary>
public enum JobCostSource
{
    /// <summary>A purchase invoice (materials/services).</summary>
    PurchaseInvoice = 1,

    /// <summary>A stock issue to the job.</summary>
    StockIssue = 2,

    /// <summary>Labour / timesheets.</summary>
    Labour = 3,

    /// <summary>Plant &amp; equipment internal hire.</summary>
    Plant = 4,

    /// <summary>A subcontractor certificate.</summary>
    Subcontractor = 5,

    /// <summary>An overhead allocation.</summary>
    Overhead = 6,

    /// <summary>Goods received on a purchase order line for the job (delivered to site).</summary>
    GoodsReceived = 7,
}

/// <summary>
/// An overhead recovery rate (<c>job.overhead_rate</c>, brief §4.12): a percentage added to the job on top of a kind of direct cost (e.g. 35%
/// on labour, 10% on plant) as the cost is captured, under its own cost head.
/// </summary>
public sealed class JobOverheadRate : AuditableEntity, ICompanyScoped
{
    private JobOverheadRate()
    {
        CostHead = string.Empty;
    }

    /// <summary>Creates the rate.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="appliesTo">The direct cost it is charged on.</param>
    /// <param name="percent">The percentage.</param>
    /// <param name="costHead">The cost head the overhead goes to.</param>
    public JobOverheadRate(long companyId, JobCostSource appliesTo, decimal percent, string costHead)
    {
        CompanyId = companyId;
        AppliesTo = appliesTo;
        Percent = percent;
        CostHead = costHead;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The direct cost it is charged on.</summary>
    public JobCostSource AppliesTo { get; private set; }

    /// <summary>The percentage.</summary>
    public decimal Percent { get; set; }

    /// <summary>The cost head the overhead goes to.</summary>
    public string CostHead { get; set; }
}

/// <summary>
/// A construction contract / job (brief §4.12, <c>job.contract</c>). Carries the contract value and
/// retention terms, accumulates captured costs for profitability, and is the subject of progress
/// valuations / interim payment certificates that post to AR.
/// </summary>
public sealed class JobContract : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<JobCostEntry> _costs = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private JobContract()
    {
        ContractNumber = string.Empty;
        Name = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates a contract.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractNumber">The contract number.</param>
    /// <param name="name">The contract name.</param>
    /// <param name="customerId">The client (customer) account.</param>
    /// <param name="contractValue">The contract value.</param>
    /// <param name="budgetCost">The budgeted cost.</param>
    /// <param name="retentionPercent">The retention percentage held from each certificate.</param>
    /// <param name="revenueAccountCode">The contract-revenue account.</param>
    public JobContract(long companyId, string contractNumber, string name, long customerId, decimal contractValue, decimal budgetCost, decimal retentionPercent, string revenueAccountCode)
    {
        CompanyId = companyId;
        ContractNumber = contractNumber.Trim().ToUpperInvariant();
        Name = name;
        CustomerId = customerId;
        ContractValue = contractValue;
        BudgetCost = budgetCost;
        RetentionPercent = retentionPercent;
        RevenueAccountCode = revenueAccountCode;
        Status = JobStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The contract number.</summary>
    public string ContractNumber { get; private set; }

    /// <summary>The contract name.</summary>
    public string Name { get; set; }

    /// <summary>The client (customer) account.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The contract value.</summary>
    public decimal ContractValue { get; set; }

    /// <summary>The budgeted cost.</summary>
    public decimal BudgetCost { get; set; }

    /// <summary>The retention percentage held from each certificate.</summary>
    public decimal RetentionPercent { get; private set; }

    /// <summary>The contract-revenue account.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The status.</summary>
    public JobStatus Status { get; private set; }

    /// <summary>The cumulative work certified to date (gross of retention).</summary>
    public decimal CertifiedToDate { get; private set; }

    /// <summary>The cumulative retention held to date.</summary>
    public decimal RetentionHeld { get; private set; }

    /// <summary>
    /// The cumulative percentage-of-completion revenue adjustment posted to date: earned revenue less gross
    /// certified. Positive = a contract asset (under-billed), negative = a contract liability (over-billed).
    /// </summary>
    public decimal RevenueAdjustmentToDate { get; private set; }

    /// <summary>The cumulative onerous-contract (expected loss) provision carried to date (IAS 37).</summary>
    public decimal LossProvisionToDate { get; private set; }

    /// <summary>The expected loss on the contract: budget cost over contract value, 0 when profitable.</summary>
    public decimal ExpectedLoss => Math.Max(0m, BudgetCost - ContractValue);

    /// <summary>The captured cost entries.</summary>
    public IReadOnlyList<JobCostEntry> Costs => _costs.AsReadOnly();

    /// <summary>The total actual cost captured to date.</summary>
    public decimal ActualCost => _costs.Sum(c => c.Amount);

    /// <summary>The percentage complete on a cost-to-cost basis (IAS 11/IFRS 15 input method).</summary>
    public decimal PercentComplete => BudgetCost <= 0m ? 0m : decimal.Round(ActualCost / BudgetCost * 100m, 2, MidpointRounding.ToEven);

    /// <summary>Captures an actual cost against the job (for profitability; the GL was hit at source).</summary>
    /// <param name="costHead">The cost head / activity.</param>
    /// <param name="source">Where the cost came from.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="date">The date.</param>
    /// <param name="reference">A reference.</param>
    /// <returns>The created entry.</returns>
    public JobCostEntry CaptureCost(string costHead, JobCostSource source, decimal amount, DateOnly date, string? reference)
    {
        JobCostEntry entry = new(CompanyId, costHead, source, amount, date, reference);
        _costs.Add(entry);
        return entry;
    }

    /// <summary>Records the effect of a progress certificate on the running totals.</summary>
    /// <param name="grossThisCertificate">The gross value certified this time.</param>
    /// <param name="retentionThisCertificate">The retention held this time.</param>
    public void ApplyCertificate(decimal grossThisCertificate, decimal retentionThisCertificate)
    {
        CertifiedToDate += grossThisCertificate;
        RetentionHeld += retentionThisCertificate;
    }

    /// <summary>Reduces the retention held when it is released.</summary>
    /// <param name="amount">The amount released.</param>
    public void ReleaseRetention(decimal amount) => RetentionHeld -= amount;

    /// <summary>Records the cumulative revenue adjustment after a percentage-of-completion run.</summary>
    /// <param name="cumulativeAdjustment">The new cumulative adjustment (earned − certified).</param>
    public void SetRevenueAdjustment(decimal cumulativeAdjustment) => RevenueAdjustmentToDate = cumulativeAdjustment;

    /// <summary>Revises the forecast total cost (budget) of the contract. Refuses a negative budget or one below cost already incurred.</summary>
    /// <param name="newBudgetCost">The revised forecast cost to complete the whole contract.</param>
    /// <returns>Success or the rule violated.</returns>
    public Result ReviseBudget(decimal newBudgetCost)
    {
        if (newBudgetCost < 0m)
        {
            return Result.Failure(Error.Validation("Job.BudgetNegative", "A contract budget cannot be negative."));
        }

        if (newBudgetCost < ActualCost)
        {
            return Result.Failure(Error.Rule("Job.BudgetBelowActual", $"The revised budget {newBudgetCost:0.00} is below the cost already incurred {ActualCost:0.00}."));
        }

        BudgetCost = newBudgetCost;
        return Result.Success();
    }

    /// <summary>Records the cumulative expected-loss provision after a provision run.</summary>
    /// <param name="cumulativeProvision">The provision now carried (≥ 0).</param>
    public void SetLossProvision(decimal cumulativeProvision) => LossProvisionToDate = cumulativeProvision;
}

/// <summary>A captured actual cost against a job (<c>job.cost_entry</c>).</summary>
public sealed class JobCostEntry : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private JobCostEntry() => CostHead = string.Empty;

    /// <summary>Creates a cost entry.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="costHead">The cost head / activity.</param>
    /// <param name="source">Where the cost came from.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="date">The date.</param>
    /// <param name="reference">A reference.</param>
    public JobCostEntry(long companyId, string costHead, JobCostSource source, decimal amount, DateOnly date, string? reference)
    {
        CompanyId = companyId;
        CostHead = costHead;
        Source = source;
        Amount = amount;
        Date = date;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The owning contract id.</summary>
    public long JobContractId { get; private set; }

    /// <summary>The cost head / activity.</summary>
    public string CostHead { get; private set; }

    /// <summary>Where the cost came from.</summary>
    public JobCostSource Source { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>A reference.</summary>
    public string? Reference { get; private set; }
}
