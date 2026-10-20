using One4All.Reporting.Model;

namespace One4All.Reporting;

/// <summary>
/// The shipped Cashiering &amp; Receipting report catalogue (brief §RCP-16, the <c>RC</c> series), seeded as
/// system report definitions into <c>rpt.report_definition</c>. Each is a banded definition built from a
/// compact column spec - read-only; a customer copies one to a company/user version to customise it. The
/// live data providers that feed these commands are wired separately.
/// </summary>
public static class ReceiptingReports
{
    private const string Module = "Receipting";

    /// <summary>A column in a listing report.</summary>
    /// <param name="Field">The data field name.</param>
    /// <param name="Header">The column header.</param>
    /// <param name="Width">The column width in points.</param>
    /// <param name="Money">Whether the column is a money amount (right-aligned, N2, summed in the footer).</param>
    private readonly record struct Col(string Field, string Header, double Width, bool Money = false);

    /// <summary>Enumerates every RC-series report definition (RC1–RC53).</summary>
    /// <returns>The report definitions.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        // Daily operations
        yield return Doc("RC1", "Cashier Cash-Up / Session Close", "Expected vs counted per currency and denomination, variance and supervisor sign-off.", ["Session", "Currency"]);
        yield return Doc("RC2", "X-Read / Z-Read", "Mid-shift non-resetting (X) and session-close immutable (Z) totals by tender and currency.", ["Session"]);
        yield return Listing("RC3", "Daily Collection Summary", "Collections by station, cashier, tender type, currency and revenue head.",
            [new Col("Category", "Category", 220), new Col("Count", "Count", 70), new Col("Amount", "Amount", 100, true)], ["Date"]);
        yield return Listing("RC4", "Receipt Listing / Cashier Day Book", "Every receipt with payer, amount, tender and allocation.",
            [new Col("ReceiptNumber", "Receipt", 90), new Col("Payer", "Payer", 180), new Col("Tender", "Tender", 90), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC5", "Collection by Revenue Vote", "Sundry income analysed to the GL vote.",
            [new Col("AccountCode", "Vote", 80), new Col("AccountName", "Description", 230), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC6", "Collection by Service", "What residents paid for - water, rates, refuse, sewer, licences, fees.",
            [new Col("Service", "Service", 220), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC7", "Collection by Tender Type & Channel", "Counter cash, card, mobile money, bank, agent, field.",
            [new Col("Tender", "Tender", 160), new Col("Channel", "Channel", 120), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC8", "Collection by Currency", "Amounts by currency with the rates applied and the realised FX effect.",
            [new Col("Currency", "Currency", 80), new Col("Transaction", "Transaction", 100, true), new Col("Rate", "Rate", 70), new Col("BaseAmount", "Base", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC9", "Collection by Ward / Zone", "Collections by ward, zone, suburb, class or scheme.",
            [new Col("Area", "Area", 220), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC10", "Hourly Transaction Volume & Queue Analysis", "Transactions per hour per station, average handling time, peak periods.",
            [new Col("Hour", "Hour", 80), new Col("Station", "Station", 120), new Col("Transactions", "Txns", 80), new Col("AvgSeconds", "Avg (s)", 80)], ["Date"]);
        yield return Listing("RC11", "Cashier Productivity", "Receipts processed, value, average handling time, variances, cancellations, reprints.",
            [new Col("Cashier", "Cashier", 160), new Col("Receipts", "Receipts", 80), new Col("Value", "Value", 100, true), new Col("Variances", "Variances", 80)], ["FromDate", "ToDate"]);

        // Reconciliation and control
        yield return Listing("RC12", "Receipting-to-GL Reconciliation", "Total collected = debtor control movement + sundry income + deposit movement + suspense movement.",
            [new Col("Component", "Component", 260), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC13", "Cash Control Account Reconciliation", "Till → safe → cash in transit → bank; supply the account codes for each stage.",
            [new Col("Stage", "Stage", 200), new Col("Debit", "Debit", 100, true), new Col("Credit", "Credit", 100, true)], ["CashAccount", "SafeAccount", "TransitAccount", "BankAccount"]);
        yield return Listing("RC14", "Till Variance Report", "Shortages and overages by cashier and period, with recovery status.",
            [new Col("Cashier", "Cashier", 150), new Col("Session", "Session", 90), new Col("Variance", "Variance", 100, true), new Col("Status", "Recovery", 90)], ["FromDate", "ToDate"]);
        yield return Listing("RC15", "Cancelled & Reversed Receipts", "With reason, authorising supervisor and value.",
            [new Col("ReceiptNumber", "Receipt", 90), new Col("Reason", "Reason", 180), new Col("Supervisor", "By", 100), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC16", "Receipt Reprint Log", "Every reprint, watermarked COPY, numbered and logged.",
            [new Col("ReceiptNumber", "Receipt", 90), new Col("PrintedOn", "Printed", 110), new Col("PrintedBy", "By", 150)], ["FromDate", "ToDate"]);
        yield return Listing("RC17", "Receipt Number Gap Analysis", "Per station - must show no gaps.",
            [new Col("Station", "Station", 120), new Col("FromNumber", "From", 110), new Col("ToNumber", "To", 110), new Col("Gaps", "Gaps", 80)], ["Station"]);
        yield return Listing("RC18", "Back-Dated Receipt Report", "Receipts posted with a date before the transaction date.",
            [new Col("ReceiptNumber", "Receipt", 90), new Col("ReceiptDate", "Receipt date", 100), new Col("CapturedOn", "Captured", 100), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC19", "Allocation Override Report", "Where a cashier directed money against the default policy, with the reason.",
            [new Col("ReceiptNumber", "Receipt", 90), new Col("Account", "Account", 150), new Col("Reason", "Reason", 160), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC20", "Manual / Pre-Numbered Stationery Control", "Books issued, serials used, serials unaccounted, books unreturned.",
            [new Col("BookNumber", "Book", 90), new Col("Range", "Serials", 120), new Col("Used", "Used", 70), new Col("Unaccounted", "Gaps", 80)], ["AsAtDate"]);
        yield return Listing("RC21", "Receipting Audit Trail", "Every receipt, cancellation, allocation, suspense movement and refund - before/after values, user, station.",
            [new Col("When", "When", 120), new Col("Action", "Action", 120), new Col("User", "User", 110), new Col("Detail", "Detail", 160)], ["FromDate", "ToDate"]);
        yield return Listing("RC22", "Segregation of Duties Violations - Receipting", "Blocked or granted SoD-conflicting actions.",
            [new Col("User", "User", 150), new Col("Conflict", "Conflict", 220), new Col("When", "When", 110)], ["FromDate", "ToDate"]);
        yield return Listing("RC23", "Receipts Against Related-Party Accounts", "Blocked attempts and any granted exceptions.",
            [new Col("Cashier", "Cashier", 150), new Col("Account", "Account", 150), new Col("Outcome", "Outcome", 100), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);

        // Banking and treasury
        yield return Doc("RC24", "Banking / Deposit Slip", "Per bank account and currency.", ["Deposit"]);
        yield return Listing("RC25", "Daily Banking Summary", "Collected vs handed to treasury vs banked vs confirmed on statement.",
            [new Col("Stage", "Stage", 220), new Col("Amount", "Amount", 100, true)], ["Date"]);
        yield return Listing("RC26", "Cash in Transit Ageing", "Anything unbanked or unconfirmed beyond N days.",
            [new Col("Reference", "Deposit", 120), new Col("DepositDate", "Deposited", 100), new Col("Days", "Days", 60), new Col("Amount", "Amount", 100, true)], ["AsAtDate"]);
        yield return Listing("RC27", "Unbanked Collections Report", "Takings not yet moved into cash-in-transit.",
            [new Col("Station", "Station", 140), new Col("Session", "Session", 90), new Col("Amount", "Amount", 100, true)], ["AsAtDate"]);
        yield return Listing("RC28", "Pickups, Drops & Petty Cash Pay-outs", "Mid-shift cash movements with reason.",
            [new Col("When", "When", 120), new Col("Type", "Type", 110), new Col("Reason", "Reason", 160), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);

        // Suspense, imports and pending
        yield return Listing("RC29", "Unallocated / Suspense Receipts Register", "With ageing - the report that recovers lost money.",
            [new Col("Reference", "Reference", 130), new Col("PayerName", "Payer", 150), new Col("Days", "Days", 60), new Col("Amount", "Amount", 100, true)], ["AsAtDate"]);
        yield return Listing("RC30", "Suspense Allocation Report", "What was allocated out, by whom, to which account, with the reason.",
            [new Col("When", "When", 110), new Col("Account", "Account", 150), new Col("Reason", "Reason", 150), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC31", "Bulk Import Batch Report", "File, records, matched, unmatched, duplicates rejected, total value.",
            [new Col("FileReference", "File", 180), new Col("Matched", "Matched", 70), new Col("Unmatched", "Unmatched", 80), new Col("TotalAmount", "Value", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC32", "Import Matching Exceptions", "Unmatched references, ambiguous matches, amount mismatches.",
            [new Col("PayerReference", "Reference", 160), new Col("PayerName", "Payer", 150), new Col("Reason", "Reason", 120), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC33", "Pending Tenders Report", "Claimed deposits and transfers not yet confirmed on the bank statement, aged.",
            [new Col("Reference", "Reference", 140), new Col("Tender", "Tender", 110), new Col("Days", "Days", 60), new Col("Amount", "Amount", 100, true)], ["AsAtDate"]);
        yield return Listing("RC34", "Dishonoured Cheque / Reversed Payment Register", "With penalties raised and recovery status.",
            [new Col("Reference", "Cheque", 120), new Col("Drawer", "Drawer", 150), new Col("Penalty", "Penalty", 90, true), new Col("Amount", "Amount", 90, true)], ["FromDate", "ToDate"]);

        // Payer-facing and account-level
        yield return Doc("RC35", "Official Receipt", "Thermal / A5 / A4 / multi-part - allocation by service, balance after payment, currency and rate.", ["Receipt"]);
        yield return Doc("RC36", "Counter Mini-Statement", "Last N transactions and the current balance - printed on request.", ["Account"]);
        yield return Doc("RC37", "Full Account Statement", "The AR / municipal / housing / school statement, printable from the counter.", ["Account"]);
        yield return Listing("RC38", "Payment History per Account", "All receipts, allocations and reversals for one payer.",
            [new Col("Date", "Date", 90), new Col("ReceiptNumber", "Receipt", 100), new Col("Type", "Type", 110), new Col("Amount", "Amount", 100, true)], ["Account", "FromDate", "ToDate"]);
        yield return Listing("RC39", "Credit Balances / Overpayments", "Residents in credit, with ageing - a liability that must be reported.",
            [new Col("Account", "Account", 150), new Col("Name", "Name", 160), new Col("Days", "Days", 60), new Col("Credit", "Credit", 100, true)], ["AsAtDate"]);
        yield return Listing("RC40", "Deposits Held Register", "By payer, type, date received, refundable amount.",
            [new Col("Payer", "Payer", 170), new Col("Type", "Type", 120), new Col("ReceivedOn", "Received", 100), new Col("Amount", "Amount", 100, true)], ["AsAtDate"]);
        yield return Listing("RC41", "Refunds Register", "Requested, approved, paid, outstanding - with turnaround times.",
            [new Col("Payer", "Payer", 170), new Col("Status", "Status", 100), new Col("Days", "Days", 60), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC42", "Payment Arrangements Captured at Counter", "New arrangements, honoured, defaulted.",
            [new Col("Account", "Account", 150), new Col("StartDate", "Start", 100), new Col("Status", "Status", 100), new Col("Instalment", "Instalment", 90, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC43", "Top Payers / Largest Receipts", "For review, and for spotting structuring.",
            [new Col("Payer", "Payer", 200), new Col("Count", "Receipts", 80), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC44", "Accounts With No Payment in N Months", "Feeds credit control and disconnection lists.",
            [new Col("Account", "Account", 150), new Col("Name", "Name", 170), new Col("LastPayment", "Last paid", 100), new Col("Balance", "Balance", 100, true)], ["AsAtDate"]);

        // Agents and field
        yield return Listing("RC45", "Agent Collection Reconciliation", "Collected, remitted, outstanding, commission due, per agent.",
            [new Col("Agent", "Agent", 160), new Col("Collected", "Collected", 90, true), new Col("Remitted", "Remitted", 90, true), new Col("Outstanding", "Outstanding", 90, true)], ["AsAtDate"]);
        yield return Listing("RC46", "Agent Remittance Ageing", "Money held by agents and not remitted.",
            [new Col("Agent", "Agent", 180), new Col("Days", "Days", 70), new Col("Outstanding", "Outstanding", 100, true)], ["AsAtDate"]);
        yield return Listing("RC47", "Field Collector Daily Reconciliation", "Receipts issued, value, cash returned, variance, per collector.",
            [new Col("Collector", "Collector", 160), new Col("Receipts", "Receipts", 80), new Col("Value", "Value", 90, true), new Col("Variance", "Variance", 90, true)], ["Date"]);
        yield return Listing("RC48", "Field Collection Route Performance", "Collections by route.",
            [new Col("Route", "Route", 200), new Col("Collections", "Collections", 90), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);

        // Management and revenue
        yield return Listing("RC49", "Collection Efficiency", "Billed vs collected, by service, ward and month.",
            [new Col("Service", "Service", 180), new Col("Billed", "Billed", 90, true), new Col("Collected", "Collected", 90, true), new Col("Percent", "%", 60)], ["FromDate", "ToDate"]);
        yield return Listing("RC50", "Collection vs Revenue Budget", "Against the §4.18 revenue budget, by vote and department.",
            [new Col("Vote", "Vote", 90), new Col("Budget", "Budget", 90, true), new Col("Actual", "Actual", 90, true), new Col("Variance", "Variance", 90, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC51", "Collection Trend & Seasonality", "Daily, weekly, monthly, year-on-year.",
            [new Col("Period", "Period", 160), new Col("Amount", "Amount", 100, true)], ["FromDate", "ToDate"]);
        yield return Listing("RC52", "Prepaid Token Sales & Credit-off-Arrears Recovery", "Token value issued and the arrears portion recovered.",
            [new Col("Date", "Date", 100), new Col("TokenValue", "Token", 100, true), new Col("ArrearsRecovered", "Arrears", 100, true)], ["FromDate", "ToDate"]);
        yield return Doc("RC53", "Receipting Executive Dashboard", "Today's collections by channel and service, cash unbanked, suspense pool and its age, variance trend, queue load.", ["Date"]);
    }

    private static ReportDefinition Listing(string code, string name, string description, Col[] columns, string[] dateParameters)
    {
        ReportDefinition def = Base(code, name, description, dateParameters);

        double titleY = 20 + (dateParameters.Length > 0 ? 14 : 0);
        List<ReportElement> headerLabels = [];
        List<ReportElement> detailFields = [];
        List<ReportElement> footer = [];
        double x = 0;
        Col? lastMoney = columns.LastOrDefault(c => c.Money) is { Field.Length: > 0 } lm ? lm : null;
        foreach (Col col in columns)
        {
            ReportHAlign align = col.Money ? ReportHAlign.Right : ReportHAlign.Left;
            headerLabels.Add(Label($"h_{col.Field}", col.Header, x, 2, col.Width, 12, bold: true, align: align));
            detailFields.Add(Field($"d_{col.Field}", col.Field, x, 0, col.Width, 12, format: col.Money ? "N2" : null, align: align));
            if (lastMoney is { } money && money.Field == col.Field)
            {
                footer.Add(Aggregate($"t_{col.Field}", col.Field, x, 4, col.Width, 14, bold: true, align: ReportHAlign.Right));
            }

            x += col.Width + 6;
        }

        def.Bands.Add(new ReportBand { Type = BandType.PageHeader, Height = 18, Elements = headerLabels });
        def.Bands.Add(new ReportBand { Type = BandType.Detail, Height = 14, Elements = detailFields });
        if (footer.Count > 0)
        {
            footer.Insert(0, Label("t_lbl", "Total", 0, 4, 90, 14, bold: true));
            def.Bands.Add(new ReportBand { Type = BandType.ReportFooter, Height = 20, Elements = footer });
        }

        return def;
    }

    private static ReportDefinition Doc(string code, string name, string description, string[] parameters)
    {
        ReportDefinition def = Base(code, name, description, parameters);
        def.Bands.Add(new ReportBand
        {
            Type = BandType.Detail,
            Height = 16,
            Elements = [new ReportElement { Kind = ElementKind.Label, Name = "body", Text = description, X = 0, Y = 0, Width = 500, Height = 14, FontSize = 9 }],
        });
        return def;
    }

    private static ReportDefinition Base(string code, string name, string description, string[] parameters)
    {
        ReportDefinition def = new()
        {
            Code = code,
            Name = name,
            Module = Module,
            Description = description,
        };
        def.DataSources.Add(new ReportDataSource { Name = "Main", Kind = DataSourceKind.InMemory, Command = code });

        List<ReportElement> header =
        [
            new ReportElement { Kind = ElementKind.Expression, Name = "company", Expression = "CompanyName", X = 0, Y = 0, Width = 400, Height = 16, Bold = true, FontSize = 13 },
            Label("title", name, 0, 18, 500, 14, bold: true, size: 11),
        ];

        double py = 34;
        foreach (string parameter in parameters)
        {
            ParameterType type = parameter.Contains("Date", StringComparison.Ordinal) ? ParameterType.Date : ParameterType.String;
            def.Parameters.Add(new ReportParameter { Name = parameter, Type = type, Prompt = SystemReports.FriendlyPrompt(parameter), Required = false });
        }

        if (parameters.Length > 0)
        {
            string expr = string.Join(" + \"  \" + ", parameters.Select(p => $"\"{p}: \" + Param(\"{p}\")"));
            header.Add(new ReportElement { Kind = ElementKind.Expression, Name = "params", Expression = expr, X = 0, Y = py, Width = 500, Height = 12, FontSize = 8 });
        }

        def.Bands.Add(new ReportBand { Type = BandType.ReportHeader, Height = 50, Elements = header });
        return def;
    }

    private static ReportElement Label(string name, string text, double x, double y, double w, double h, bool bold = false, double size = 9, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Label, Name = name, Text = text, X = x, Y = y, Width = w, Height = h, Bold = bold, FontSize = size, HAlign = align };

    private static ReportElement Field(string name, string field, double x, double y, double w, double h, string? format = null, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Field, Name = name, FieldName = field, X = x, Y = y, Width = w, Height = h, Format = format, HAlign = align };

    private static ReportElement Aggregate(string name, string field, double x, double y, double w, double h, bool bold = false, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Aggregate, Name = name, Aggregate = AggregateFunction.Sum, AggregateField = field, X = x, Y = y, Width = w, Height = h, Format = "N2", Bold = bold, HAlign = align };
}
