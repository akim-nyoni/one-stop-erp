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
        "TrialBalance", "TrialBalanceByType", "IncomeStatement", "BalanceSheet", "CashFlow", "DepartmentalIncomeStatement", "NominalActivity", "JournalDaybook", "TrialBalanceMovement", "ChartOfAccounts", "FinancialSnapshot", "VatReturn", "ControlReconciliation", "VatReturnByCode",
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
            "TRIALBALANCEBYTYPE" => await TrialBalanceByTypeAsync(companyId, parameters, ct).ConfigureAwait(false),
            "INCOMESTATEMENT" => await IncomeStatementAsync(companyId, parameters, ct).ConfigureAwait(false),
            "BALANCESHEET" => await BalanceSheetAsync(companyId, parameters, ct).ConfigureAwait(false),
            "CASHFLOW" => await CashFlowAsync(companyId, parameters, ct).ConfigureAwait(false),
            "DEPARTMENTALINCOMESTATEMENT" => await DepartmentalIncomeStatementAsync(companyId, parameters, ct).ConfigureAwait(false),
            "NOMINALACTIVITY" => await NominalActivityAsync(companyId, parameters, ct).ConfigureAwait(false),
            "JOURNALDAYBOOK" => await JournalDaybookAsync(companyId, parameters, ct).ConfigureAwait(false),
            "TRIALBALANCEMOVEMENT" => await TrialBalanceMovementAsync(companyId, parameters, ct).ConfigureAwait(false),
            "CHARTOFACCOUNTS" => await ChartOfAccountsAsync(companyId, ct).ConfigureAwait(false),
            "FINANCIALSNAPSHOT" => await FinancialSnapshotAsync(companyId, parameters, ct).ConfigureAwait(false),
            "VATRETURN" => await VatReturnAsync(companyId, parameters, ct).ConfigureAwait(false),
            "CONTROLRECONCILIATION" => await ControlReconciliationAsync(companyId, parameters, ct).ConfigureAwait(false),
            "VATRETURNBYCODE" => await VatReturnByCodeAsync(companyId, parameters, ct).ConfigureAwait(false),
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

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TrialBalanceByTypeAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        TrialBalanceByTypeReport report = await _reports.TrialBalanceByTypeAsync(companyId, ReportParameters.Date(parameters, "AsAtDate"), ct).ConfigureAwait(false);
        return report.Rows.Select(r => Row(new()
        {
            ["Type"] = r.Type.ToString(),
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

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DepartmentalIncomeStatementAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string department = ReportParameters.String(parameters, "Department");
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        IncomeStatement report = await _reports.IncomeStatementByDepartmentAsync(companyId, from, to, department, ct).ConfigureAwait(false);

        List<IReadOnlyDictionary<string, object?>> rows = [];
        rows.AddRange(report.Income.Select(l => Row(new() { ["Category"] = "Income", ["AccountCode"] = l.AccountCode, ["AccountName"] = l.AccountName, ["Amount"] = l.Amount })));
        rows.AddRange(report.Expenses.Select(l => Row(new() { ["Category"] = "Expense", ["AccountCode"] = l.AccountCode, ["AccountName"] = l.AccountName, ["Amount"] = -l.Amount })));
        rows.Add(Row(new() { ["Category"] = "Result", ["AccountCode"] = string.Empty, ["AccountName"] = "Net profit", ["Amount"] = report.NetProfit }));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashFlowAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        CashFlowStatement cf = await _reports.CashFlowStatementAsync(companyId, from, to, ct).ConfigureAwait(false);

        return
        [
            Row(new() { ["Activity"] = "Operating activities", ["Amount"] = cf.Operating }),
            Row(new() { ["Activity"] = "Investing activities", ["Amount"] = cf.Investing }),
            Row(new() { ["Activity"] = "Financing activities", ["Amount"] = cf.Financing }),
            Row(new() { ["Activity"] = "Unclassified", ["Amount"] = cf.Unclassified }),
            Row(new() { ["Activity"] = "Net change in cash", ["Amount"] = cf.NetChange }),
            Row(new() { ["Activity"] = "Opening cash", ["Amount"] = cf.OpeningCash }),
            Row(new() { ["Activity"] = "Closing cash", ["Amount"] = cf.ClosingCash }),
        ];
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

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ChartOfAccountsAsync(long companyId, CancellationToken ct)
    {
        ChartOfAccountsReport report = await _reports.ChartOfAccountsAsync(companyId, ct).ConfigureAwait(false);
        return report.Rows.Select(r => Row(new()
        {
            ["AccountCode"] = r.AccountCode,
            ["AccountName"] = r.AccountName,
            ["Type"] = r.Type,
            ["ReportCategory"] = r.ReportCategory,
            ["IsControl"] = r.IsControl ? "Yes" : string.Empty,
            ["IsActive"] = r.IsActive ? "Yes" : string.Empty,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FinancialSnapshotAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        FinancialSnapshotReport report = await _reports.FinancialSnapshotAsync(companyId, ReportParameters.Date(parameters, "AsAtDate"), ct).ConfigureAwait(false);
        return report.Rows.Select(r => Row(new()
        {
            ["Metric"] = r.Metric,
            ["Amount"] = r.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VatReturnAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        VatReturnReport report = await _reports.VatReturnAsync(companyId, from, to, ct).ConfigureAwait(false);
        return
        [
            Row(new() { ["Metric"] = "Output VAT (sales)", ["Amount"] = report.OutputTax }),
            Row(new() { ["Metric"] = "Input VAT (purchases)", ["Amount"] = report.InputTax }),
            Row(new() { ["Metric"] = "Net VAT Payable", ["Amount"] = report.NetPayable }),
        ];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ControlReconciliationAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        ControlReconciliationReport report = await _reports.ControlReconciliationAsync(companyId, ReportParameters.Date(parameters, "AsAtDate"), ct).ConfigureAwait(false);
        return report.Rows.Select(r => Row(new()
        {
            ["Control"] = r.Control,
            ["AccountCode"] = r.AccountCode,
            ["GlBalance"] = r.GlBalance,
            ["SubLedgerBalance"] = r.SubLedgerBalance,
            ["Difference"] = r.Difference,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VatReturnByCodeAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        VatReturnByCodeReport report = await _reports.VatReturnByCodeAsync(companyId, from, to, ct).ConfigureAwait(false);
        return report.Rows.Select(r => Row(new()
        {
            ["TaxCode"] = r.TaxCode,
            ["OutputTax"] = r.OutputTax,
            ["InputTax"] = r.InputTax,
            ["Net"] = r.Net,
        })).ToList();
    }

    private static IReadOnlyDictionary<string, object?> Row(Dictionary<string, object?> values) => values;
}
