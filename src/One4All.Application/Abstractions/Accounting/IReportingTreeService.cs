using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>A tree's mapping problems.</summary>
/// <param name="Unmapped">Active accounts no node holds (they would be missing from the statements).</param>
/// <param name="MappedTwice">Accounts more than one node holds (they would be counted twice), with the nodes.</param>
/// <param name="Orphans">Nodes whose parent does not exist.</param>
public sealed record ReportingTreeCheck(IReadOnlyList<string> Unmapped, IReadOnlyList<(string Account, IReadOnlyList<string> Nodes)> MappedTwice, IReadOnlyList<string> Orphans)
{
    /// <summary>Gets a value indicating whether the tree maps every account exactly once.</summary>
    public bool IsComplete => Unmapped.Count == 0 && MappedTwice.Count == 0 && Orphans.Count == 0;
}

/// <summary>One line of a tree rollup.</summary>
/// <param name="Level">Depth (0 = top heading).</param>
/// <param name="Code">The node.</param>
/// <param name="Name">The heading.</param>
/// <param name="Amount">The total of the node and everything under it, signed for presentation.</param>
/// <param name="IsLeaf">Whether it has no child headings.</param>
public sealed record ReportingTreeLine(int Level, string Code, string Name, decimal Amount, bool IsLeaf);

/// <summary>
/// Reporting hierarchies over the chart of accounts (brief §3.1): build trees of headings with account ranges, start one from the accounts'
/// types and report categories, check every account is mapped exactly once, and roll posted amounts up through a tree for any dates.
/// </summary>
public interface IReportingTreeService
{
    /// <summary>Creates a tree.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">Its code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="purpose">What it is for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tree id, or why not.</returns>
    Task<Result<long>> CreateTreeAsync(long companyId, string code, string name, ReportingTreePurpose purpose, CancellationToken ct = default);

    /// <summary>Adds or changes a heading.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="treeCode">The tree.</param>
    /// <param name="nodeCode">The heading's code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="parentCode">Its parent (null: top).</param>
    /// <param name="sortOrder">Its order among siblings.</param>
    /// <param name="reverseSign">Show credit-positive.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not (an unknown parent, or a parent under the node itself).</returns>
    Task<Result> SaveNodeAsync(long companyId, string treeCode, string nodeCode, string name, string? parentCode, int sortOrder, bool reverseSign, CancellationToken ct = default);

    /// <summary>Maps an inclusive account range to a heading.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="treeCode">The tree.</param>
    /// <param name="nodeCode">The heading.</param>
    /// <param name="fromAccount">The first account.</param>
    /// <param name="toAccount">The last account (null: just the first).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> MapAsync(long companyId, string treeCode, string nodeCode, string fromAccount, string? toAccount, CancellationToken ct = default);

    /// <summary>Starts a tree from the chart: a heading per account type, one per report category beneath it, each account mapped to its category.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="treeCode">An empty tree.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The headings created, or why not.</returns>
    Task<Result<int>> SeedFromChartAsync(long companyId, string treeCode, CancellationToken ct = default);

    /// <summary>Checks the tree maps every active account exactly once and every heading has its parent.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="treeCode">The tree.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The check, or why not.</returns>
    Task<Result<ReportingTreeCheck>> CheckAsync(long companyId, string treeCode, CancellationToken ct = default);

    /// <summary>Rolls posted amounts (debit-positive, shown credit-positive where the heading says) up through the tree for a date range.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="treeCode">The tree.</param>
    /// <param name="from">The first day (null: from the beginning - a balance).</param>
    /// <param name="to">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lines in tree order, or why not.</returns>
    Task<Result<IReadOnlyList<ReportingTreeLine>>> RollupAsync(long companyId, string treeCode, DateOnly? from, DateOnly to, CancellationToken ct = default);
}
