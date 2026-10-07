using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Reporting hierarchies over the chart of accounts (brief §3.1).</summary>
public sealed class ReportingTreeService : IReportingTreeService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ReportingTreeService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> CreateTreeAsync(long companyId, string code, string name, ReportingTreePurpose purpose, CancellationToken ct = default)
    {
        string key = Code(code);
        if (key.Length == 0 || key.Length > 20 || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("Tree.Invalid", "A reporting tree needs a code (up to 20 characters) and a name.");
        }

        if (await _db.ReportingTrees.AnyAsync(t => t.CompanyId == companyId && t.Code == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Tree.Duplicate", $"Reporting tree {key} already exists.");
        }

        ReportingTree tree = new(companyId, key, name.Trim(), purpose);
        _db.ReportingTrees.Add(tree);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return tree.Id;
    }

    /// <inheritdoc />
    public async Task<Result> SaveNodeAsync(long companyId, string treeCode, string nodeCode, string name, string? parentCode, int sortOrder, bool reverseSign, CancellationToken ct = default)
    {
        if (await LoadAsync(companyId, treeCode, ct).ConfigureAwait(false) is not ReportingTree tree)
        {
            return Result.Failure(Missing(treeCode));
        }

        string key = Code(nodeCode);
        string? parent = string.IsNullOrWhiteSpace(parentCode) ? null : Code(parentCode);
        if (key.Length == 0 || key.Length > 40 || string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("Tree.NodeInvalid", "A heading needs a code (up to 40 characters) and a name."));
        }

        if (parent is not null && tree.Nodes.All(n => n.Code != parent))
        {
            return Result.Failure(Error.NotFound("Tree.ParentNotFound", $"Heading {parent} is not in tree {tree.Code}."));
        }

        // A heading cannot sit under itself or anything beneath it.
        for (string? up = parent; up is not null; up = tree.Nodes.FirstOrDefault(n => n.Code == up)?.ParentCode)
        {
            if (up == key)
            {
                return Result.Failure(Error.Rule("Tree.Cycle", $"{key} cannot sit under {parent}: that heading is beneath it."));
            }
        }

        ReportingNode? node = tree.Nodes.FirstOrDefault(n => n.Code == key);
        if (node is null)
        {
            tree.AddNode(new ReportingNode(key, name.Trim(), parent, sortOrder, reverseSign));
        }
        else
        {
            node.Name = name.Trim();
            node.ParentCode = parent;
            node.SortOrder = sortOrder;
            node.ReverseSign = reverseSign;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> MapAsync(long companyId, string treeCode, string nodeCode, string fromAccount, string? toAccount, CancellationToken ct = default)
    {
        if (await LoadAsync(companyId, treeCode, ct).ConfigureAwait(false) is not ReportingTree tree)
        {
            return Result.Failure(Missing(treeCode));
        }

        string from = Code(fromAccount), to = string.IsNullOrWhiteSpace(toAccount) ? from : Code(toAccount);
        if (tree.Nodes.FirstOrDefault(n => n.Code == Code(nodeCode)) is not ReportingNode node)
        {
            return Result.Failure(Error.NotFound("Tree.NodeNotFound", $"Heading {Code(nodeCode)} is not in tree {tree.Code}."));
        }

        if (from.Length == 0 || string.CompareOrdinal(from, to) > 0)
        {
            return Result.Failure(Error.Validation("Tree.RangeInvalid", "A range runs from an account to the same or a later one."));
        }

        node.Map(from, to);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> SeedFromChartAsync(long companyId, string treeCode, CancellationToken ct = default)
    {
        if (await LoadAsync(companyId, treeCode, ct).ConfigureAwait(false) is not ReportingTree tree)
        {
            return Missing(treeCode);
        }

        if (tree.Nodes.Count > 0)
        {
            return Error.Rule("Tree.NotEmpty", $"Tree {tree.Code} already has headings; seed an empty tree.");
        }

        List<LedgerAccount> accounts = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).OrderBy(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        int created = 0;
        foreach (IGrouping<AccountType, LedgerAccount> type in accounts.GroupBy(a => a.Type).OrderBy(g => (int)g.Key))
        {
            string typeCode = type.Key.ToString().ToUpperInvariant();
            bool creditPositive = type.Key is AccountType.Liability or AccountType.Equity or AccountType.Income;
            tree.AddNode(new ReportingNode(typeCode, Plural(type.Key), null, (int)type.Key * 10, creditPositive));
            created++;
            int order = 0;
            foreach (IGrouping<string, LedgerAccount> category in type.GroupBy(a => string.IsNullOrWhiteSpace(a.ReportCategory) ? "Other" : a.ReportCategory.Trim()).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                string slug = new([.. category.Key.ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')]);
                string nodeCode = $"{typeCode}-{slug}";
                ReportingNode node = new(nodeCode.Length > 40 ? nodeCode[..40] : nodeCode, category.Key, typeCode, ++order * 10, creditPositive);
                foreach (LedgerAccount account in category)
                {
                    node.Map(account.AccountCode, account.AccountCode);
                }

                tree.AddNode(node);
                created++;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc />
    public async Task<Result<ReportingTreeCheck>> CheckAsync(long companyId, string treeCode, CancellationToken ct = default)
    {
        if (await LoadAsync(companyId, treeCode, ct).ConfigureAwait(false) is not ReportingTree tree)
        {
            return Missing(treeCode);
        }

        List<string> active = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.IsActive).OrderBy(a => a.AccountCode).Select(a => a.AccountCode)
            .ToListAsync(ct).ConfigureAwait(false);
        List<string> unmapped = [];
        List<(string, IReadOnlyList<string>)> twice = [];
        foreach (string account in active)
        {
            List<string> holders = [.. tree.Nodes.Where(n => n.Holds(account)).Select(n => n.Code)];
            if (holders.Count == 0)
            {
                unmapped.Add(account);
            }
            else if (holders.Count > 1)
            {
                twice.Add((account, holders));
            }
        }

        List<string> orphans = [.. tree.Nodes.Where(n => n.ParentCode is not null && tree.Nodes.All(p => p.Code != n.ParentCode)).Select(n => n.Code)];
        return new ReportingTreeCheck(unmapped, twice, orphans);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ReportingTreeLine>>> RollupAsync(long companyId, string treeCode, DateOnly? from, DateOnly to, CancellationToken ct = default)
    {
        if (await LoadAsync(companyId, treeCode, ct).ConfigureAwait(false) is not ReportingTree tree)
        {
            return Missing(treeCode);
        }

        DateOnly start = from ?? DateOnly.MinValue;
        Dictionary<string, decimal> byAccount = await (
                from line in _db.Set<JournalLine>()
                join journal in _db.Journals on line.JournalEntryId equals journal.Id
                where journal.CompanyId == companyId && journal.PostingDate >= start && journal.PostingDate <= to
                    && (journal.Status == JournalStatus.Posted || journal.Status == JournalStatus.Reversed)
                group line by line.AccountCode into g
                select new { Account = g.Key, Net = g.Sum(l => l.BaseDebit) - g.Sum(l => l.BaseCredit) })
            .ToDictionaryAsync(x => x.Account, x => x.Net, ct).ConfigureAwait(false);

        Dictionary<string, List<ReportingNode>> children = tree.Nodes.Where(n => n.ParentCode is not null).GroupBy(n => n.ParentCode!)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ThenBy(n => n.Code, StringComparer.Ordinal).ToList());
        Dictionary<string, decimal> totals = [];
        decimal Total(ReportingNode node)
        {
            if (totals.TryGetValue(node.Code, out decimal known))
            {
                return known;
            }

            decimal own = byAccount.Where(a => node.Holds(a.Key)).Sum(a => a.Value);
            decimal sum = own + children.GetValueOrDefault(node.Code, []).Sum(Total);
            totals[node.Code] = sum;
            return sum;
        }

        List<ReportingTreeLine> lines = [];
        void Walk(ReportingNode node, int level)
        {
            List<ReportingNode> under = children.GetValueOrDefault(node.Code, []);
            decimal total = Total(node);
            lines.Add(new ReportingTreeLine(level, node.Code, node.Name, node.ReverseSign ? -total : total, under.Count == 0));
            under.ForEach(child => Walk(child, level + 1));
        }

        foreach (ReportingNode top in tree.Nodes.Where(n => n.ParentCode is null).OrderBy(n => n.SortOrder).ThenBy(n => n.Code, StringComparer.Ordinal))
        {
            Walk(top, 0);
        }

        return lines;
    }

    private static string Code(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string Plural(AccountType type) => type switch
    {
        AccountType.Asset => "Assets",
        AccountType.Liability => "Liabilities",
        AccountType.Equity => "Equity",
        AccountType.Income => "Income",
        _ => "Expenses",
    };

    private static Error Missing(string treeCode) => Error.NotFound("Tree.NotFound", $"Reporting tree {Code(treeCode)} not found.");

    private Task<ReportingTree?> LoadAsync(long companyId, string treeCode, CancellationToken ct)
    {
        string key = Code(treeCode);
        return _db.ReportingTrees.Include(t => t.Nodes).ThenInclude(n => n.Ranges).FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == key, ct);
    }
}

/// <summary>Rows for the reporting-tree statement report (command <c>TreeRollup</c>: TreeCode, FromDate, ToDate).</summary>
public sealed class ReportingTreeReportDataProvider : Application.Abstractions.Reporting.IReportDataProvider
{
    private readonly IReportingTreeService _trees;

    /// <summary>Creates the provider.</summary>
    /// <param name="trees">The tree service.</param>
    public ReportingTreeReportDataProvider(IReportingTreeService trees) => _trees = trees;

    /// <inheritdoc />
    public bool CanHandle(string command) => string.Equals(command, "TreeRollup", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        string tree = Convert.ToString(parameters.GetValueOrDefault("TreeCode"), CultureInfo.InvariantCulture) ?? string.Empty;
        DateOnly to = parameters.GetValueOrDefault("ToDate") is DateOnly t ? t : DateOnly.FromDateTime(DateTime.Today);
        DateOnly? from = parameters.GetValueOrDefault("FromDate") is DateOnly f ? f : null;
        Result<IReadOnlyList<ReportingTreeLine>> rolled = await _trees.RollupAsync(companyId, tree, from, to, ct).ConfigureAwait(false);
        return rolled.IsFailure ? [] : [.. rolled.Value.Select(l => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["Code"] = l.Code, ["Heading"] = new string(' ', l.Level * 4) + l.Name, ["Amount"] = l.Amount, ["Level"] = l.Level, ["IsLeaf"] = l.IsLeaf,
        })];
    }
}
