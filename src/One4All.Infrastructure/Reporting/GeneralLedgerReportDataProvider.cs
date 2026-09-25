using One4All.Application.Abstractions.Reporting;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Supplies live rows for the general-ledger reports (trial balance, income statement, balance sheet,
/// nominal activity) from <see cref="IGeneralLedgerReports"/>.
/// </summary>
public sealed class GeneralLedgerReportDataProvider : IReportDataProvider
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "TrialBalance", "IncomeStatement", "BalanceSheet", "NominalActivity", "JournalDaybook", "TrialBalanceMovement",
    };

    private readonly IGeneralLedgerReports _reports;

    /// <summary>Creates the provider.</summary>
    /// <param name="reports">The GL report services.</param>
    public GeneralLedgerReportDataProvider(IGeneralLedgerReports reports) => _reports = reports;

    /// <inheritdoc />
    public bool CanHandle(string command) => Commands.Contains(command);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        return command.ToUpperInvariant() switch
        {
            "TRIALBALANCE" => await TrialBalanceAsync(companyId, parameters, ct).ConfigureAwait(false),
            "INCOMESTATEMENT" => await IncomeStatementAsync(companyId, parameters, ct).ConfigureAwait(false),
            "BALANCESHEET" => await BalanceSheetAsync(companyId, parameters, ct).ConfigureAwait(false),
            "NOMINALACTIVITY" => await NominalActivityAsync(companyId, parameters, ct).ConfigureAwait(false),
            "JOURNALDAYBOOK" => await JournalDaybookAsync(companyId, parameters, ct).ConfigureAwait(false),
            "TRIALBALANCEMOVEMENT" => await TrialBalanceMovementAsync(companyId, parameters, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TrialBalanceAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        TrialBalanceReport tb = await _reports.TrialBalanceAsync(companyId, ReportParameters.Date(parameters, "AsAtDate"), ct).ConfigureAwait(false);
        return tb.Rows.Select(r => Row(new()
        {
            ["AccountCode"] = r.AccountCode,
            ["AccountName"] = r.AccountName,
            ["Debit"] = r.Debit,
            ["Credit"] = r.Credit,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> IncomeStatementAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        IncomeStatement report = await _reports.IncomeStatementAsync(companyId, from, to, ct).ConfigureAwait(false);

        List<IReadOnlyDictionary<string, object?>> rows = [];
        rows.AddRange(report.Income.Select(l => Row(new() { ["Category"] = "Income", ["AccountCode"] = l.AccountCode, ["AccountName"] = l.AccountName, ["Amount"] = l.Amount })));
        rows.AddRange(report.Expenses.Select(l => Row(new() { ["Category"] = "Expense", ["AccountCode"] = l.AccountCode, ["AccountName"] = l.AccountName, ["Amount"] = -l.Amount })));
        rows.Add(Row(new() { ["Category"] = "Result", ["AccountCode"] = string.Empty, ["AccountName"] = "Net profit", ["Amount"] = report.NetProfit }));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BalanceSheetAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        BalanceSheet report = await _reports.BalanceSheetAsync(companyId, ReportParameters.Date(parameters, "AsAtDate"), ct).ConfigureAwait(false);

        List<IReadOnlyDictionary<string, object?>> rows = [];
        rows.AddRange(report.Assets.Select(l => Row(new() { ["Category"] = "Asset", ["AccountCode"] = l.AccountCode, ["AccountName"] = l.AccountName, ["Amount"] = l.Amount })));
        rows.AddRange(report.Liabilities.Select(l => Row(new() { ["Category"] = "Liability", ["AccountCode"] = l.AccountCode, ["AccountName"] = l.AccountName, ["Amount"] = l.Amount })));
        rows.AddRange(report.Equity.Select(l => Row(new() { ["Category"] = "Equity", ["AccountCode"] = l.AccountCode, ["AccountName"] = l.AccountName, ["Amount"] = l.Amount })));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NominalActivityAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string account = ReportParameters.String(parameters, "AccountCode");
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        if (string.IsNullOrWhiteSpace(account))
        {
            return [];
        }

        NominalActivityReport report = await _reports.NominalActivityAsync(companyId, account, from, to, ct).ConfigureAwait(false);
        return report.Lines.Select(l => Row(new()
        {
            ["Date"] = l.Date.ToDateTime(TimeOnly.MinValue),
            ["JournalNumber"] = l.JournalNumber,
            ["Narrative"] = l.Narrative,
            ["Debit"] = l.Debit,
            ["Credit"] = l.Credit,
            ["RunningBalance"] = l.RunningBalance,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> JournalDaybookAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        JournalDaybookReport report = await _reports.JournalDaybookAsync(companyId, from, to, ct).ConfigureAwait(false);
        return report.Lines.Select(l => Row(new()
        {
            ["Date"] = l.Date.ToDateTime(TimeOnly.MinValue),
            ["JournalNumber"] = l.JournalNumber,
            ["SourceModule"] = l.SourceModule,
            ["DocumentType"] = l.DocumentType ?? string.Empty,
            ["DocumentNumber"] = l.DocumentNumber ?? string.Empty,
            ["Narrative"] = l.Narrative ?? string.Empty,
            ["Amount"] = l.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TrialBalanceMovementAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        TrialBalanceMovementReport report = await _reports.TrialBalanceMovementAsync(companyId, from, to, ct).ConfigureAwait(false);
        return report.Rows.Select(r => Row(new()
        {
            ["AccountCode"] = r.AccountCode,
            ["AccountName"] = r.AccountName,
            ["Opening"] = r.Opening,
            ["Debit"] = r.Debit,
            ["Credit"] = r.Credit,
            ["Closing"] = r.Closing,
        })).ToList();
    }

    private static IReadOnlyDictionary<string, object?> Row(Dictionary<string, object?> values) => values;
}
