using System.Reflection;
using One4All.Application.Onboarding;
using One4All.Core.Accounting;

namespace One4All.Application.MasterData;

/// <summary>
/// The seed charts of accounts per industry (brief §3.1: "docs/coa/retail.csv, …"), embedded from <c>docs/coa</c>: code, name, type, control type,
/// report category and whether the account is a budget vote. Pure.
/// </summary>
public static class ChartOfAccounts
{
    /// <summary>The industry's seed chart (empty when there is none).</summary>
    /// <param name="vertical">The industry.</param>
    /// <returns>The accounts.</returns>
    public static IReadOnlyList<AccountSeed> Load(Vertical vertical)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"coa.{vertical.ToString().ToLowerInvariant()}.csv");
        if (stream is null)
        {
            return [];
        }

        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Reads a chart: <c>code, name, type</c> and optionally <c>control, category, budget</c> (yes / true).</summary>
    /// <param name="csv">The chart.</param>
    /// <returns>The accounts.</returns>
    /// <exception cref="FormatException">Thrown for a row whose type or control type is not known.</exception>
    public static IReadOnlyList<AccountSeed> Parse(string csv)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "code", "name", "type");
        if (missing.Count > 0)
        {
            throw new FormatException($"A chart needs the columns {string.Join(", ", missing)}.");
        }

        return [.. rows.Select(row =>
        {
            string code = (row.Text("code") ?? string.Empty).Trim().ToUpperInvariant();
            AccountType type = Enum.TryParse(row.Text("type"), true, out AccountType t) ? t : throw new FormatException($"Account {code}: unknown type '{row.Text("type")}'.");
            ControlAccountType? control = row.Text("control") is string c && c.Length > 0
                ? Enum.TryParse(c, true, out ControlAccountType parsed) ? parsed : throw new FormatException($"Account {code}: unknown control type '{c}'.")
                : null;
            string? budget = row.Text("budget");
            return new AccountSeed(code, (row.Text("name") ?? string.Empty).Trim(), type, control, row.Text("category"),
                string.Equals(budget, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(budget, "true", StringComparison.OrdinalIgnoreCase));
        })];
    }

    /// <summary>The template's accounts given the chart's category and budget flag, followed by the chart's other accounts.</summary>
    /// <param name="accounts">The template's own accounts.</param>
    /// <param name="chart">The seed chart.</param>
    /// <returns>The merged accounts.</returns>
    public static IReadOnlyList<AccountSeed> Merge(IReadOnlyList<AccountSeed> accounts, IReadOnlyList<AccountSeed> chart)
    {
        Dictionary<string, AccountSeed> byCode = chart.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        List<AccountSeed> merged = [.. accounts.Select(a => byCode.TryGetValue(a.Code, out AccountSeed? row)
            ? a with { ReportCategory = a.ReportCategory ?? row.ReportCategory, BudgetEnabled = a.BudgetEnabled || row.BudgetEnabled }
            : a)];
        HashSet<string> codes = [.. accounts.Select(a => a.Code)];
        merged.AddRange(chart.Where(a => !codes.Contains(a.Code)));
        return merged;
    }
}
