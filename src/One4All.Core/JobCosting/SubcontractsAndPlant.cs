using One4All.Core.Common;

namespace One4All.Core.JobCosting;

/// <summary>A subcontract let on a contract (<c>job.subcontract</c>), certified and paid through the supplier ledger with retention held back.</summary>
public sealed class Subcontract : AuditableEntity, ICompanyScoped
{
    private readonly List<SubcontractCertificate> _certificates = [];

    private Subcontract()
    {
        Reference = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Lets a subcontract.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="jobContractId">The main contract.</param>
    /// <param name="supplierId">The subcontractor.</param>
    /// <param name="reference">The subcontract reference.</param>
    /// <param name="description">The package.</param>
    /// <param name="value">The subcontract value.</param>
    /// <param name="retentionPercent">The retention held on each certificate.</param>
    public Subcontract(long companyId, long jobContractId, long supplierId, string reference, string description, decimal value, decimal retentionPercent)
    {
        CompanyId = companyId;
        JobContractId = jobContractId;
        SupplierId = supplierId;
        Reference = reference.Trim().ToUpperInvariant();
        Description = description;
        Value = value;
        RetentionPercent = retentionPercent;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the main contract.</summary>
    public long JobContractId { get; private set; }

    /// <summary>Gets the subcontractor.</summary>
    public long SupplierId { get; private set; }

    /// <summary>Gets the reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets the package.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the value.</summary>
    public decimal Value { get; private set; }

    /// <summary>Gets the retention percentage.</summary>
    public decimal RetentionPercent { get; private set; }

    /// <summary>Gets the retention released so far.</summary>
    public decimal RetentionReleased { get; private set; }

    /// <summary>Gets the certificates.</summary>
    public IReadOnlyList<SubcontractCertificate> Certificates => _certificates.AsReadOnly();

    /// <summary>Gets the work certified to date (gross).</summary>
    public decimal CertifiedToDate => _certificates.Sum(c => c.Gross);

    /// <summary>Gets the retention held (deducted less released).</summary>
    public decimal RetentionHeld => _certificates.Sum(c => c.Retention) - RetentionReleased;

    /// <summary>Adds a certificate.</summary>
    /// <param name="certificate">The certificate.</param>
    public void AddCertificate(SubcontractCertificate certificate) => _certificates.Add(certificate);

    /// <summary>Releases retention.</summary>
    /// <param name="amount">The amount released.</param>
    public void Release(decimal amount) => RetentionReleased += amount;
}

/// <summary>One certificate of work done by a subcontractor.</summary>
public sealed class SubcontractCertificate : Entity
{
    private SubcontractCertificate()
    {
        Number = string.Empty;
        InvoiceNumber = string.Empty;
    }

    /// <summary>Records a certificate.</summary>
    /// <param name="number">The certificate number.</param>
    /// <param name="date">The date.</param>
    /// <param name="workDoneToDate">The cumulative value of work done.</param>
    /// <param name="gross">The gross value of this certificate.</param>
    /// <param name="retention">The retention held on it.</param>
    /// <param name="invoiceNumber">The supplier-ledger invoice it raised.</param>
    public SubcontractCertificate(string number, DateOnly date, decimal workDoneToDate, decimal gross, decimal retention, string invoiceNumber)
    {
        Number = number;
        Date = date;
        WorkDoneToDate = workDoneToDate;
        Gross = gross;
        Retention = retention;
        InvoiceNumber = invoiceNumber;
    }

    /// <summary>Gets the subcontract.</summary>
    public long SubcontractId { get; private set; }

    /// <summary>Gets the certificate number.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the cumulative work done.</summary>
    public decimal WorkDoneToDate { get; private set; }

    /// <summary>Gets the gross this certificate.</summary>
    public decimal Gross { get; private set; }

    /// <summary>Gets the retention held on it.</summary>
    public decimal Retention { get; private set; }

    /// <summary>Gets the supplier-ledger invoice number.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>Gets the amount payable (gross less retention).</summary>
    public decimal Payable => Gross - Retention;
}

/// <summary>An item of plant or equipment hired to jobs (<c>job.plant_item</c>).</summary>
public sealed class PlantItem : Entity, ICompanyScoped
{
    private PlantItem()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Registers plant.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The plant code.</param>
    /// <param name="name">The name.</param>
    /// <param name="hireRatePerHour">The internal hire rate charged to jobs per hour.</param>
    public PlantItem(long companyId, string code, string name, decimal hireRatePerHour)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        HireRatePerHour = hireRatePerHour;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets or sets the hire rate per hour.</summary>
    public decimal HireRatePerHour { get; set; }

    /// <summary>Gets or sets the fixed asset it is, when capitalised.</summary>
    public long? FixedAssetId { get; set; }

    /// <summary>Gets or sets a value indicating whether it is in service.</summary>
    public bool IsActive { get; set; }
}

/// <summary>What plant did on a day: hours worked on a job (charged at the hire rate) or a running cost (fuel, repairs).</summary>
public sealed class PlantLog : Entity, ICompanyScoped
{
    private PlantLog()
    {
        Description = string.Empty;
    }

    /// <summary>Records plant activity.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="plantItemId">The plant.</param>
    /// <param name="date">The day.</param>
    /// <param name="jobContractId">The job worked on (null for a running cost).</param>
    /// <param name="hours">The hours worked.</param>
    /// <param name="hireCharge">The hire charged to the job.</param>
    /// <param name="runningCost">The running cost incurred.</param>
    /// <param name="description">What it was.</param>
    public PlantLog(long companyId, long plantItemId, DateOnly date, long? jobContractId, decimal hours, decimal hireCharge, decimal runningCost, string description)
    {
        CompanyId = companyId;
        PlantItemId = plantItemId;
        Date = date;
        JobContractId = jobContractId;
        Hours = hours;
        HireCharge = hireCharge;
        RunningCost = runningCost;
        Description = description;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the plant.</summary>
    public long PlantItemId { get; private set; }

    /// <summary>Gets the day.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the job worked on.</summary>
    public long? JobContractId { get; private set; }

    /// <summary>Gets the hours worked.</summary>
    public decimal Hours { get; private set; }

    /// <summary>Gets the hire charged to the job.</summary>
    public decimal HireCharge { get; private set; }

    /// <summary>Gets the running cost.</summary>
    public decimal RunningCost { get; private set; }

    /// <summary>Gets what it was.</summary>
    public string Description { get; private set; }
}
