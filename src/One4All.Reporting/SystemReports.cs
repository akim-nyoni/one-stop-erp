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

    /// <summary>The Aged Debtors report code.</summary>
    public const string AgedDebtorsCode = "AR-AGE";

    /// <summary>The Aged Creditors report code.</summary>
    public const string AgedCreditorsCode = "AP-AGE";

    /// <summary>A column in a generic listing report.</summary>
    /// <param name="Field">The bound data field.</param>
    /// <param name="Header">The column header.</param>
    /// <param name="Width">The width in points.</param>
    /// <param name="Money">Whether the column is a money amount (right-aligned, N2, totalled).</param>
    public readonly record struct Column(string Field, string Header, double Width, bool Money = false);

    /// <summary>Builds the Income Statement definition (brief §14 report 7).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition IncomeStatement() => Listing(IncomeStatementCode, "Income Statement", "GeneralLedger",
        "Income and expenses for a period; the difference is the net profit.", "IncomeStatement",
        [new Column("Category", "Section", 120), new Column("AccountName", "Account", 250), new Column("Amount", "Amount", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

    /// <summary>Builds the Balance Sheet definition (brief §14 report 6).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition BalanceSheet() => Listing(BalanceSheetCode, "Balance Sheet", "GeneralLedger",
        "Assets, liabilities and equity as at a date; assets equal liabilities plus equity.", "BalanceSheet",
        [new Column("Category", "Section", 120), new Column("AccountName", "Account", 250), new Column("Amount", "Amount", 100, true)],
        [("AsAtDate", ParameterType.Date)]);

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

    /// <summary>The Stock Valuation report code.</summary>
    public const string StockValuationCode = "INV-VAL";

    /// <summary>The Fixed Asset Register report code.</summary>
    public const string AssetRegisterCode = "FA-REG";

    /// <summary>The Payroll Summary report code.</summary>
    public const string PayrollSummaryCode = "PAY-SUM";

    /// <summary>The Property Register report code.</summary>
    public const string PropertyRegisterCode = "MUN-PROP";

    /// <summary>The Budget vs Actual report code.</summary>
    public const string BudgetVsActualCode = "BUD-BVA";

    /// <summary>Builds the Stock Valuation definition (brief §4.11 inventory).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition StockValuation() => Listing(StockValuationCode, "Stock Valuation", "Inventory",
        "Quantity on hand and value at moving average cost, per item and warehouse.", "StockValuation",
        [new Column("ItemCode", "Item", 90), new Column("Description", "Description", 180), new Column("WarehouseCode", "Warehouse", 90), new Column("Quantity", "Qty", 70, true), new Column("AverageCost", "Avg cost", 80, true), new Column("Value", "Value", 90, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Fixed Asset Register definition (brief §4.14).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition AssetRegister() => Listing(AssetRegisterCode, "Fixed Asset Register", "FixedAssets",
        "Assets with cost, accumulated depreciation and net book value.", "AssetRegister",
        [new Column("AssetNumber", "Asset", 90), new Column("Description", "Description", 200), new Column("Cost", "Cost", 90, true), new Column("AccumulatedDepreciation", "Accum. depr.", 90, true), new Column("NetBookValue", "NBV", 90, true)],
        [("AsAtDate", ParameterType.Date)]);

    /// <summary>Builds the Payroll Summary definition (brief §4.15.6).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition PayrollSummary() => Listing(PayrollSummaryCode, "Payroll Summary", "Payroll",
        "Gross, deductions and net pay per employee for a period.", "PayrollSummary",
        [new Column("Employee", "Employee", 220), new Column("Gross", "Gross", 100, true), new Column("Deductions", "Deductions", 100, true), new Column("Net", "Net", 100, true)],
        [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);

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

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportHeader,
            Height = 40,
            Elements =
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

    private static ReportElement Label(string name, string text, double x, double y, double w, double h, bool bold = false, double size = 9, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Label, Name = name, Text = text, X = x, Y = y, Width = w, Height = h, Bold = bold, FontSize = size, HAlign = align };

    private static ReportElement Field(string name, string field, double x, double y, double w, double h, string? format = null, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Field, Name = name, FieldName = field, X = x, Y = y, Width = w, Height = h, Format = format, HAlign = align };

    private static ReportElement Aggregate(string name, string field, double x, double y, double w, double h, bool bold = false, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Aggregate, Name = name, Aggregate = AggregateFunction.Sum, AggregateField = field, X = x, Y = y, Width = w, Height = h, Format = "N2", Bold = bold, HAlign = align };
}
