using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>What a reporting tree is for.</summary>
public enum ReportingTreePurpose
{
    /// <summary>The statutory financial statements.</summary>
    Statutory = 1,

    /// <summary>Management reporting.</summary>
    Management = 2,

    /// <summary>Group consolidation.</summary>
    Consolidation = 3,
}

/// <summary>
/// A reporting hierarchy over the chart of accounts, independent of the account codes (brief §3.1): statements are laid out by the tree,
/// and a company keeps several (statutory, management, consolidation) side by side (<c>gl.reporting_tree</c>).
/// </summary>
public sealed class ReportingTree : AuditableEntity, ICompanyScoped
{
    private readonly List<ReportingNode> _nodes = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReportingTree()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a tree.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="purpose">What it is for.</param>
    public ReportingTree(long companyId, string code, string name, ReportingTreePurpose purpose)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        Purpose = purpose;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the purpose.</summary>
    public ReportingTreePurpose Purpose { get; set; }

    /// <summary>Gets the nodes.</summary>
    public IReadOnlyList<ReportingNode> Nodes => _nodes.AsReadOnly();

    /// <summary>Adds a node.</summary>
    /// <param name="node">The node.</param>
    public void AddNode(ReportingNode node) => _nodes.Add(node);

    /// <summary>Removes a node.</summary>
    /// <param name="node">The node.</param>
    public void RemoveNode(ReportingNode node) => _nodes.Remove(node);
}

/// <summary>A heading in a reporting tree, under a parent (or at the top), holding account ranges and / or child headings (<c>gl.reporting_node</c>).</summary>
public sealed class ReportingNode : Entity
{
    private readonly List<ReportingAccountRange> _ranges = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReportingNode()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a node.</summary>
    /// <param name="code">The code (unique in the tree).</param>
    /// <param name="name">The heading.</param>
    /// <param name="parentCode">The parent's code (null: a top heading).</param>
    /// <param name="sortOrder">Its place among its siblings.</param>
    /// <param name="reverseSign">Whether amounts are shown credit-positive (income, liabilities, equity).</param>
    public ReportingNode(string code, string name, string? parentCode, int sortOrder, bool reverseSign)
    {
        Code = code;
        Name = name;
        ParentCode = parentCode;
        SortOrder = sortOrder;
        ReverseSign = reverseSign;
    }

    /// <summary>Gets the tree.</summary>
    public long ReportingTreeId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the heading.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the parent's code.</summary>
    public string? ParentCode { get; set; }

    /// <summary>Gets or sets the order among siblings.</summary>
    public int SortOrder { get; set; }

    /// <summary>Gets or sets a value indicating whether amounts are shown credit-positive.</summary>
    public bool ReverseSign { get; set; }

    /// <summary>Gets the account ranges mapped to the node.</summary>
    public IReadOnlyList<ReportingAccountRange> Ranges => _ranges.AsReadOnly();

    /// <summary>Maps an account range (inclusive, by code order) to the node.</summary>
    /// <param name="from">The first account.</param>
    /// <param name="to">The last account.</param>
    public void Map(string from, string to) => _ranges.Add(new ReportingAccountRange(from, to));

    /// <summary>Removes the node's ranges.</summary>
    public void ClearRanges() => _ranges.Clear();

    /// <summary>Whether an account falls in one of the node's ranges.</summary>
    /// <param name="accountCode">The account.</param>
    /// <returns>True when mapped here.</returns>
    public bool Holds(string accountCode) => _ranges.Any(r => r.Contains(accountCode));
}

/// <summary>An inclusive range of account codes mapped to a reporting node (<c>gl.reporting_account_range</c>).</summary>
public sealed class ReportingAccountRange : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReportingAccountRange()
    {
        FromCode = string.Empty;
        ToCode = string.Empty;
    }

    /// <summary>Creates a range.</summary>
    /// <param name="fromCode">The first account.</param>
    /// <param name="toCode">The last account.</param>
    public ReportingAccountRange(string fromCode, string toCode)
    {
        FromCode = fromCode;
        ToCode = toCode;
    }

    /// <summary>Gets the node.</summary>
    public long ReportingNodeId { get; private set; }

    /// <summary>Gets the first account.</summary>
    public string FromCode { get; private set; }

    /// <summary>Gets the last account.</summary>
    public string ToCode { get; private set; }

    /// <summary>Whether an account code falls in the range (ordinal comparison, as codes sort).</summary>
    /// <param name="accountCode">The account.</param>
    /// <returns>True when inside.</returns>
    public bool Contains(string accountCode)
        => string.CompareOrdinal(accountCode, FromCode) >= 0 && string.CompareOrdinal(accountCode, ToCode) <= 0;
}
