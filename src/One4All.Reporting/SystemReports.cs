using One4All.Reporting.Model;

namespace One4All.Reporting;

/// <summary>
/// The shipped system report definitions built in code (brief §14), seeded into <c>rpt.report_definition</c>.
/// These are read-only; a customer copies one to a company/user version to customise it.
/// </summary>
public static class SystemReports
{
    /// <summary>The Trial Balance report code.</summary>
    public const string TrialBalanceCode = "GL-TB";

    /// <summary>Builds the Trial Balance report definition (brief §14 report 1).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition TrialBalance()
    {
        ReportDefinition def = new()
        {
            Code = TrialBalanceCode,
            Name = "Trial Balance",
            Module = "GeneralLedger",
            Description = "Account balances as at a date; debits must equal credits.",
        };
        def.DataSources.Add(new ReportDataSource { Name = "Main", Kind = DataSourceKind.InMemory, Command = "TrialBalance" });
        def.Parameters.Add(new ReportParameter { Name = "AsAtDate", Type = ParameterType.Date, Prompt = "As at date" });
        def.Parameters.Add(new ReportParameter { Name = "IncludeUnposted", Type = ParameterType.Boolean, Prompt = "Include unposted (draft and deferred) journals", Required = false });
        def.Parameters.Add(new ReportParameter { Name = "ReportingCurrency", Type = ParameterType.String, Prompt = "Reporting currency (blank: base)", Required = false });
        def.Parameters.Add(new ReportParameter { Name = "CostCentre", Type = ParameterType.String, Prompt = "Cost centre (blank: all)", Required = false });
        def.Parameters.Add(new ReportParameter { Name = "Department", Type = ParameterType.String, Prompt = "Department (blank: all)", Required = false });
        def.Parameters.Add(new ReportParameter { Name = "Branch", Type = ParameterType.String, Prompt = "Branch (blank: all)", Required = false });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportHeader,
            Height = 46,
            Elements =
            [
                new ReportElement { Kind = ElementKind.Expression, Name = "company", Expression = "CompanyName", X = 0, Y = 0, Width = 400, Height = 16, Bold = true, FontSize = 13 },
                new ReportElement { Kind = ElementKind.Expression, Name = "title", Expression = "\"Trial Balance as at \" + Format(Param(\"AsAtDate\"), \"yyyy-MM-dd\")", X = 0, Y = 18, Width = 400, Height = 14, Bold = true },
                new ReportElement { Kind = ElementKind.Expression, Name = "curLbl", Expression = "\"Currency: \" + Param(\"Currency\")", X = 0, Y = 32, Width = 200, Height = 12, FontSize = 8 },
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.PageHeader,
            Height = 18,
            Elements =
            [
                Label("hCode", "Account", 0, 2, 80, 12, bold: true),
                Label("hName", "Name", 90, 2, 210, 12, bold: true),
                Label("hDebit", "Debit", 300, 2, 90, 12, bold: true, align: ReportHAlign.Right),
                Label("hCredit", "Credit", 400, 2, 90, 12, bold: true, align: ReportHAlign.Right),
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.Detail,
            Height = 14,
            Elements =
            [
                Field("dCode", "AccountCode", 0, 0, 80, 12),
                Field("dName", "AccountName", 90, 0, 210, 12),
                Field("dDebit", "Debit", 300, 0, 90, 12, format: "N2", align: ReportHAlign.Right),
                Field("dCredit", "Credit", 400, 0, 90, 12, format: "N2", align: ReportHAlign.Right),
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportFooter,
            Height = 20,
            Elements =
            [
                Label("tLabel", "Total", 90, 4, 100, 14, bold: true),
                Aggregate("tDebit", "Debit", 300, 4, 90, 14, bold: true, align: ReportHAlign.Right),
                Aggregate("tCredit", "Credit", 400, 4, 90, 14, bold: true, align: ReportHAlign.Right),
            ],
        });

        return def;
    }

    /// <summary>The customer statement report code.</summary>
    public const string CustomerStatementCode = "AR-STMT";

    /// <summary>Builds the open-item customer statement definition (brief §14 report 26).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition CustomerStatement()
    {
        ReportDefinition def = new()
        {
            Code = CustomerStatementCode,
            Name = "Customer Statement",
            Module = "Sales",
            Description = "Outstanding items for a customer, with the closing balance.",
        };
        def.DataSources.Add(new ReportDataSource { Name = "Main", Kind = DataSourceKind.InMemory, Command = "CustomerStatement" });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportHeader,
            Height = 60,
            Elements =
            [
                new ReportElement { Kind = ElementKind.Expression, Name = "company", Expression = "CompanyName", X = 0, Y = 0, Width = 400, Height = 16, Bold = true, FontSize = 13 },
                Label("title", "Statement of Account", 0, 20, 300, 14, bold: true, size: 11),
                new ReportElement { Kind = ElementKind.Expression, Name = "cust", Expression = "\"Account: \" + Param(\"TraderCode\") + \"  \" + Param(\"TraderName\")", X = 0, Y = 38, Width = 400, Height = 12, FontSize = 9 },
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.PageHeader,
            Height = 18,
            Elements =
            [
                Label("hDate", "Date", 0, 2, 70, 12, bold: true),
                Label("hType", "Type", 75, 2, 90, 12, bold: true),
                Label("hNum", "Number", 170, 2, 100, 12, bold: true),
                Label("hDue", "Due", 275, 2, 70, 12, bold: true),
                Label("hOut", "Outstanding", 420, 2, 90, 12, bold: true, align: ReportHAlign.Right),
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.Detail,
            Height = 14,
            Elements =
            [
                Field("dDate", "Date", 0, 0, 70, 12, format: "yyyy-MM-dd"),
                Field("dType", "Type", 75, 0, 90, 12),
                Field("dNum", "Number", 170, 0, 100, 12),
                Field("dDue", "Due", 275, 0, 70, 12, format: "yyyy-MM-dd"),
                Field("dOut", "Outstanding", 420, 0, 90, 12, format: "N2", align: ReportHAlign.Right),
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportFooter,
            Height = 22,
            Elements =
            [
                Label("bLabel", "Balance due", 275, 4, 140, 14, bold: true),
                Aggregate("bTotal", "Outstanding", 420, 4, 90, 14, bold: true, align: ReportHAlign.Right),
            ],
        });

        return def;
    }

    /// <summary>The Income Statement report code.</summary>
    public const string IncomeStatementCode = "GL-IS";

    /// <summary>The Balance Sheet report code.</summary>
    public const string BalanceSheetCode = "GL-BS";

    /// <summary>The Statement of Cash Flows report code.</summary>
    public const string CashFlowCode = "GL-CF";

    /// <summary>The Departmental Income Statement report code.</summary>
    public const string DepartmentalIncomeStatementCode = "GL-DEPT-IS";

    /// <summary>The Trial Balance by Type report code.</summary>
    public const string TrialBalanceByTypeCode = "GL-TBTYPE";

    /// <summary>The Journal Daybook report code.</summary>
    public const string JournalDaybookCode = "GL-DAYBOOK";

    /// <summary>The Trial Balance Movement report code.</summary>
    public const string TrialBalanceMovementCode = "GL-TBMOV";

    /// <summary>The Chart of Accounts report code.</summary>
    public const string ChartOfAccountsCode = "GL-COA";

    /// <summary>The Financial Snapshot report code.</summary>
    public const string FinancialSnapshotCode = "GL-SNAP";

    /// <summary>The VAT Return report code.</summary>
    public const string VatReturnCode = "GL-VAT";

    /// <summary>The Control-Account Reconciliation report code.</summary>
    public const string ControlReconciliationCode = "GL-CTRL";

    /// <summary>The VAT Return by Code report code.</summary>
    public const string VatReturnByCodeCode = "GL-VATC";

    /// <summary>The Customer Register report code.</summary>
    public const string CustomerRegisterCode = "AR-CUST";

    /// <summary>The Supplier Register report code.</summary>
    public const string SupplierRegisterCode = "AP-SUP";

    /// <summary>The Aged Debtors report code.</summary>
    public const string AgedDebtorsCode = "AR-AGE";

    /// <summary>The Aged Creditors report code.</summary>
    public const string AgedCreditorsCode = "AP-AGE";

    /// <summary>The Customer Credit Exposure report code.</summary>
    public const string CreditExposureCode = "AR-CREDIT";

    /// <summary>The Cash-Flow Forecast report code.</summary>
    public const string CashFlowForecastCode = "CB-FLOW";

    /// <summary>The Payments Due report code.</summary>
    public const string PaymentsDueCode = "AP-DUE";

    /// <summary>A column in a generic listing report.</summary>
    /// <param name="Field">The bound data field.</param>
    /// <param name="Header">The column header.</param>
    /// <param name="Width">The width in points.</param>
    /// <param name="Money">Whether the column is a money amount (right-aligned, N2, totalled).</param>
    public readonly record struct Column(string Field, string Header, double Width, bool Money = false);

    /// <summary>Builds the Income Statement definition (brief §14 report 7).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition IncomeStatement() => Listing(IncomeStatementCode, "Income Statement", "GeneralLedger",
        "Income and expenses for a period; the difference is the net profit - beside the same period a year earlier and the change.", "IncomeStatement",
        [new Column("Category", "Section", 110), new Column("AccountName", "Account", 220), new Column("Amount", "This period", 95, true), new Column("PriorAmount", "Prior year", 95, true),
            new Column("Change", "Change", 90, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String), ("Department", ParameterType.String)]);

    /// <summary>The Income Statement by Function report code.</summary>
    public const string IncomeStatementByFunctionCode = "GL-IS-FN";

    /// <summary>Builds the Income Statement by Function (IAS 1.103, the cost-of-sales method).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition IncomeStatementByFunction() => Listing(IncomeStatementByFunctionCode, "Income Statement by Function", "GeneralLedger",
        "Revenue, cost of sales and gross profit, then distribution, administrative and other operating expenses, operating profit, finance costs and the "
            + "profit for the period, beside the same period a year earlier. Expenses fall to a function by their account's report category (\"Distribution\", "
            + "\"Administrative\", \"Finance\"…); the accounts items post cost of sales to are cost of sales.", "IncomeStatementByFunction",
        [new Column("Line", "", 260), new Column("Amount", "This period", 100, true), new Column("PriorAmount", "Prior year", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String), ("Department", ParameterType.String)]);

    /// <summary>The code of the statement laid out by a reporting tree.</summary>
    public const string TreeStatementCode = "GL-TREE";

    /// <summary>Builds the reporting-tree statement: any tree's headings with their rolled-up amounts for a period (or a balance to a date).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition TreeStatement()
    {
        ReportDefinition definition = Listing(TreeStatementCode, "Statement by Reporting Tree", "GeneralLedger",
            "A financial statement laid out by a reporting tree (statutory, management or consolidation): each heading with everything mapped beneath it rolled up.", "TreeRollup",
            [new Column("Code", "Code", 110), new Column("Heading", "Heading", 300), new Column("Amount", "Amount", 120, true)],
            [("TreeCode", ParameterType.String), ("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        ReportBand detail = definition.BandOfType(BandType.Detail)!;
        foreach (ReportElement element in detail.Elements)
        {
            element.ConditionalFormats.Add(new ConditionalFormat { Condition = "!IsLeaf", Bold = true });
        }

        return definition;
    }

    /// <summary>Builds the Journal Daybook definition (audit journal listing).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition JournalDaybook() => Listing(JournalDaybookCode, "Journal Daybook", "GeneralLedger",
        "Every journal over a period - date, number, source module, document, narrative and base total (draft and deferred too when asked; a branch, cost centre or department narrows the lines counted).", "JournalDaybook",
        [new Column("Date", "Date", 95), new Column("JournalNumber", "Journal", 110), new Column("SourceModule", "Module", 70), new Column("DocumentType", "Doc type", 90), new Column("DocumentNumber", "Doc no.", 100), new Column("Narrative", "Narrative", 200), new Column("Amount", "Amount", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String), ("Department", ParameterType.String)]);

    /// <summary>Builds the VAT Return by Code definition (multi-rate breakdown).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition VatReturnByCode() => Listing(VatReturnByCodeCode, "VAT Return by Code", "GeneralLedger",
        "Output and input tax by tax code for a period, with the net per code - the multi-rate VAT breakdown.", "VatReturnByCode",
        [new Column("TaxCode", "Tax code", 120), new Column("OutputTax", "Output", 110, true), new Column("InputTax", "Input", 110, true), new Column("Net", "Net", 110, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Control-Account Reconciliation definition (GL vs sub-ledgers).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition ControlReconciliation() => Listing(ControlReconciliationCode, "Control Reconciliation", "GeneralLedger",
        "GL control-account balances (AR, AP, Stock) versus their sub-ledger totals, with the difference - zero when they agree.", "ControlReconciliation",
        [new Column("Control", "Control", 160), new Column("AccountCode", "Account", 90), new Column("GlBalance", "GL balance", 110, true), new Column("SubLedgerBalance", "Sub-ledger", 110, true), new Column("Difference", "Difference", 100, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the VAT Return definition (output vs input tax for a period).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition VatReturn() => Listing(VatReturnCode, "VAT Return", "GeneralLedger",
        "Output tax on sales less input tax on purchases for a period, giving the net VAT payable.", "VatReturn",
        [new Column("Metric", "Metric", 220), new Column("Amount", "Amount", 130, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Financial Snapshot definition (executive summary).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition FinancialSnapshot() => Listing(FinancialSnapshotCode, "Financial Snapshot", "GeneralLedger",
        "Headline figures as at a date: assets, liabilities, equity and year-to-date income, expenses and net profit.", "FinancialSnapshot",
        [new Column("Metric", "Metric", 220), new Column("Amount", "Amount", 130, true)],
        [("AsAtDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String), ("Department", ParameterType.String)]);

    /// <summary>Builds the Chart of Accounts definition (reference listing).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition ChartOfAccounts() => Listing(ChartOfAccountsCode, "Chart of Accounts", "GeneralLedger",
        "Every ledger account with its type, report category, control flag and active status.", "ChartOfAccounts",
        [new Column("AccountCode", "Account", 90), new Column("AccountName", "Name", 220), new Column("Type", "Type", 90), new Column("ReportCategory", "Category", 140), new Column("IsControl", "Control", 70), new Column("IsActive", "Active", 60)],
        []);

    /// <summary>Builds the Trial Balance Movement definition (opening/movement/closing per account).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition TrialBalanceMovement() => Listing(TrialBalanceMovementCode, "Trial Balance Movement", "GeneralLedger",
        "Per account: opening balance brought forward, debits and credits in the period, and the closing balance.", "TrialBalanceMovement",
        [new Column("AccountCode", "Account", 90), new Column("AccountName", "Name", 200), new Column("Opening", "Opening", 100, true), new Column("Debit", "Debit", 90, true), new Column("Credit", "Credit", 90, true), new Column("Closing", "Closing", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String), ("Department", ParameterType.String)]);

    /// <summary>Builds the Balance Sheet definition (brief §14 report 6).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition BalanceSheet() => Listing(BalanceSheetCode, "Balance Sheet", "GeneralLedger",
        "Assets, liabilities and equity as at a date; assets equal liabilities plus equity - beside the position a year earlier and the change.", "BalanceSheet",
        [new Column("Category", "Section", 110), new Column("AccountName", "Account", 220), new Column("Amount", "This year", 95, true), new Column("PriorAmount", "Prior year", 95, true),
            new Column("Change", "Change", 90, true)],
        [("AsAtDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String), ("Department", ParameterType.String)]);

    /// <summary>Builds the Statement of Cash Flows definition (IAS 7, movement method).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition CashFlow() => Listing(CashFlowCode, "Statement of Cash Flows", "GeneralLedger",
        "Cash generated by operating, investing and financing activities for a period, reconciled to the change in cash.", "CashFlow",
        [new Column("Activity", "Activity", 280), new Column("Amount", "Amount", 120, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String), ("Department", ParameterType.String)]);

    /// <summary>Builds the Departmental Income Statement definition (P&amp;L for one department).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition DepartmentalIncomeStatement() => Listing(DepartmentalIncomeStatementCode, "Departmental Income Statement", "GeneralLedger",
        "Income and expenses for a period restricted to one department; the difference is the department's net profit.", "DepartmentalIncomeStatement",
        [new Column("Category", "Section", 120), new Column("AccountName", "Account", 250), new Column("Amount", "Amount", 100, true)],
        [("Department", ParameterType.String), ("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String),
            ("Branch", ParameterType.String), ("CostCentre", ParameterType.String)]);

    /// <summary>Builds the Trial Balance by Type definition (summary trial balance grouped by account type).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition TrialBalanceByType() => Listing(TrialBalanceByTypeCode, "Trial Balance by Type", "GeneralLedger",
        "The trial balance summarised to one line per account type, with debit and credit totals that balance.", "TrialBalanceByType",
        [new Column("Type", "Account type", 200), new Column("Debit", "Debit", 120, true), new Column("Credit", "Credit", 120, true)],
        [("AsAtDate", ParameterType.Date), ("IncludeUnposted", ParameterType.Boolean), ("ReportingCurrency", ParameterType.String), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String), ("Department", ParameterType.String)]);

    /// <summary>Builds the Customer Register definition (AR master listing).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition CustomerRegister() => Listing(CustomerRegisterCode, "Customer Register", "Sales",
        "Customer master: code, name, currency, credit limit, payment terms, on-hold flag and analysis codes.", "CustomerRegister",
        [new Column("Code", "Account", 90), new Column("Name", "Name", 200), new Column("Currency", "Ccy", 55), new Column("CreditLimit", "Credit limit", 100, true), new Column("TermsDays", "Terms", 60, true), new Column("OnHold", "On hold", 70),
            new Column("Analysis1", "Analysis 1", 70), new Column("Analysis2", "Analysis 2", 70), new Column("Analysis3", "Analysis 3", 70)],
        []);

    /// <summary>Builds the Supplier Register definition (AP master listing).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition SupplierRegister() => Listing(SupplierRegisterCode, "Supplier Register", "Purchasing",
        "Supplier master: code, name, currency, payment terms, on-hold flag and analysis codes.", "SupplierRegister",
        [new Column("Code", "Account", 90), new Column("Name", "Name", 200), new Column("Currency", "Ccy", 55), new Column("TermsDays", "Terms", 60, true), new Column("OnHold", "On hold", 70),
            new Column("Analysis1", "Analysis 1", 70), new Column("Analysis2", "Analysis 2", 70), new Column("Analysis3", "Analysis 3", 70)],
        []);

    /// <summary>Builds the Aged Debtors definition (brief §14 report 29).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition AgedDebtors() => Listing(AgedDebtorsCode, "Aged Debtors", "Sales",
        "Outstanding customer balances by ageing band as at a date.", "AgedDebtors",
        [new Column("TraderCode", "Account", 80), new Column("TraderName", "Name", 150), new Column("Current", "Current", 70, true), new Column("Days1To30", "1-30", 60, true), new Column("Days31To60", "31-60", 60, true), new Column("Days61To90", "61-90", 60, true), new Column("Days91Plus", "91+", 60, true), new Column("Total", "Total", 80, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Aged Creditors definition (brief §14 report 30).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition AgedCreditors() => Listing(AgedCreditorsCode, "Aged Creditors", "Purchasing",
        "Outstanding supplier balances by ageing band as at a date.", "AgedCreditors",
        [new Column("TraderCode", "Account", 80), new Column("TraderName", "Name", 150), new Column("Current", "Current", 70, true), new Column("Days1To30", "1-30", 60, true), new Column("Days31To60", "31-60", 60, true), new Column("Days61To90", "61-90", 60, true), new Column("Days91Plus", "91+", 60, true), new Column("Total", "Total", 80, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Customer Credit Exposure definition (credit control).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition CreditExposure() => Listing(CreditExposureCode, "Customer Credit Exposure", "Sales",
        "Each customer's credit limit, current balance and remaining credit, flagging on-hold and over-limit accounts.", "CreditExposure",
        [new Column("CustomerCode", "Account", 90), new Column("CustomerName", "Name", 170), new Column("CreditLimit", "Limit", 90, true), new Column("Balance", "Balance", 90, true), new Column("Available", "Available", 90, true), new Column("OnHold", "On hold", 70), new Column("OverLimit", "Over limit", 70)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>The Stock Valuation report code.</summary>
    public const string StockValuationCode = "INV-VAL";

    /// <summary>The Stock Movements report code.</summary>
    public const string StockMovementsCode = "INV-MOVE";

    /// <summary>The Reorder report code.</summary>
    public const string ReorderCode = "INV-REORDER";

    /// <summary>The Back-order Book report code.</summary>
    public const string BackOrdersCode = "INV-BACKORDER";

    /// <summary>The Slow-moving Stock report code.</summary>
    public const string SlowStockCode = "INV-SLOW";

    /// <summary>The Fixed Asset Register report code.</summary>
    public const string AssetRegisterCode = "FA-REG";

    /// <summary>The Depreciation Forecast report code.</summary>
    public const string DepreciationForecastCode = "FA-FCST";

    /// <summary>The Asset Maintenance Log report code.</summary>
    public const string AssetMaintenanceCode = "FA-MAINT";

    /// <summary>The Asset Disposals report code.</summary>
    public const string AssetDisposalsCode = "FA-DISP";

    /// <summary>The Payroll Summary report code.</summary>
    public const string PayrollSummaryCode = "PAY-SUM";

    /// <summary>The Employee Loans report code.</summary>
    public const string EmployeeLoansCode = "PAY-LOANS";

    /// <summary>The Payroll Deductions report code.</summary>
    public const string PayrollDeductionsCode = "PAY-DED";

    /// <summary>The Employee Register report code.</summary>
    public const string EmployeeRegisterCode = "HR-EMP";

    /// <summary>The Leave Balances report code.</summary>
    public const string LeaveBalancesCode = "PAY-LEAVE";

    /// <summary>The Property Register report code.</summary>
    public const string PropertyRegisterCode = "MUN-PROP";

    /// <summary>The Budget vs Actual report code.</summary>
    public const string BudgetVsActualCode = "BUD-BVA";

    /// <summary>The CRM Pipeline report code.</summary>
    public const string PipelineCode = "CRM-PIPE";

    /// <summary>The Budget Listing report code.</summary>
    public const string BudgetListCode = "BUD-LIST";

    /// <summary>The Recurring Journals report code.</summary>
    public const string RecurringJournalsCode = "GL-RECUR";

    /// <summary>The Period Status report code.</summary>
    public const string PeriodStatusCode = "SYS-PERIOD";

    /// <summary>The Expense Claims report code.</summary>
    public const string ExpenseClaimsCode = "EXP-CLAIMS";

    /// <summary>The Inventory ABC Analysis report code.</summary>
    public const string AbcAnalysisCode = "INV-ABC";

    /// <summary>The Item Master report code.</summary>
    public const string ItemMasterCode = "INV-ITEMS";

    /// <summary>The Stock Ageing report code.</summary>
    public const string StockAgeingCode = "INV-AGE";

    /// <summary>Builds the Stock Ageing definition (on-hand value by age since last movement).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition StockAgeing() => Listing(StockAgeingCode, "Stock Ageing", "Inventory",
        "On-hand items with their value and the age band since last movement (0-30 / 31-60 / 61-90 / 90+ days).", "StockAgeing",
        [new Column("ItemCode", "Item", 100), new Column("Description", "Description", 190), new Column("Warehouse", "Warehouse", 90), new Column("Quantity", "Qty", 70, true), new Column("Value", "Value", 90, true), new Column("AgeBand", "Age band", 80)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Item Master definition (stock item reference listing).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition ItemMaster() => Listing(ItemMasterCode, "Item Master", "Inventory",
        "Stock item master: code, description, unit, reorder level, default accounts and active status.", "ItemMaster",
        [new Column("Code", "Item", 100), new Column("Description", "Description", 200), new Column("Uom", "UoM", 60), new Column("ReorderLevel", "Reorder", 80, true), new Column("RevenueAccount", "Revenue", 80), new Column("CostAccount", "COS", 70), new Column("Active", "Active", 60)],
        []);

    /// <summary>Builds the Inventory ABC Analysis definition (Pareto value classification).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition AbcAnalysis() => Listing(AbcAnalysisCode, "Inventory ABC Analysis", "Inventory",
        "Items ranked by stock value with the cumulative percentage and Pareto class (A/B/C).", "AbcAnalysis",
        [new Column("ItemCode", "Item", 100), new Column("Description", "Description", 200), new Column("Value", "Value", 100, true), new Column("CumulativePercent", "Cum %", 80, true), new Column("Class", "Class", 60)],
        []);

    /// <summary>Builds the Cash-Flow Forecast definition (treasury).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition CashFlowForecast() => Listing(CashFlowForecastCode, "Cash-Flow Forecast", "CashBook",
        "Expected receipts and payments from open receivables and payables, bucketed by due date, with the running net.", "CashFlowForecast",
        [new Column("Bucket", "Period", 110), new Column("Inflow", "Receipts", 100, true), new Column("Outflow", "Payments", 100, true), new Column("Net", "Net", 100, true), new Column("RunningNet", "Running net", 110, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Payments Due definition (payables schedule).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition PaymentsDue() => Listing(PaymentsDueCode, "Payments Due", "Purchasing",
        "Outstanding supplier invoices ordered by due date, with days until due (negative when overdue).", "PaymentsDue",
        [new Column("SupplierCode", "Account", 80), new Column("SupplierName", "Supplier", 150), new Column("DocumentNumber", "Invoice", 100), new Column("DueDate", "Due", 95), new Column("DaysUntilDue", "Days", 60, true), new Column("Outstanding", "Outstanding", 100, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Stock Valuation definition (brief §4.11 inventory).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition StockValuation() => Listing(StockValuationCode, "Stock Valuation", "Inventory",
        "Quantity on hand and value per item and warehouse, with the item's category and costing method - now, or as at any earlier date (from the stock movements to it).", "StockValuation",
        [new Column("ItemCode", "Item", 80), new Column("Description", "Description", 160), new Column("Category", "Category", 80), new Column("Costing", "Costing", 85),
            new Column("WarehouseCode", "Warehouse", 80), new Column("Quantity", "Qty", 65, true), new Column("AverageCost", "Avg cost", 75, true), new Column("Value", "Value", 90, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Stock Movements definition (inventory transaction history).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition StockMovements() => Listing(StockMovementsCode, "Stock Movements", "Inventory",
        "Every stock movement over a period - receipts, issues, transfers, adjustments and reversals.", "StockMovements",
        [new Column("Date", "Date", 95), new Column("Item", "Item", 100), new Column("Warehouse", "Warehouse", 90), new Column("Type", "Type", 90), new Column("Qty", "Qty", 70, true), new Column("Value", "Value", 90, true), new Column("Reference", "Reference", 120)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Reorder definition (inventory replenishment).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Reorder() => Listing(ReorderCode, "Reorder Report", "Inventory",
        "Items at or below their reorder level, with the suggested reorder quantity - a purchasing aid.", "Reorder",
        [new Column("ItemCode", "Item", 100), new Column("Description", "Description", 190), new Column("OnHand", "On hand", 80, true), new Column("ReorderLevel", "Reorder level", 90, true), new Column("Suggested", "Suggested order", 100, true)],
        []);

    /// <summary>Builds the Back-order Book definition (undespatched sales-order lines against stock on hand).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition BackOrders() => Listing(BackOrdersCode, "Back-order Book", "Inventory",
        "Every undespatched sales-order line, oldest order first, with stock on hand allocated to earlier orders so each line shows whether it can be fulfilled and any shortfall.", "BackOrders",
        [new Column("OrderNumber", "Order", 90), new Column("OrderDate", "Date", 80), new Column("Customer", "Customer", 90), new Column("ItemCode", "Item", 100), new Column("Outstanding", "Outstanding", 90, true), new Column("OnHand", "On hand", 80, true), new Column("CanFulfil", "Fulfil?", 60), new Column("Shortfall", "Shortfall", 80, true)],
        []);

    /// <summary>Builds the Slow-moving Stock definition (dead-stock analysis).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition SlowStock() => Listing(SlowStockCode, "Slow-moving Stock", "Inventory",
        "Items on hand with no movement since the given date - capital tied up in dead stock.", "SlowStock",
        [new Column("ItemCode", "Item", 100), new Column("Description", "Description", 180), new Column("Warehouse", "Warehouse", 90), new Column("Quantity", "Qty", 70, true), new Column("Value", "Value", 90, true), new Column("LastMovement", "Last movement", 110)],
        [("SinceDate", ParameterType.Date)]);

    /// <summary>Builds the Fixed Asset Register definition (brief §4.14).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition AssetRegister() => Listing(AssetRegisterCode, "Fixed Asset Register", "FixedAssets",
        "Assets with cost, accumulated depreciation and net book value.", "AssetRegister",
        [new Column("AssetNumber", "Asset", 90), new Column("Description", "Description", 200), new Column("Cost", "Cost", 90, true), new Column("AccumulatedDepreciation", "Accum. depr.", 90, true), new Column("NetBookValue", "NBV", 90, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Depreciation Forecast definition (fixed assets).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition DepreciationForecast() => Listing(DepreciationForecastCode, "Depreciation Forecast", "FixedAssets",
        "Projected monthly depreciation across active assets and the aggregate closing net book value.", "DepreciationForecast",
        [new Column("Period", "Month", 90), new Column("Charge", "Depreciation", 110, true), new Column("ClosingNetBookValue", "Closing NBV", 120, true)],
        [("StartMonth", ParameterType.Date), ("Months", ParameterType.Integer)]);

    /// <summary>Builds the Asset Disposals definition (fixed assets).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition AssetDisposals() => Listing(AssetDisposalsCode, "Asset Disposals", "FixedAssets",
        "Disposed assets with proceeds, net book value at disposal, the gain or loss, and the journal - newest first.", "AssetDisposals",
        [new Column("AssetNumber", "Asset", 90), new Column("Date", "Date", 95), new Column("Proceeds", "Proceeds", 100, true), new Column("NetBookValue", "NBV", 100, true), new Column("GainOrLoss", "Gain/(loss)", 100, true), new Column("Journal", "Journal", 100)],
        []);

    /// <summary>Builds the Asset Maintenance Log definition (fixed assets).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition AssetMaintenance() => Listing(AssetMaintenanceCode, "Asset Maintenance Log", "FixedAssets",
        "Maintenance and service events logged against assets, newest first, with the informational cost.", "AssetMaintenance",
        [new Column("AssetNumber", "Asset", 90), new Column("Asset", "Description", 160), new Column("Date", "Date", 95), new Column("Work", "Work done", 200), new Column("Provider", "Provider", 120), new Column("Cost", "Cost", 90, true)],
        []);

    /// <summary>Builds the Payroll Summary definition (brief §4.15.6).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition PayrollSummary() => Listing(PayrollSummaryCode, "Payroll Summary", "Payroll",
        "Gross, deductions and net pay per employee for a period.", "PayrollSummary",
        [new Column("Employee", "Employee", 220), new Column("Gross", "Gross", 100, true), new Column("Deductions", "Deductions", 100, true), new Column("Net", "Net", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Employee Register definition (HR reference listing).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition EmployeeRegister() => Listing(EmployeeRegisterCode, "Employee Register", "HumanResources",
        "All employees with number, name, department, cost centre, status and basic salary.", "EmployeeRegister",
        [new Column("EmployeeNumber", "Number", 90), new Column("FullName", "Name", 200), new Column("Department", "Department", 120), new Column("CostCentre", "Cost centre", 110), new Column("Status", "Status", 90), new Column("BasicSalary", "Basic", 100, true)],
        []);

    /// <summary>Builds the Payroll Deductions definition (statutory-return view).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition PayrollDeductions() => Listing(PayrollDeductionsCode, "Payroll Deductions", "Payroll",
        "Deductions aggregated by element across payslips in the period, with the number of employees affected.", "PayrollDeductions",
        [new Column("ElementCode", "Element", 100), new Column("Description", "Description", 200), new Column("Employees", "Employees", 90, true), new Column("Amount", "Amount", 110, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Employee Loans definition (Payroll module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition EmployeeLoans() => Listing(EmployeeLoansCode, "Employee Loans", "Payroll",
        "Outstanding employee loans - principal, balance, installment and status.", "EmployeeLoans",
        [new Column("Employee", "Employee", 180), new Column("Reference", "Reference", 100), new Column("Principal", "Principal", 100, true), new Column("Outstanding", "Outstanding", 100, true), new Column("Installment", "Installment", 100, true)],
        []);

    /// <summary>Builds the Leave Balances definition (Payroll module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition LeaveBalances() => Listing(LeaveBalancesCode, "Leave Balances", "Payroll",
        "Employee leave balances by type - days accrued, taken and available.", "LeaveBalances",
        [new Column("Employee", "Employee", 180), new Column("LeaveType", "Type", 90), new Column("Accrued", "Accrued", 90, true), new Column("Taken", "Taken", 90, true), new Column("Balance", "Balance", 90, true)],
        []);

    /// <summary>Builds the Property Register definition (brief §4.9 municipality).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition PropertyRegister() => Listing(PropertyRegisterCode, "Property Register", "Municipal",
        "Stands on the municipal register with ward, valuation and ratepayer.", "PropertyRegister",
        [new Column("StandNumber", "Stand", 100), new Column("Ward", "Ward", 120), new Column("Ratepayer", "Ratepayer", 150), new Column("Valuation", "Valuation", 110, true)],
        []);

    /// <summary>Builds the Budget vs Actual definition (brief §4.18.6).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition BudgetVsActual() => Listing(BudgetVsActualCode, "Budget vs Actual", "Budget",
        "Active budget compared to GL actuals by account, with the variance.", "BudgetVsActual",
        [new Column("AccountCode", "Account", 100), new Column("Budget", "Budget", 100, true), new Column("Actual", "Actual", 100, true), new Column("Variance", "Variance", 100, true)],
        [("FiscalYearId", ParameterType.Integer), ("Department", ParameterType.String), ("Period", ParameterType.Integer)]);

    /// <summary>Builds the Period Status definition (month-end close aid).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition PeriodStatus() => Listing(PeriodStatusCode, "Period Status", "System",
        "Accounting periods with their open/closed status for the key ledgers (GL, AR, AP) - a month-end close aid.", "PeriodStatus",
        [new Column("Period", "Period", 140), new Column("Start", "Start", 95), new Column("End", "End", 95), new Column("GL", "GL", 80), new Column("AR", "AR", 80), new Column("AP", "AP", 80)],
        []);

    /// <summary>Builds the Recurring Journals definition (standing GL journal templates).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition RecurringJournals() => Listing(RecurringJournalsCode, "Recurring Journals", "GeneralLedger",
        "Standing recurring journal templates with code, description, source module, value, balanced and active flags.", "RecurringJournals",
        [new Column("Code", "Code", 100), new Column("Description", "Description", 200), new Column("SourceModule", "Module", 70), new Column("Amount", "Amount", 100, true), new Column("Balanced", "Balanced", 70), new Column("Active", "Active", 60)],
        []);

    /// <summary>Builds the Budget Listing definition (budget register).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition BudgetList() => Listing(BudgetListCode, "Budget Listing", "Budget",
        "All budget versions with type, department, status, active flag and annual total.", "BudgetList",
        [new Column("Label", "Budget", 180), new Column("Type", "Type", 100), new Column("Department", "Department", 110), new Column("Version", "Ver", 50, true), new Column("Status", "Status", 90), new Column("Active", "Active", 60), new Column("AnnualTotal", "Annual total", 110, true)],
        []);

    /// <summary>Builds the CRM Pipeline definition (brief §4.16).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Pipeline() => Listing(PipelineCode, "Sales Pipeline", "CRM",
        "Open opportunities by stage - estimated value, win probability and the probability-weighted value.", "Pipeline",
        [new Column("Name", "Opportunity", 170), new Column("Contact", "Contact", 130), new Column("Stage", "Stage", 100), new Column("Probability", "Prob %", 70, true), new Column("Estimated", "Estimated", 100, true), new Column("Weighted", "Weighted", 100, true)],
        []);

    /// <summary>Builds the Expense Claims definition (Expenses module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition ExpenseClaims() => Listing(ExpenseClaimsCode, "Expense Claims", "Expenses",
        "Employee expense claims with reference, date, total, status and reimbursement journal.", "ExpenseClaims",
        [new Column("Reference", "Reference", 100), new Column("Employee", "Employee", 180), new Column("ClaimDate", "Date", 95), new Column("Total", "Total", 100, true), new Column("Status", "Status", 90), new Column("Journal", "Journal", 100)],
        []);

    /// <summary>The POS Sales Summary report code.</summary>
    public const string PosSalesCode = "POS-SUM";

    /// <summary>The Job Profitability report code.</summary>
    public const string JobProfitabilityCode = "JOB-PROF";

    /// <summary>The Progress Certificates report code.</summary>
    public const string ProgressCertificatesCode = "JOB-IPC";

    /// <summary>The Job WIP (work in progress) report code.</summary>
    public const string JobWipCode = "JOB-WIP";

    /// <summary>Builds the POS Sales Summary definition (brief §4.1).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition PosSales() => Listing(PosSalesCode, "POS Sales Summary", "PointOfSale",
        "Point-of-sale transactions with net, tax and gross for a period.", "PosSales",
        [new Column("SaleNumber", "Sale", 110), new Column("SaleDate", "Date", 100), new Column("TotalNet", "Net", 100, true), new Column("TotalTax", "Tax", 90, true), new Column("TotalGross", "Gross", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Job Profitability definition (brief §4.12).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition JobProfitability() => Listing(JobProfitabilityCode, "Job Profitability", "JobCosting",
        "Contract value, budget, actual cost, certified-to-date and percentage complete per contract.", "JobProfitability",
        [new Column("ContractNumber", "Contract", 90), new Column("Name", "Name", 160), new Column("ContractValue", "Value", 90, true), new Column("BudgetCost", "Budget", 90, true), new Column("ActualCost", "Actual", 90, true), new Column("CertifiedToDate", "Certified", 90, true), new Column("PercentComplete", "% Complete", 70)],
        []);

    /// <summary>Builds the Progress Certificates definition (brief §4.12).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition ProgressCertificates() => Listing(ProgressCertificatesCode, "Progress Certificates", "JobCosting",
        "Interim payment certificates with gross, retention and net payable for a period.", "ProgressCertificates",
        [new Column("CertificateNumber", "Certificate", 110), new Column("Date", "Date", 100), new Column("Gross", "Gross", 100, true), new Column("Retention", "Retention", 100, true), new Column("Net", "Net payable", 110, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Job WIP definition (IFRS 15 contract asset/liability position).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition JobWip() => Listing(JobWipCode, "Job WIP", "JobCosting",
        "Per contract: revenue recognised on cost-to-cost completion versus certified-to-date, giving the WIP position (contract asset if positive, liability if negative).", "JobWip",
        [new Column("ContractNumber", "Contract", 100), new Column("Name", "Name", 160), new Column("ContractValue", "Contract", 100, true), new Column("PercentComplete", "% complete", 80, true), new Column("RevenueRecognised", "Recognised", 100, true), new Column("CertifiedToDate", "Certified", 100, true), new Column("WipPosition", "WIP", 100, true)],
        []);

    /// <summary>The Housing Stand Register report code.</summary>
    public const string HousingStandsCode = "HSE-STAND";

    /// <summary>The Student Register report code.</summary>
    public const string StudentRegisterCode = "SCH-STU";

    /// <summary>The Open Purchase Orders report code.</summary>
    public const string OpenPurchaseOrdersCode = "POP-OPEN";

    /// <summary>The Purchase Requisitions report code.</summary>
    public const string PurchaseRequisitionsCode = "POP-REQ";

    /// <summary>The Sales Quotations report code.</summary>
    public const string QuotationsCode = "SOP-QUOTE";

    /// <summary>The Sales Back-Orders report code.</summary>
    public const string SalesBackOrdersCode = "SOP-BO";

    /// <summary>The Recurring Invoices report code.</summary>
    public const string RecurringInvoicesCode = "AR-RECUR";

    /// <summary>The Recurring Purchases report code.</summary>
    public const string RecurringPurchasesCode = "AP-RECUR";

    /// <summary>Builds the Housing Stand Register definition (brief §4.10).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition HousingStands() => Listing(HousingStandsCode, "Housing Stand Register", "Housing",
        "Stands with status, apportioned cost, selling price and current holder.", "HousingStands",
        [new Column("StandNumber", "Stand", 90), new Column("Status", "Status", 100), new Column("Holder", "Holder", 150), new Column("AllocatedCost", "Cost", 90, true), new Column("SellingPrice", "Price", 90, true)],
        []);

    /// <summary>Builds the Student Register definition (brief §4.11).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition StudentRegister() => Listing(StudentRegisterCode, "Student Register", "School",
        "Enrolled students with grade, boarding status and bursary.", "StudentRegister",
        [new Column("AdmissionNumber", "Admission", 100), new Column("FullName", "Name", 180), new Column("Grade", "Grade", 80), new Column("Boarder", "Boarder", 70), new Column("BursaryValue", "Bursary", 90, true)],
        []);

    /// <summary>Builds the Open Purchase Orders definition (brief §4.6/§4.12 procurement).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition OpenPurchaseOrders() => Listing(OpenPurchaseOrdersCode, "Open Purchase Orders", "Purchasing",
        "Purchase orders not yet fully received/invoiced, with their order value.", "OpenPurchaseOrders",
        [new Column("OrderNumber", "Order", 100), new Column("OrderDate", "Date", 100), new Column("Supplier", "Supplier", 160), new Column("Status", "Status", 100), new Column("TotalNet", "Value", 100, true)],
        []);

    /// <summary>Builds the Purchase Requisitions definition (brief §4.12).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition PurchaseRequisitions() => Listing(PurchaseRequisitionsCode, "Purchase Requisitions", "Purchasing",
        "Requisitions with supplier, date, estimated value and status, newest first.", "PurchaseRequisitions",
        [new Column("RequisitionNumber", "Requisition", 110), new Column("Supplier", "Supplier", 170), new Column("RequestedDate", "Date", 95), new Column("Total", "Value", 100, true), new Column("Status", "Status", 90)],
        []);

    /// <summary>Builds the Sales Quotations definition (brief §4.13).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Quotations() => Listing(QuotationsCode, "Sales Quotations", "SalesOrders",
        "Quotations with customer, dates, value and status, newest first.", "Quotations",
        [new Column("QuoteNumber", "Quote", 100), new Column("Customer", "Customer", 170), new Column("QuoteDate", "Date", 95), new Column("ValidUntil", "Valid until", 95), new Column("Total", "Value", 100, true), new Column("Status", "Status", 90)],
        []);

    /// <summary>Builds the Recurring Invoices definition (subscription billing).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition RecurringInvoices() => Listing(RecurringInvoicesCode, "Recurring Invoices", "Sales",
        "Standing recurring-invoice templates with amount, tax, interval, next run date and active flag.", "RecurringInvoices",
        [new Column("Reference", "Reference", 100), new Column("Customer", "Customer", 170), new Column("NetAmount", "Net", 90, true), new Column("TaxAmount", "Tax", 80, true), new Column("IntervalMonths", "Every (mo)", 80, true), new Column("NextRunDate", "Next run", 95), new Column("Active", "Active", 60)],
        []);

    /// <summary>Builds the Sales Back-Orders definition (undespatched order lines).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition SalesBackOrders() => Listing(SalesBackOrdersCode, "Sales Back-Orders", "SalesOrders",
        "Order lines not yet fully despatched, with the ordered, despatched and outstanding quantities.", "SalesBackOrders",
        [new Column("OrderNumber", "Order", 100), new Column("Customer", "Customer", 160), new Column("OrderDate", "Date", 95), new Column("Description", "Item", 170), new Column("Ordered", "Ordered", 80, true), new Column("Despatched", "Despatched", 90, true), new Column("Outstanding", "Outstanding", 90, true)],
        []);

    /// <summary>Builds the Recurring Purchases definition (standing payables).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition RecurringPurchases() => Listing(RecurringPurchasesCode, "Recurring Purchases", "Purchasing",
        "Standing recurring supplier-invoice templates with amount, tax, interval, next run date and active flag.", "RecurringPurchases",
        [new Column("Reference", "Reference", 100), new Column("Supplier", "Supplier", 170), new Column("NetAmount", "Net", 90, true), new Column("TaxAmount", "Tax", 80, true), new Column("IntervalMonths", "Every (mo)", 80, true), new Column("NextRunDate", "Next run", 95), new Column("Active", "Active", 60)],
        []);

    /// <summary>The Menu report code.</summary>
    public const string MenuCode = "RES-MENU";

    /// <summary>The Table Status report code.</summary>
    public const string TableStatusCode = "RES-TBL";

    /// <summary>The Kitchen Order Tickets report code.</summary>
    public const string KitchenTicketsCode = "RES-KOT";

    /// <summary>Builds the Menu definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Menu() => Listing(MenuCode, "Menu", "Restaurant",
        "Menu items with category, price, recipe ingredient count and availability.", "Menu",
        [new Column("Code", "Code", 90), new Column("Name", "Name", 160), new Column("Category", "Category", 90), new Column("Ingredients", "Ingredients", 80), new Column("Available", "Available", 70), new Column("Price", "Price", 90, true)],
        []);

    /// <summary>The Tip Allocations report code.</summary>
    public const string TipAllocationsCode = "RES-TIPS";

    /// <summary>The Menu Engineering report code.</summary>
    public const string MenuEngineeringCode = "RES-MIX";

    /// <summary>The Table Reservations report code.</summary>
    public const string ReservationsCode = "RES-RESV";

    /// <summary>The Waitlist report code.</summary>
    public const string WaitlistCode = "RES-WAIT";

    /// <summary>The Combined Tables report code.</summary>
    public const string TableGroupsCode = "RES-COMB";

    /// <summary>The Floor Plan report code.</summary>
    public const string FloorPlanCode = "RES-SECT";

    /// <summary>The Section Sales report code.</summary>
    public const string SectionSalesCode = "RES-SSALES";

    /// <summary>The Voids &amp; Comps report code.</summary>
    public const string VoidsAndCompsCode = "RES-VOID";

    /// <summary>The Sales by Order Type report code.</summary>
    public const string OrderTypeSalesCode = "RES-OTYPE";

    /// <summary>The Active Deliveries report code.</summary>
    public const string DeliveriesCode = "RES-DELIV";

    /// <summary>The Server Sales report code.</summary>
    public const string ServerSalesCode = "RES-SERVER";

    /// <summary>The Category Sales report code.</summary>
    public const string CategorySalesCode = "RES-CAT";

    /// <summary>The Wastage report code.</summary>
    public const string WastageCode = "RES-WASTE";

    /// <summary>The Bills report code.</summary>
    public const string BillsCode = "RES-BILLS";

    /// <summary>The Menu Cost Card report code.</summary>
    public const string MenuCostCode = "RES-COST";

    /// <summary>Builds the Kitchen Order Tickets definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition KitchenTickets() => Listing(KitchenTicketsCode, "Kitchen Order Tickets", "Restaurant",
        "Open kitchen tickets (not yet served) with their status and item count.", "KitchenTickets",
        [new Column("Ticket", "Ticket", 100), new Column("Status", "Status", 110), new Column("Items", "Items", 70), new Column("Sent", "Sent", 100)],
        []);

    /// <summary>Builds the Tip Allocations definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition TipAllocations() => Listing(TipAllocationsCode, "Tip Allocations", "Restaurant",
        "Pooled tips allocated to staff, with amount and payment status.", "TipAllocations",
        [new Column("Staff", "Staff", 120), new Column("Date", "Date", 100), new Column("Status", "Status", 90), new Column("Amount", "Amount", 100, true)],
        []);

    /// <summary>Builds the Menu Engineering definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition MenuEngineering() => Listing(MenuEngineeringCode, "Menu Engineering", "Restaurant",
        "Sales mix by menu item - quantity, revenue, recipe cost, margin and menu-engineering class.", "MenuEngineering",
        [new Column("Item", "Item", 150), new Column("QtySold", "Qty", 70), new Column("Revenue", "Revenue", 90, true), new Column("Cost", "Cost", 90, true), new Column("Margin", "Margin", 90, true), new Column("Class", "Class", 90)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Table Reservations definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Reservations() => Listing(ReservationsCode, "Table Reservations", "Restaurant",
        "Upcoming table bookings - when, table, guest, party size and contact.", "Reservations",
        [new Column("When", "When", 130), new Column("Table", "Table", 70), new Column("Guest", "Guest", 130), new Column("Party", "Party", 60), new Column("Phone", "Phone", 100)],
        []);

    /// <summary>Builds the Waitlist definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Waitlist() => Listing(WaitlistCode, "Waitlist", "Restaurant",
        "Walk-in parties currently queued for a table, in the order they joined.", "Waitlist",
        [new Column("Added", "Added", 130), new Column("Guest", "Guest", 130), new Column("Party", "Party", 60), new Column("Quoted", "Quoted (min)", 90), new Column("Phone", "Phone", 100)],
        []);

    /// <summary>Builds the Combined Tables definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition TableGroups() => Listing(TableGroupsCode, "Combined Tables", "Restaurant",
        "Active table groups - the combined tables, their primary and total seats.", "TableGroups",
        [new Column("Group", "Group", 130), new Column("Primary", "Primary", 80), new Column("Tables", "Tables", 70), new Column("Seats", "Seats", 70)],
        []);

    /// <summary>Builds the Floor Plan definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition FloorPlan() => Listing(FloorPlanCode, "Floor Plan", "Restaurant",
        "Floor-plan sections with their tables, seats, live occupancy and assigned server.", "FloorPlan",
        [new Column("Section", "Section", 140), new Column("Tables", "Tables", 70), new Column("Seats", "Seats", 70), new Column("Occupied", "Occupied", 80), new Column("Server", "Server", 120), new Column("Active", "Active", 70)],
        []);

    /// <summary>Builds the Section Sales definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition SectionSales() => Listing(SectionSalesCode, "Section Sales", "Restaurant",
        "Billed sales by floor-plan section over a period - orders, net, tax and gross.", "SectionSales",
        [new Column("Section", "Section", 140), new Column("Orders", "Orders", 70), new Column("Net", "Net", 100, true), new Column("Tax", "Tax", 90, true), new Column("Gross", "Gross", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Voids &amp; Comps definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition VoidsAndComps() => Listing(VoidsAndCompsCode, "Voids &amp; Comps", "Restaurant",
        "Voided and comped order lines over a period, with reasons and the authorising manager - for oversight.", "VoidsAndComps",
        [new Column("Date", "Date", 100), new Column("Order", "Order", 90), new Column("Item", "Item", 130), new Column("Qty", "Qty", 55), new Column("Type", "Type", 60), new Column("Reason", "Reason", 160), new Column("Authorised", "Authorised", 120)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Sales by Order Type definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition OrderTypeSales() => Listing(OrderTypeSalesCode, "Sales by Order Type", "Restaurant",
        "Billed sales split by fulfilment type (dine-in / takeaway / delivery) over a period.", "OrderTypeSales",
        [new Column("Type", "Type", 120), new Column("Orders", "Orders", 70), new Column("Net", "Net", 100, true), new Column("Tax", "Tax", 90, true), new Column("Gross", "Gross", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Active Deliveries definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Deliveries() => Listing(DeliveriesCode, "Active Deliveries", "Restaurant",
        "Delivery orders still in progress - customer, address, driver and dispatch state.", "Deliveries",
        [new Column("Order", "Order", 100), new Column("Customer", "Customer", 120), new Column("Address", "Address", 180), new Column("Driver", "Driver", 110), new Column("Status", "Status", 110)],
        []);

    /// <summary>Builds the Server Sales definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition ServerSales() => Listing(ServerSalesCode, "Server Sales", "Restaurant",
        "Billed sales by waiter over a period - orders, net, tax and gross (for tip-out and performance).", "ServerSales",
        [new Column("Server", "Server", 140), new Column("Orders", "Orders", 70), new Column("Net", "Net", 100, true), new Column("Tax", "Tax", 90, true), new Column("Gross", "Gross", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Category Sales definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition CategorySales() => Listing(CategorySalesCode, "Category Sales", "Restaurant",
        "Billed sales by menu category (starters, mains, drinks…) over a period.", "CategorySales",
        [new Column("Category", "Category", 150), new Column("Qty", "Qty", 70), new Column("Net", "Net", 100, true), new Column("Tax", "Tax", 90, true), new Column("Gross", "Gross", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Wastage definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Wastage() => Listing(WastageCode, "Stock Wastage", "Restaurant",
        "Spoilage / waste of stock ingredients over a period, with reasons and value written off.", "Wastage",
        [new Column("Date", "Date", 100), new Column("Item", "Item", 130), new Column("Qty", "Qty", 70), new Column("Value", "Value", 100, true), new Column("Reason", "Reason", 200)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Bills definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition Bills() => Listing(BillsCode, "Bills", "Restaurant",
        "Every restaurant bill over a period - net, tax, service charge, tip, discount and gross.", "Bills",
        [new Column("Date", "Date", 95), new Column("Order", "Order", 90), new Column("Net", "Net", 90, true), new Column("Tax", "Tax", 80, true), new Column("Service", "Service", 80, true), new Column("Tip", "Tip", 70, true), new Column("Discount", "Discount", 85, true), new Column("Gross", "Gross", 95, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Menu Cost Card definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition MenuCost() => Listing(MenuCostCode, "Menu Cost Card", "Restaurant",
        "Every menu item's price, recipe cost (from ingredient average cost), gross margin and food-cost %.", "MenuCost",
        [new Column("Code", "Code", 90), new Column("Name", "Name", 150), new Column("Price", "Price", 90, true), new Column("Cost", "Cost", 90, true), new Column("Margin", "Margin", 90, true), new Column("FoodCostPct", "Food %", 80, true)],
        []);

    /// <summary>Builds the Table Status definition (Restaurant module).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition TableStatus() => Listing(TableStatusCode, "Table Status", "Restaurant",
        "Tables with seats and current status.", "TableStatus",
        [new Column("TableNumber", "Table", 100), new Column("Seats", "Seats", 80), new Column("Status", "Status", 120)],
        []);

    /// <summary>Builds a generic listing report definition from a column spec.</summary>
    /// <param name="code">The report code.</param>
    /// <param name="name">The report name.</param>
    /// <param name="module">The owning module.</param>
    /// <param name="description">The description.</param>
    /// <param name="command">The data-source command.</param>
    /// <param name="columns">The columns.</param>
    /// <param name="parameters">The parameters (name + type).</param>
    /// <returns>The definition.</returns>
    public static ReportDefinition Listing(string code, string name, string module, string description, string command, Column[] columns, (string Name, ParameterType Type)[] parameters)
    {
        ReportDefinition def = new() { Code = code, Name = name, Module = module, Description = description };
        def.DataSources.Add(new ReportDataSource { Name = "Main", Kind = DataSourceKind.InMemory, Command = command });
        foreach ((string paramName, ParameterType type) in parameters)
        {
            def.Parameters.Add(new ReportParameter { Name = paramName, Type = type, Prompt = paramName, Required = false });
        }

        // Sales, purchasing, cash book, tax and the finance modules' listings all take the common parameters (brief §9): a reporting currency and rate basis, a branch,
        // cost centre and department to narrow to, and whether unposted journals count; the header then names the rate basis the runner used.
        bool common = CommonParameterModules.Contains(module, StringComparer.Ordinal);
        if (common)
        {
            foreach ((string paramName, ParameterType type) in CommonParameters.Where(c => def.Parameters.All(p => !string.Equals(p.Name, c.Name, StringComparison.OrdinalIgnoreCase))))
            {
                def.Parameters.Add(new ReportParameter { Name = paramName, Type = type, Prompt = paramName, Required = false });
            }
        }

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportHeader,
            Height = common ? 46 : 40,
            Elements = common
                ?
                [
                    new ReportElement { Kind = ElementKind.Expression, Name = "company", Expression = "CompanyName", X = 0, Y = 0, Width = 400, Height = 16, Bold = true, FontSize = 13 },
                    Label("title", name, 0, 18, 500, 14, bold: true, size: 11),
                    new ReportElement { Kind = ElementKind.RichText, Name = "basis", Text = "{" + RateBasisUsedParameter + "}", X = 0, Y = 33, Width = 600, Height = 11, FontSize = 8 },
                ]
                :
                [
                    new ReportElement { Kind = ElementKind.Expression, Name = "company", Expression = "CompanyName", X = 0, Y = 0, Width = 400, Height = 16, Bold = true, FontSize = 13 },
                    Label("title", name, 0, 18, 500, 14, bold: true, size: 11),
                ],
        });

        Column? lastMoney = columns.LastOrDefault(c => c.Money) is { Field.Length: > 0 } lm ? lm : null;
        List<ReportElement> headers = [];
        List<ReportElement> fields = [];
        List<ReportElement> footer = [];
        double x = 0;
        foreach (Column col in columns)
        {
            ReportHAlign align = col.Money ? ReportHAlign.Right : ReportHAlign.Left;
            headers.Add(Label($"h_{col.Field}", col.Header, x, 2, col.Width, 12, bold: true, align: align));
            fields.Add(Field($"d_{col.Field}", col.Field, x, 0, col.Width, 12, format: col.Money ? "N2" : null, align: align));
            if (lastMoney is { } money && money.Field == col.Field)
            {
                footer.Add(Aggregate($"t_{col.Field}", col.Field, x, 4, col.Width, 14, bold: true, align: ReportHAlign.Right));
            }

            x += col.Width + 6;
        }

        def.Bands.Add(new ReportBand { Type = BandType.PageHeader, Height = 18, Elements = headers });
        def.Bands.Add(new ReportBand { Type = BandType.Detail, Height = 14, Elements = fields });
        if (footer.Count > 0)
        {
            footer.Insert(0, Label("t_lbl", "Total", 0, 4, 80, 14, bold: true));
            def.Bands.Add(new ReportBand { Type = BandType.ReportFooter, Height = 20, Elements = footer });
        }

        return def;
    }

    /// <summary>
    /// Reconstructs the <see cref="Column"/> list from a listing definition previously built by
    /// <see cref="Listing"/> - pairing each <c>d_*</c> detail field with its <c>h_*</c> page-header label -
    /// so a designer can round-trip a listing report (extract → edit → rebuild via <see cref="Listing"/>).
    /// Returns an empty array for a definition that is not in the listing shape.
    /// </summary>
    /// <param name="definition">The report definition.</param>
    /// <returns>The reconstructed columns, in render order.</returns>
    public static Column[] ExtractListingColumns(ReportDefinition definition)
    {
        ReportBand? detail = definition.BandOfType(BandType.Detail);
        ReportBand? pageHeader = definition.BandOfType(BandType.PageHeader);
        if (detail is null)
        {
            return [];
        }

        Dictionary<string, string> headers = pageHeader?.Elements
            .Where(e => e.Kind == ElementKind.Label && e.Name.StartsWith("h_", StringComparison.Ordinal))
            .ToDictionary(e => e.Name["h_".Length..], e => e.Text ?? string.Empty) ?? [];

        return detail.Elements
            .Where(e => e.Kind == ElementKind.Field && e.Name.StartsWith("d_", StringComparison.Ordinal) && !string.IsNullOrEmpty(e.FieldName))
            .OrderBy(e => e.X)
            .Select(e =>
            {
                string field = e.FieldName!;
                string header = headers.TryGetValue(field, out string? h) && !string.IsNullOrEmpty(h) ? h : field;
                bool money = string.Equals(e.Format, "N2", StringComparison.OrdinalIgnoreCase);
                return new Column(field, header, e.Width, money);
            })
            .ToArray();
    }

    /// <summary>The data-source command backing a definition (the key the report data provider handles).</summary>
    /// <param name="definition">The report definition.</param>
    /// <returns>The command, or the report code when no data source is defined.</returns>
    public static string CommandOf(ReportDefinition definition) =>
        definition.DataSources.FirstOrDefault()?.Command ?? definition.Code;

    /// <summary>The modules whose listings take the common parameters (mirrored by the application layer's ReportCommonParameters, which this project cannot reference).</summary>
    public static readonly string[] CommonParameterModules = ["Sales", "Purchasing", "CashBook", "Tax", "GeneralLedger", "Receivables", "SalesOrders", "FixedAssets", "Budget", "JobCosting"];

    /// <summary>The parameter the report runner adds to say which rate it translated amounts at; the listing header shows it.</summary>
    public const string RateBasisUsedParameter = "RateBasisUsed";

    /// <summary>The common parameters of the listings of <see cref="CommonParameterModules"/>, in the order they are prompted.</summary>
    public static readonly (string Name, ParameterType Type)[] CommonParameters =
    [
        ("ReportingCurrency", ParameterType.String), ("RateBasis", ParameterType.String), ("Rate", ParameterType.Decimal), ("Branch", ParameterType.String), ("CostCentre", ParameterType.String),
        ("Department", ParameterType.String), ("IncludeUnposted", ParameterType.Boolean),
    ];

    private static ReportElement Label(string name, string text, double x, double y, double w, double h, bool bold = false, double size = 9, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Label, Name = name, Text = text, X = x, Y = y, Width = w, Height = h, Bold = bold, FontSize = size, HAlign = align };

    private static ReportElement Field(string name, string field, double x, double y, double w, double h, string? format = null, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Field, Name = name, FieldName = field, X = x, Y = y, Width = w, Height = h, Format = format, HAlign = align };

    private static ReportElement Aggregate(string name, string field, double x, double y, double w, double h, bool bold = false, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Aggregate, Name = name, Aggregate = AggregateFunction.Sum, AggregateField = field, X = x, Y = y, Width = w, Height = h, Format = "N2", Bold = bold, HAlign = align };
}
