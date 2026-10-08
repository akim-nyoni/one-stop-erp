using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>A bill of materials (<c>inv.bom</c>): the components that make a batch of a finished item.</summary>
public sealed class BillOfMaterials : AuditableEntity, ICompanyScoped
{
    private readonly List<BomLine> _lines = [];

    private BillOfMaterials()
    {
        Code = string.Empty;
    }

    /// <summary>Creates a bill of materials.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The BOM code.</param>
    /// <param name="parentItemId">The finished item.</param>
    /// <param name="outputQuantity">How many finished units one batch makes.</param>
    public BillOfMaterials(long companyId, string code, long parentItemId, decimal outputQuantity)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        ParentItemId = parentItemId;
        OutputQuantity = outputQuantity;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the BOM code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the finished item.</summary>
    public long ParentItemId { get; private set; }

    /// <summary>Gets how many finished units one batch makes.</summary>
    public decimal OutputQuantity { get; private set; }

    /// <summary>Gets or sets a value indicating whether the BOM can be used.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets the components.</summary>
    public IReadOnlyList<BomLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a component.</summary>
    /// <param name="componentItemId">The component.</param>
    /// <param name="quantity">The quantity per batch.</param>
    /// <param name="scrapPercent">The expected scrap, as a percentage on top of the quantity.</param>
    public void AddLine(long componentItemId, decimal quantity, decimal scrapPercent = 0m) => _lines.Add(new BomLine(componentItemId, quantity, scrapPercent));
}

/// <summary>One component of a bill of materials.</summary>
public sealed class BomLine : Entity
{
    private BomLine()
    {
    }

    /// <summary>Creates a component line.</summary>
    /// <param name="componentItemId">The component.</param>
    /// <param name="quantity">The quantity per batch.</param>
    /// <param name="scrapPercent">The expected scrap percentage.</param>
    public BomLine(long componentItemId, decimal quantity, decimal scrapPercent)
    {
        ComponentItemId = componentItemId;
        Quantity = quantity;
        ScrapPercent = scrapPercent;
    }

    /// <summary>Gets the owning BOM.</summary>
    public long BillOfMaterialsId { get; private set; }

    /// <summary>Gets the component.</summary>
    public long ComponentItemId { get; private set; }

    /// <summary>Gets the quantity per batch.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the expected scrap percentage.</summary>
    public decimal ScrapPercent { get; private set; }
}

/// <summary>An assembly (production) run against a BOM (<c>inv.assembly_run</c>): what was planned, consumed and produced.</summary>
public sealed class AssemblyRun : AuditableEntity, ICompanyScoped
{
    private readonly List<AssemblyRunLine> _lines = [];

    private AssemblyRun()
    {
        RunNumber = string.Empty;
    }

    /// <summary>Records a run.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runNumber">The run number.</param>
    /// <param name="billOfMaterialsId">The BOM.</param>
    /// <param name="parentItemId">The finished item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="date">The run date.</param>
    /// <param name="plannedOutput">The output the components were planned for.</param>
    /// <param name="actualOutput">The output actually produced.</param>
    /// <param name="totalCost">The cost of the components consumed (the finished goods' value).</param>
    public AssemblyRun(long companyId, string runNumber, long billOfMaterialsId, long parentItemId, long warehouseId, DateOnly date, decimal plannedOutput, decimal actualOutput, decimal totalCost)
    {
        CompanyId = companyId;
        RunNumber = runNumber;
        BillOfMaterialsId = billOfMaterialsId;
        ParentItemId = parentItemId;
        WarehouseId = warehouseId;
        Date = date;
        PlannedOutput = plannedOutput;
        ActualOutput = actualOutput;
        TotalCost = totalCost;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the run number.</summary>
    public string RunNumber { get; private set; }

    /// <summary>Gets the BOM.</summary>
    public long BillOfMaterialsId { get; private set; }

    /// <summary>Gets the finished item.</summary>
    public long ParentItemId { get; private set; }

    /// <summary>Gets the warehouse.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>Gets the run date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the planned output.</summary>
    public decimal PlannedOutput { get; private set; }

    /// <summary>Gets the actual output.</summary>
    public decimal ActualOutput { get; private set; }

    /// <summary>Gets the cost of components consumed.</summary>
    public decimal TotalCost { get; private set; }

    /// <summary>The usage variance posted (components used beyond standard, at their issue cost; negative when fewer were used).</summary>
    public decimal UsageVariance { get; set; }

    /// <summary>Gets or sets a value indicating whether the run took finished goods apart (lines are components recovered).</summary>
    public bool IsDisassembly { get; set; }

    /// <summary>Gets the components consumed.</summary>
    public IReadOnlyList<AssemblyRunLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a consumed component.</summary>
    /// <param name="line">The line.</param>
    public void AddLine(AssemblyRunLine line) => _lines.Add(line);
}

/// <summary>One component consumed by an assembly run.</summary>
public sealed class AssemblyRunLine : Entity
{
    private AssemblyRunLine()
    {
    }

    /// <summary>Records a consumed component.</summary>
    /// <param name="componentItemId">The component.</param>
    /// <param name="standardQuantity">What the BOM allows for the actual output (scrap included).</param>
    /// <param name="actualQuantity">What was actually consumed.</param>
    /// <param name="cost">The cost of what was consumed.</param>
    public AssemblyRunLine(long componentItemId, decimal standardQuantity, decimal actualQuantity, decimal cost)
    {
        ComponentItemId = componentItemId;
        StandardQuantity = standardQuantity;
        ActualQuantity = actualQuantity;
        Cost = cost;
    }

    /// <summary>Gets the owning run.</summary>
    public long AssemblyRunId { get; private set; }

    /// <summary>Gets the component.</summary>
    public long ComponentItemId { get; private set; }

    /// <summary>Gets the standard quantity for the actual output.</summary>
    public decimal StandardQuantity { get; private set; }

    /// <summary>Gets the quantity consumed.</summary>
    public decimal ActualQuantity { get; private set; }

    /// <summary>Gets the cost consumed.</summary>
    public decimal Cost { get; private set; }
}
