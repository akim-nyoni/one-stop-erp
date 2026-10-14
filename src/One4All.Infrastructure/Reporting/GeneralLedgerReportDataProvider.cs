using Microsoft.EntityFrameworkCore;
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
        "TrialBalance", "TrialBalanceByType", "IncomeStatement", "IncomeStatementByFunction", "BalanceSheet", "CashFlow", "DepartmentalIncomeStatement", "NominalActivity", "JournalDaybook", "TrialBalanceMovement", "ChartOfAccounts", "FinancialSnapshot", "VatReturn", "ControlReconciliation", "VatReturnByCode",
    };

    private readonly IGeneralLedgerReports _reports;
    private readonly Persistence.One4AllDbContext? _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="reports">The GL report services.</param>
    public GeneralLedgerReportDataProvider(IGeneralLedgerReports reports) => _reports = reports;

    /// <summary>Creates the provider with the database the reporting-currency rates come from.</summary>
    /// <param name="reports">The ledger reports.</param>
    /// <param name="db">The database context.</param>
    public GeneralLedgerReportDataProvider(IGeneralLedgerReports reports, Persistence.One4AllDbContext db)
        : this(reports) => _db = db;

    // The common parameters: IncludeUnposted (true / yes / 1) and ReportingCurrency, translated at the spot rate on the date (base when no rate is held).
    private async Task<LedgerReportOptions> OptionsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, DateOnly date, CancellationToken ct)
    {
        // A cost centre or department narrows the report to the lines analysed to it.
        static string? Dimension(IReadOnlyDictionary<string, object?> values, string name)
            => values.TryGetValue(name, out object? given) && given is string text && text.Trim().Length > 0 ? text.Trim().ToUpperInvariant() : null;
        LedgerReportOptions options = await CurrencyOptionsAsync(companyId, parameters, date, ct).ConfigureAwait(false);
        return options with { CostCentre = Dimension(parameters, "CostCentre"), Department = Dimension(parameters, "Department") };
    }

    private async Task<LedgerReportOptions> CurrencyOptionsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, DateOnly date, CancellationToken ct)
    {
        bool unposted = parameters.TryGetValue("IncludeUnposted", out object? flag) && flag switch
        {
            bool b => b,
            string s => s.Trim().ToUpperInvariant() is "TRUE" or "YES" or "Y" or "1",
            _ => false,
        };
        string? currency = parameters.TryGetValue("ReportingCurrency", out object? value) && value is string code && code.Trim().Length == 3 ? code.Trim().ToUpperInvariant() : null;
        if (currency is null || _db is null)
        {
            return new LedgerReportOptions(unposted);
        }

        Core.Accounting.CurrencyCode baseCurrency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        if (baseCurrency.Value == currency)
        {
            return new LedgerReportOptions(unposted);
        }

        decimal? rate = await Accounting.ExchangeRateResolver.FindAsync(_db, baseCurrency, Core.Accounting.CurrencyCode.Parse(currency), Core.System.ExchangeRateType.Spot, date, ct)
            .ConfigureAwait(false);
        return rate is decimal r and > 0m ? new LedgerReportOptions(unposted, currency, r) : new LedgerReportOptions(unposted);
    }

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
            "INCOMESTATEMENTBYFUNCTION" => await IncomeStatementByFunctionAsync(companyId, parameters, ct).ConfigureAwait(false),
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
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        TrialBalanceReport tb = await _reports.TrialBalanceAsync(companyId, asAt, await OptionsAsync(companyId, parameters, asAt, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
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
        IncomeStatement report = await _reports.IncomeStatementAsync(companyId, from, to, await OptionsAsync(companyId, parameters, to, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        IncomeStatement prior = await _reports.IncomeStatementAsync(companyId, from.AddYears(-1), to.AddYears(-1),
            await OptionsAsync(companyId, parameters, to.AddYears(-1), ct).ConfigureAwait(false), ct).ConfigureAwait(false);

        List<IReadOnlyDictionary<string, object?>> rows =
        [
            .. Compared("Income", report.Income.Select(l => (l.AccountCode, l.AccountName, l.Amount)), prior.Income.Select(l => (l.AccountCode, l.AccountName, l.Amount))),
            .. Compared("Expense", report.Expenses.Select(l => (l.AccountCode, l.AccountName, -l.Amount)), prior.Expenses.Select(l => (l.AccountCode, l.AccountName, -l.Amount))),
        ];
        rows.Add(Row(new()
        {
            ["Category"] = "Result", ["AccountCode"] = string.Empty, ["AccountName"] = "Net profit", ["Amount"] = report.NetProfit, ["PriorAmount"] = prior.NetProfit,
            ["Change"] = report.NetProfit - prior.NetProfit,
        }));
        return rows;
    }

    // IAS 1.103: expenses by function. An account's report category names its function; the accounts items post cost of sales to are cost of sales.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> IncomeStatementByFunctionAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly to = ReportParameters.Date(parameters, "ToDate");
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        HashSet<string> costOfSales = _db is null ? [] : new HashSet<string>(await _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && i.CostOfSalesAccountCode != null)
            .Select(i => i.CostOfSalesAccountCode!).Distinct().ToListAsync(ct).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
        IncomeStatement now = await _reports.IncomeStatementAsync(companyId, from, to, await OptionsAsync(companyId, parameters, to, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        IncomeStatement prior = await _reports.IncomeStatementAsync(companyId, from.AddYears(-1), to.AddYears(-1), await OptionsAsync(companyId, parameters, to.AddYears(-1), ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);

        static bool Has(StatementLine line, params string[] words) => words.Any(w => line.Category.Contains(w, StringComparison.OrdinalIgnoreCase) || line.AccountName.Contains(w, StringComparison.OrdinalIgnoreCase));
        string Function(StatementLine expense) =>
            costOfSales.Contains(expense.AccountCode) || Has(expense, "cost of sales", "cost of goods") ? "Cost of sales"
            : Has(expense, "distribution", "selling", "marketing") ? "Distribution costs"
            : Has(expense, "administrative", "administration") ? "Administrative expenses"
            : Has(expense, "finance cost", "interest", "finance") ? "Finance costs"
            : "Other operating expenses";
        (decimal Revenue, decimal OtherIncome, Dictionary<string, decimal> Functions) Split(IncomeStatement statement)
        {
            decimal other = statement.Income.Where(l => Has(l, "other income")).Sum(l => l.Amount);
            Dictionary<string, decimal> functions = statement.Expenses.GroupBy(Function).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
            return (statement.TotalIncome - other, other, functions);
        }

        var (revenue, otherIncome, functions) = Split(now);
        var (priorRevenue, priorOtherIncome, priorFunctions) = Split(prior);
        decimal F(Dictionary<string, decimal> map, string key) => map.GetValueOrDefault(key);
        decimal gross = revenue - F(functions, "Cost of sales"), priorGross = priorRevenue - F(priorFunctions, "Cost of sales");
        decimal operating = gross + otherIncome - F(functions, "Distribution costs") - F(functions, "Administrative expenses") - F(functions, "Other operating expenses");
        decimal priorOperating = priorGross + priorOtherIncome - F(priorFunctions, "Distribution costs") - F(priorFunctions, "Administrative expenses") - F(priorFunctions, "Other operating expenses");
        IReadOnlyDictionary<string, object?> Line(string label, decimal amount, decimal priorAmount) => Row(new() { ["Line"] = label, ["Amount"] = amount, ["PriorAmount"] = priorAmount });
        return
        [
            Line("Revenue", revenue, priorRevenue),
            Line("Cost of sales", -F(functions, "Cost of sales"), -F(priorFunctions, "Cost of sales")),
            Line("Gross profit", gross, priorGross),
            Line("Other income", otherIncome, priorOtherIncome),
            Line("Distribution costs", -F(functions, "Distribution costs"), -F(priorFunctions, "Distribution costs")),
            Line("Administrative expenses", -F(functions, "Administrative expenses"), -F(priorFunctions, "Administrative expenses")),
            Line("Other operating expenses", -F(functions, "Other operating expenses"), -F(priorFunctions, "Other operating expenses")),
            Line("Operating profit", operating, priorOperating),
            Line("Finance costs", -F(functions, "Finance costs"), -F(priorFunctions, "Finance costs")),
            Line("Profit for the period", operating - F(functions, "Finance costs"), priorOperating - F(priorFunctions, "Finance costs")),
        ];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BalanceSheetAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly balanceDate = ReportParameters.Date(parameters, "AsAtDate");
        BalanceSheet report = await _reports.BalanceSheetAsync(companyId, balanceDate, await OptionsAsync(companyId, parameters, balanceDate, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        DateOnly priorDate = balanceDate.AddYears(-1);
        BalanceSheet prior = await _reports.BalanceSheetAsync(companyId, priorDate, await OptionsAsync(companyId, parameters, priorDate, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        static IEnumerable<(string, string, decimal)> Lines(IEnumerable<StatementLine> lines) => lines.Select(l => (l.AccountCode, l.AccountName, l.Amount));
        return
        [
            .. Compared("Asset", Lines(report.Assets), Lines(prior.Assets)),
            .. Compared("Liability", Lines(report.Liabilities), Lines(prior.Liabilities)),
            .. Compared("Equity", Lines(report.Equity), Lines(prior.Equity)),
        ];
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

        // The indirect method: each activity's lines (profit, then the movement on each account), then the activity's total.
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach ((string section, string heading, decimal total) in new[]
        {
            ("Operating", "Operating activities", cf.Operating), ("Investing", "Investing activities", cf.Investing), ("Financing", "Financing activities", cf.Financing),
            ("Unclassified", "Unclassified", cf.Unclassified),
        })
        {
            rows.AddRange(cf.Lines.Where(l => l.Section == section).Select(l => Row(new() { ["Activity"] = "    " + l.Label, ["Amount"] = l.Amount })));
            if (section != "Unclassified" || total != 0m)
            {
                rows.Add(Row(new() { ["Activity"] = heading, ["Amount"] = total }));
            }
        }

        rows.Add(Row(new() { ["Activity"] = "Net change in cash", ["Amount"] = cf.NetChange }));
        rows.Add(Row(new() { ["Activity"] = "Opening cash", ["Amount"] = cf.OpeningCash }));
        rows.Add(Row(new() { ["Activity"] = "Closing cash", ["Amount"] = cf.ClosingCash }));
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

    // A statement section beside its comparative: every account in either year, in this year's order and then the accounts only the prior year had.
    private static IEnumerable<IReadOnlyDictionary<string, object?>> Compared(string category, IEnumerable<(string Code, string Name, decimal Amount)> current,
        IEnumerable<(string Code, string Name, decimal Amount)> prior)
    {
        Dictionary<string, (string Code, string Name, decimal Amount)> before = prior.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => (g.Key, g.First().Name, g.Sum(l => l.Amount)));
        List<(string Code, string Name, decimal Amount)> now = [.. current];
        foreach ((string code, string name, decimal amount) in now.Concat(before.Values.Where(p => now.All(c => c.Code != p.Code)).Select(p => (p.Code, p.Name, 0m))))
        {
            decimal earlier = before.TryGetValue(code, out var p) ? p.Amount : 0m;
            yield return Row(new() { ["Category"] = category, ["AccountCode"] = code, ["AccountName"] = name, ["Amount"] = amount, ["PriorAmount"] = earlier, ["Change"] = amount - earlier });
        }
    }
}
