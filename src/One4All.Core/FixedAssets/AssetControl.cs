using One4All.Core.Common;

namespace One4All.Core.FixedAssets;

/// <summary>What a physical verification found for one asset.</summary>
public enum AssetCountResult
{
    /// <summary>Expected but not (yet) seen; missing once the count closes.</summary>
    NotSeen = 0,

    /// <summary>Seen where the register says it is.</summary>
    Found = 1,

    /// <summary>Seen somewhere other than the register's location.</summary>
    FoundElsewhere = 2,

    /// <summary>Not seen by the time the count closed.</summary>
    Missing = 3,

    /// <summary>A tagged item seen that is not on the register.</summary>
    Unregistered = 4,
}

/// <summary>A physical asset verification (<c>fa.asset_count</c>): the register's expectation for a location, and what was seen.</summary>
public sealed class AssetCount : AuditableEntity, ICompanyScoped
{
    private readonly List<AssetCountLine> _lines = [];

    private AssetCount()
    {
        CountNumber = string.Empty;
    }

    /// <summary>Starts a count.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="countNumber">The count number.</param>
    /// <param name="date">The count date.</param>
    /// <param name="location">The location counted, or null for everywhere.</param>
    public AssetCount(long companyId, string countNumber, DateOnly date, string? location)
    {
        CompanyId = companyId;
        CountNumber = countNumber;
        Date = date;
        Location = location;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the count number.</summary>
    public string CountNumber { get; private set; }

    /// <summary>Gets the count date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the location counted (null = everywhere).</summary>
    public string? Location { get; private set; }

    /// <summary>Gets a value indicating whether the count is closed.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<AssetCountLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a line.</summary>
    /// <param name="line">The line.</param>
    public void AddLine(AssetCountLine line) => _lines.Add(line);

    /// <summary>Closes the count: whatever was expected and not seen is missing.</summary>
    public void Close()
    {
        foreach (AssetCountLine line in _lines.Where(l => l.Result == AssetCountResult.NotSeen))
        {
            line.MarkMissing();
        }

        IsClosed = true;
    }
}

/// <summary>One asset on a verification.</summary>
public sealed class AssetCountLine : Entity
{
    private AssetCountLine()
    {
        Tag = string.Empty;
    }

    /// <summary>Creates a line.</summary>
    /// <param name="assetId">The asset, when on the register.</param>
    /// <param name="tag">The asset number / tag.</param>
    /// <param name="expectedLocation">Where the register says it is.</param>
    /// <param name="result">The starting result.</param>
    public AssetCountLine(long? assetId, string tag, string? expectedLocation, AssetCountResult result)
    {
        AssetId = assetId;
        Tag = tag;
        ExpectedLocation = expectedLocation;
        Result = result;
    }

    /// <summary>Gets the owning count.</summary>
    public long AssetCountId { get; private set; }

    /// <summary>Gets the asset, when registered.</summary>
    public long? AssetId { get; private set; }

    /// <summary>Gets the asset number / tag.</summary>
    public string Tag { get; private set; }

    /// <summary>Gets where the register says it is.</summary>
    public string? ExpectedLocation { get; private set; }

    /// <summary>Gets where it was seen.</summary>
    public string? FoundLocation { get; private set; }

    /// <summary>Gets the condition noted.</summary>
    public string? Condition { get; private set; }

    /// <summary>Gets the result.</summary>
    public AssetCountResult Result { get; private set; }

    /// <summary>Records a sighting.</summary>
    /// <param name="location">Where it was seen.</param>
    /// <param name="condition">Its condition.</param>
    public void Sight(string? location, string? condition)
    {
        FoundLocation = location;
        Condition = condition;
        if (Result != AssetCountResult.Unregistered)
        {
            Result = string.Equals(location?.Trim(), ExpectedLocation?.Trim(), StringComparison.OrdinalIgnoreCase) ? AssetCountResult.Found : AssetCountResult.FoundElsewhere;
        }
    }

    /// <summary>Marks the line missing.</summary>
    public void MarkMissing() => Result = AssetCountResult.Missing;
}

/// <summary>A recurring maintenance task for an asset (<c>fa.maintenance_plan</c>).</summary>
public sealed class MaintenancePlan : Entity, ICompanyScoped
{
    private MaintenancePlan()
    {
        Description = string.Empty;
    }

    /// <summary>Creates a plan.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="description">The task.</param>
    /// <param name="intervalMonths">How often, in months.</param>
    /// <param name="nextDue">When it is next due.</param>
    public MaintenancePlan(long companyId, long assetId, string description, int intervalMonths, DateOnly nextDue)
    {
        CompanyId = companyId;
        AssetId = assetId;
        Description = description;
        IntervalMonths = intervalMonths;
        NextDue = nextDue;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the asset.</summary>
    public long AssetId { get; private set; }

    /// <summary>Gets the task.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the interval in months.</summary>
    public int IntervalMonths { get; private set; }

    /// <summary>Gets when it is next due.</summary>
    public DateOnly NextDue { get; private set; }

    /// <summary>Gets when it was last done.</summary>
    public DateOnly? LastDone { get; private set; }

    /// <summary>Gets or sets a value indicating whether the plan is live.</summary>
    public bool IsActive { get; set; }

    /// <summary>Records the task done and rolls the next due date forward from when it was done.</summary>
    /// <param name="date">When it was done.</param>
    public void Done(DateOnly date)
    {
        LastDone = date;
        NextDue = date.AddMonths(IntervalMonths);
    }
}
