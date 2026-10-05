using One4All.Core.Common;

namespace One4All.Core.JobCosting;

/// <summary>Where a variation order stands.</summary>
public enum VariationStatus
{
    /// <summary>Submitted to the client, awaiting a decision.</summary>
    Submitted = 0,

    /// <summary>Approved: the contract value and budget move.</summary>
    Approved = 1,

    /// <summary>Rejected.</summary>
    Rejected = 2,
}

/// <summary>A variation to a contract's scope (<c>job.variation_order</c>).</summary>
public sealed class VariationOrder : AuditableEntity, ICompanyScoped
{
    private VariationOrder()
    {
        Number = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Submits a variation.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="jobContractId">The contract.</param>
    /// <param name="number">The VO number.</param>
    /// <param name="description">What changes.</param>
    /// <param name="valueImpact">The change to the contract value (may be negative).</param>
    /// <param name="costImpact">The change to the budgeted cost (may be negative).</param>
    /// <param name="submittedOn">When it was submitted.</param>
    public VariationOrder(long companyId, long jobContractId, string number, string description, decimal valueImpact, decimal costImpact, DateOnly submittedOn)
    {
        CompanyId = companyId;
        JobContractId = jobContractId;
        Number = number.Trim().ToUpperInvariant();
        Description = description;
        ValueImpact = valueImpact;
        CostImpact = costImpact;
        SubmittedOn = submittedOn;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the contract.</summary>
    public long JobContractId { get; private set; }

    /// <summary>Gets the VO number.</summary>
    public string Number { get; private set; }

    /// <summary>Gets what changes.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the change to the contract value.</summary>
    public decimal ValueImpact { get; private set; }

    /// <summary>Gets the change to the budgeted cost.</summary>
    public decimal CostImpact { get; private set; }

    /// <summary>Gets when it was submitted.</summary>
    public DateOnly SubmittedOn { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public VariationStatus Status { get; private set; }

    /// <summary>Gets when it was decided.</summary>
    public DateOnly? DecidedOn { get; private set; }

    /// <summary>Gets who decided it.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>Gets the decision note.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>Records the decision.</summary>
    /// <param name="approved">Whether it was approved.</param>
    /// <param name="date">When.</param>
    /// <param name="userId">Who decided.</param>
    /// <param name="note">Any note.</param>
    public void Decide(bool approved, DateOnly date, long? userId, string? note)
    {
        Status = approved ? VariationStatus.Approved : VariationStatus.Rejected;
        DecidedOn = date;
        DecidedByUserId = userId;
        DecisionNote = note;
    }
}

/// <summary>A bill-of-quantities line with the quantity measured against it (<c>job.boq_item</c>).</summary>
public sealed class BoqItem : Entity, ICompanyScoped
{
    private BoqItem()
    {
        ItemCode = string.Empty;
        Description = string.Empty;
        Unit = string.Empty;
    }

    /// <summary>Creates a BOQ line.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="jobContractId">The contract.</param>
    /// <param name="itemCode">The BOQ item reference.</param>
    /// <param name="description">The work.</param>
    /// <param name="unit">The unit of measure.</param>
    /// <param name="quantity">The billed quantity.</param>
    /// <param name="rate">The rate per unit.</param>
    public BoqItem(long companyId, long jobContractId, string itemCode, string description, string unit, decimal quantity, decimal rate)
    {
        CompanyId = companyId;
        JobContractId = jobContractId;
        ItemCode = itemCode.Trim().ToUpperInvariant();
        Description = description;
        Unit = unit;
        Quantity = quantity;
        Rate = rate;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the contract.</summary>
    public long JobContractId { get; private set; }

    /// <summary>Gets the BOQ item reference.</summary>
    public string ItemCode { get; private set; }

    /// <summary>Gets the work.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the unit.</summary>
    public string Unit { get; private set; }

    /// <summary>Gets the billed quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the rate.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Gets the quantity measured to date.</summary>
    public decimal MeasuredQuantity { get; private set; }

    /// <summary>Gets when it was last measured.</summary>
    public DateOnly? MeasuredOn { get; private set; }

    /// <summary>Records the cumulative measured quantity.</summary>
    /// <param name="quantityToDate">The quantity measured to date.</param>
    /// <param name="date">When.</param>
    public void Measure(decimal quantityToDate, DateOnly date)
    {
        MeasuredQuantity = quantityToDate;
        MeasuredOn = date;
    }
}

/// <summary>Hours an employee worked on a contract (<c>job.timesheet</c>), costed into the job.</summary>
public sealed class JobTimesheet : Entity, ICompanyScoped
{
    private JobTimesheet()
    {
        CostHead = string.Empty;
    }

    /// <summary>Records hours.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="jobContractId">The contract.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">The day.</param>
    /// <param name="hours">The hours.</param>
    /// <param name="hourlyCost">The cost per hour.</param>
    /// <param name="costHead">The cost head charged.</param>
    public JobTimesheet(long companyId, long jobContractId, long employeeId, DateOnly date, decimal hours, decimal hourlyCost, string costHead)
    {
        CompanyId = companyId;
        JobContractId = jobContractId;
        EmployeeId = employeeId;
        Date = date;
        Hours = hours;
        HourlyCost = hourlyCost;
        CostHead = costHead;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the contract.</summary>
    public long JobContractId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the day.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the hours.</summary>
    public decimal Hours { get; private set; }

    /// <summary>Gets the cost per hour.</summary>
    public decimal HourlyCost { get; private set; }

    /// <summary>Gets the cost head.</summary>
    public string CostHead { get; private set; }

    /// <summary>Gets the cost (hours × rate).</summary>
    public decimal Cost => Math.Round(Hours * HourlyCost, 2, MidpointRounding.AwayFromZero);
}
