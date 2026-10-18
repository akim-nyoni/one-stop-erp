using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Budgeting, planning and commitment control reports from brief §14 A-B (the B codes) over <c>Bgt*</c> data commands
/// served by the budgeting report data provider. The fiscal year is the one containing the as-at date.
/// </summary>
public static class BudgetingReports
{
    private static readonly (string, ParameterType)[] AsAt = [("AsAtDate", ParameterType.Date)];

    /// <summary>Every B-coded definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("B1", "Budget Preparation Status Board", "Budget", "Every department's budget preparation for the year of the date: not started, in progress, submitted or approved, against the deadline.",
            "BgtPrepStatus", [new Column("Department", "Department", 120), new Column("Status", "Status", 90), new Column("Version", "Version", 55), new Column("Updated", "Last change", 80), new Column("Deadline", "Deadline", 75),
             new Column("Days", "Days to / past deadline", 90), new Column("Requested", "Requested", 100, true)], [("AsAtDate", ParameterType.Date), ("Deadline", ParameterType.Date)]);
        yield return Listing("B2", "Departmental Budget Submission", "Budget", "Every budget of the year by department and version: each account's quarters and annual total.",
            "BgtSubmission", [new Column("Budget", "Budget", 120), new Column("Version", "Ver", 40), new Column("Status", "Status", 75), new Column("Department", "Department", 85), new Column("Account", "Account", 65),
             new Column("Name", "Name", 140), new Column("Q1", "Q1", 80), new Column("Q2", "Q2", 80), new Column("Q3", "Q3", 80), new Column("Q4", "Q4", 80), new Column("Total", "Annual", 95, true)], AsAt);
        yield return Listing("B3", "Target vs Submission Gap", "Budget", "Per department: the ceiling issued, what its latest draft requests, and the gap still to reconcile.",
            "BgtCeilingGap", [new Column("Department", "Department", 120), new Column("Ceiling", "Ceiling", 100), new Column("Requested", "Requested", 100), new Column("Status", "Status", 90), new Column("Gap", "Over / (under)", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B4", "Consolidated Draft Budget by Hierarchy", "Budget", "The latest drafts rolled up department → division → directorate → company, at the level asked for.",
            "BgtHierarchy", [new Column("Level", "Level", 80), new Column("Unit", "Unit", 160), new Column("Departments", "Departments", 70), new Column("Requested", "Requested", 110, true)],
            [("AsAtDate", ParameterType.Date), ("Level", ParameterType.String)]);
        yield return Listing("B35", "Variance Decomposition", "Budget", "Year-to-date variance per budget line split into volume (actual vs budgeted quantity at budget price), FX (foreign amounts at actual vs planned rate) and price / rate.",
            "BgtDecomposition", [new Column("Account", "Account", 60), new Column("Name", "Name", 140), new Column("Department", "Dept", 55), new Column("Budget", "Budget YTD", 90), new Column("Actual", "Actual YTD", 90),
             new Column("BudgetQty", "Budget qty", 65), new Column("ActualQty", "Actual qty", 65), new Column("Volume", "Volume", 85), new Column("Price", "Price / rate", 85), new Column("Fx", "FX", 80),
             new Column("Variance", "Variance", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B5", "Assumption & Driver Register", "Budget", "The year's planning drivers with their value and the budget lines each one feeds.",
            "BgtDrivers", [new Column("Driver", "Driver", 80), new Column("Name", "Name", 160), new Column("Unit", "Unit", 50), new Column("Value", "Value", 80), new Column("Lines", "Lines fed", 60), new Column("Amount", "Amount fed", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B6", "Assumption Sensitivity / What-If", "Budget", "Change one driver: each dependent line scaled to the new value, the change per line and on the total.",
            "BgtWhatIf", [new Column("Budget", "Budget", 110), new Column("Account", "Account", 70), new Column("Department", "Department", 100), new Column("Current", "Current", 95), new Column("Revised", "Revised", 95),
             new Column("Change", "Change", 95, true)], [("AsAtDate", ParameterType.Date), ("DriverCode", ParameterType.String), ("NewValue", ParameterType.Decimal)]);
        yield return Listing("B7", "Budget Preparation Basis", "Budget", "How every line of the year's budgets was arrived at - zero-based, incremental, actuals-based or driver-based - with the driver.",
            "BgtBasis", [new Column("Budget", "Budget", 120), new Column("Department", "Department", 100), new Column("Account", "Account", 70), new Column("Basis", "Basis", 90), new Column("Driver", "Driver", 70),
             new Column("Amount", "Annual amount", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B8", "Establishment-Driven Staff Budget", "Budget", "Per department: approved posts at their annual cost less the vacancy factor, against the staff budget held for the department.",
            "BgtStaff", [new Column("Department", "Department", 110), new Column("Posts", "Posts", 50), new Column("Filled", "Filled", 50), new Column("Establishment", "Establishment cost", 105), new Column("Vacancy", "Vacancy factor", 80),
             new Column("Expected", "Expected cost", 100), new Column("Budgeted", "Staff budget", 100), new Column("Difference", "Budget less expected", 100, true)],
            [("AsAtDate", ParameterType.Date), ("StaffAccount", ParameterType.String), ("VacancyPercent", ParameterType.Decimal)]);
        yield return Listing("B9", "Capital Budget Submission & Phasing", "Budget", "The year's capital budgets per vote by month, with the cumulative share spent of the year - the S-curve.",
            "BgtCapitalPhasing", [new Column("Budget", "Budget", 110), new Column("Account", "Vote", 70), new Column("Period", "Period", 50), new Column("Month", "Month", 65), new Column("Cumulative", "Cumulative", 95),
             new Column("Share", "Cumulative %", 75), new Column("Amount", "Phased", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B10", "Revenue Budget Build-up", "Budget", "Driver-based revenue lines: the volume each is built on, the implied rate and the amount.",
            "BgtRevenueBuild", [new Column("Budget", "Budget", 110), new Column("Account", "Account", 70), new Column("Name", "Name", 140), new Column("Driver", "Driver", 70), new Column("Volume", "Volume", 80),
             new Column("Rate", "Rate", 80), new Column("Amount", "Amount", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B11", "Budget Version Register", "Budget", "Every budget version of the year ever created: number, type, status, author and date, why it was raised, "
            + "who approved it under which resolution, and its seal hash with whether it still verifies.", "BgtVersionRegister",
            [new Column("Budget", "Budget", 130), new Column("Version", "Ver", 40), new Column("Type", "Type", 100), new Column("Status", "Status", 75), new Column("Author", "Author", 80),
             new Column("Created", "Created", 75), new Column("Reason", "Reason", 140), new Column("ApprovedBy", "Approved by", 80), new Column("Resolution", "Resolution", 90),
             new Column("Seal", "Seal hash", 110), new Column("Verified", "Verifies", 60)], AsAt);
        yield return Listing("B12", "Budget Version Comparison", "Budget", "For each budget with more than one version (or the one named): the latest against the one before, or any two versions "
            + "chosen, per account - or per account and period, or per account and cost centre.",
            "BgtVersions", [new Column("Budget", "Budget", 130), new Column("Versions", "Versions", 70), new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("Detail", "Period / cost centre", 80),
             new Column("Previous", "Previous", 95), new Column("Current", "Current", 95), new Column("Percent", "Change %", 65), new Column("Change", "Change", 95, true)],
            [("AsAtDate", ParameterType.Date), ("Budget", ParameterType.String), ("FromVersion", ParameterType.Integer), ("ToVersion", ParameterType.Integer), ("By", ParameterType.String)]);
        yield return Listing("B13", "Original vs Recommended vs Approved", "Budget", "Per department and account: what was first requested, the last draft recommended, what was approved, and the cut.",
            "BgtCutsTrail", [new Column("Department", "Department", 110), new Column("Account", "Account", 70), new Column("Name", "Name", 150), new Column("Requested", "Requested", 95), new Column("Recommended", "Recommended", 95),
             new Column("Approved", "Approved", 95), new Column("Cut", "Cut", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B14", "Budget Approval Audit", "Budget", "Every status change of the year's budgets - submitted, approved, activated, superseded - with who made it and the days since the previous step.",
            "BgtApprovalAudit", [new Column("When", "When", 115), new Column("Budget", "Budget", 140), new Column("Status", "New status", 90), new Column("User", "By", 90), new Column("Days", "Days since last step", 100)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B15", "Multi-Year Budget & Actual Comparison", "Budget", "Each budgeted account's budget and actual for this year and the two before.",
            "BgtMultiYear", [new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("Budget2", "Budget Y-2", 90), new Column("Actual2", "Actual Y-2", 90), new Column("Budget1", "Budget Y-1", 90),
             new Column("Actual1", "Actual Y-1", 90), new Column("Budget0", "Budget this year", 95), new Column("Actual0", "Actual to date", 95, true)], AsAt);
        yield return Listing("B16", "Archived Budget Retrieval", "Budget", "Any sealed budget of the date's year in full, exactly as approved - every account by quarter - headed by "
            + "its seal hash re-verified against the lines. Choose the budget by label and version (latest sealed by default).", "BgtArchive",
            [new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("CostCentre", "Cost centre", 75), new Column("Q1", "Q1", 85), new Column("Q2", "Q2", 85),
             new Column("Q3", "Q3", 85), new Column("Q4", "Q4", 85), new Column("Total", "Total", 100, true)],
            [("AsAtDate", ParameterType.Date), ("BudgetLabel", ParameterType.String), ("Version", ParameterType.Integer)]);
        yield return Listing("B17", "Budget Versions, Seals & Superseded Budgets", "Budget", "Every budget held: type, version, status, why it was revised, and whether its seal still verifies.",
            "BgtRegister", [new Column("Budget", "Budget", 130), new Column("Year", "Year", 55), new Column("Type", "Type", 110), new Column("Version", "Ver", 40), new Column("Status", "Status", 80),
             new Column("Active", "Active", 50), new Column("Reason", "Reason", 160), new Column("Seal", "Seal", 110), new Column("Total", "Total", 100, true)], AsAt);
        yield return Listing("B18", "Budget Line Change Log", "Budget", "Every recorded change to budget lines in the year, with who made it and the fields changed.",
            "BgtLineChanges", [new Column("When", "When", 115), new Column("User", "User", 90), new Column("Id", "Line id", 60), new Column("Action", "Action", 60), new Column("Changed", "Fields changed", 300)], AsAt);
        yield return Listing("B19", "Approved Budget Register by Vote & Department", "Budget", "The appropriation of record: every in-force budget line for the year by account and department, by quarter.",
            "BgtApproved", [new Column("Account", "Vote", 70), new Column("Name", "Name", 150), new Column("Department", "Department", 100), new Column("Q1", "Q1", 85), new Column("Q2", "Q2", 85), new Column("Q3", "Q3", 85),
             new Column("Q4", "Q4", 85), new Column("Total", "Approved", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B20", "Virement Register", "Budget", "Transfers between budget lines in the year: from, to, period, amount, justification, who asked and who reviewed.",
            "BgtVirements", [new Column("Requested", "Requested", 115), new Column("From", "From", 70), new Column("To", "To", 70), new Column("Period", "Period", 45), new Column("Justification", "Justification", 200),
             new Column("Status", "Status", 70), new Column("RequestedBy", "Requested by", 90), new Column("ReviewedBy", "Reviewed by", 90), new Column("Amount", "Amount", 95, true)], AsAt);
        yield return Listing("B21", "Budget Reconciliation (Original to Current)", "Budget", "Per vote: original appropriation, plus supplementary, plus or minus virements and rephasing, equals the current approved budget.",
            "BgtReconciliation", [new Column("Account", "Vote", 70), new Column("Name", "Name", 150), new Column("Original", "Original", 95), new Column("Supplementary", "Supplementary", 95), new Column("Virements", "Virements", 95),
             new Column("Rephasing", "Rephasing", 80), new Column("Current", "Current approved", 105, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B22", "Supplementary Appropriation Schedule", "Budget", "Each supplementary or revised budget of the year: per vote, the amount against the original approved, the change, and the reason.",
            "BgtSupplementary", [new Column("Budget", "Budget", 140), new Column("Status", "Status", 70), new Column("Account", "Vote", 70), new Column("Original", "Original", 95), new Column("Revised", "Revised", 95),
             new Column("Reason", "Reason", 180), new Column("Change", "Change", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B23", "Rephasing Report", "Budget", "Between consecutive versions of a budget: votes whose annual total is unchanged but whose months moved, with the value moved.",
            "BgtRephasing", [new Column("Budget", "Budget", 120), new Column("Versions", "Versions", 70), new Column("Account", "Vote", 70), new Column("Annual", "Annual (unchanged)", 100), new Column("Detail", "Months moved", 220),
             new Column("Moved", "Value moved", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B24", "Vote Book", "Budget", "Per budgeted account and department: the year's budget, actual to date, open commitments and what remains.",
            "BgtVoteBook", [new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("Department", "Department", 85), new Column("Budget", "Annual budget", 100), new Column("Actual", "Actual", 95),
             new Column("Committed", "Committed", 95), new Column("Remaining", "Remaining", 100, true)], AsAt);
        yield return Listing("B25", "Commitment / Encumbrance Register", "Budget", "Every commitment of the year with reference, account, department, period and status.",
            "BgtCommitments", [new Column("Reference", "Reference", 110), new Column("Account", "Account", 65), new Column("Department", "Department", 85), new Column("Period", "Period", 45), new Column("Type", "Budget", 90),
             new Column("Status", "Status", 90), new Column("Amount", "Amount", 95, true)], AsAt);
        yield return Listing("B26", "Commitments vs Open Purchase Orders", "Budget", "Open commitments against the open value of the purchase order they reference, with any difference.",
            "BgtCommitmentVsPo", [new Column("Reference", "Reference", 110), new Column("Order", "Order status", 100), new Column("OpenValue", "PO open value", 100), new Column("Committed", "Committed", 100),
             new Column("Difference", "Difference", 95, true)], AsAt);
        yield return Listing("B27", "Budget Block & Override Report", "Budget", "Postings fund control refused for want of an appropriation, and those let through on a supervisor's override with the stated reason.",
            "BgtBlocks", [new Column("When", "When", 115), new Column("Outcome", "Outcome", 80), new Column("Date", "Posting date", 75), new Column("Account", "Vote", 70), new Column("Department", "Department", 80),
             new Column("Document", "Document", 120), new Column("User", "By", 80), new Column("AuthorisedBy", "Authorised by", 90), new Column("Reason", "Reason", 180), new Column("Amount", "Amount", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B28", "Expenditure Against Unappropriated Votes", "Budget", "Expense accounts with spending this year but no line in an approved budget.",
            "BgtUnappropriated", [new Column("Account", "Account", 65), new Column("Name", "Name", 180), new Column("Postings", "Postings", 60), new Column("Actual", "Actual to date", 100, true)], AsAt);
        yield return Listing("B29", "Year-End Commitment Carry-Forward Schedule", "Budget", "Commitments of the year that lapsed or were carried into the next year at year end, with who approved the carry-forward.",
            "BgtCarryForward", [new Column("Reference", "Reference", 100), new Column("Account", "Account", 70), new Column("Department", "Department", 100), new Column("Period", "Period", 50), new Column("Outcome", "Outcome", 100),
             new Column("ApprovedBy", "Approved by", 90), new Column("Amount", "Amount", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B31", "Budget vs Actual vs Commitment vs Available", "Budget", "At account, department or division level, for the date's period, the year to date or the "
            + "full year: budget, actual, open commitments, what is available and the share used, with the number of postings behind each actual.", "BgtAvailable",
            [new Column("Level", "Level", 140), new Column("Name", "Name", 150), new Column("Budget", "Budget", 100), new Column("Actual", "Actual", 95), new Column("Committed", "Committed", 95),
             new Column("Available", "Available", 100), new Column("Used", "Used %", 55), new Column("Postings", "Postings", 55, true)],
            [("AsAtDate", ParameterType.Date), ("Level", ParameterType.String), ("Basis", ParameterType.String)]);
        yield return Listing("B32", "Budget Holder Statement", "Budget", "Per budget holder: their department's votes - budget, spent and committed to date, available and utilisation.",
            "BgtHolders", [new Column("Holder", "Budget holder", 130), new Column("Department", "Department", 100), new Column("Account", "Vote", 70), new Column("Budget", "Budget", 95), new Column("Actual", "Spent", 95),
             new Column("Committed", "Committed", 90), new Column("Used", "Used %", 60), new Column("Available", "Available", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B30", "Unspent Capital Carry-Forward Schedule", "Budget", "Per capital vote: the year's capital budget, spent to the date, and the unspent balance proposed to carry forward.",
            "BgtCapitalUnspent", [new Column("Account", "Vote", 70), new Column("Name", "Name", 150), new Column("Budget", "Capital budget", 100), new Column("Spent", "Spent", 95), new Column("Used", "Used %", 60),
             new Column("Unspent", "Unspent to carry", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B33", "Budget Utilisation (RAG)", "Budget", "Year-to-date use of each budgeted account's year-to-date budget: green under 90%, amber to 100%, red over.",
            "BgtUtilisation", [new Column("Account", "Account", 65), new Column("Name", "Name", 160), new Column("Budget", "Budget YTD", 95), new Column("Used", "Used %", 60), new Column("Rag", "RAG", 55),
             new Column("Actual", "Actual YTD", 100, true)], AsAt);
        yield return Listing("B34", "Over-Budget & Threshold Exceptions", "Budget", "Only the budgeted accounts at amber or red, worst first.",
            "BgtExceptions", [new Column("Account", "Account", 65), new Column("Name", "Name", 160), new Column("Budget", "Budget YTD", 95), new Column("Used", "Used %", 60), new Column("Rag", "RAG", 55),
             new Column("Over", "Over budget", 100, true)], AsAt);
        yield return Listing("B36", "Variance Commentary Pack", "Budget", "The year's variance explanations by period and account, beside the budget and actual they explain.",
            "BgtCommentary", [new Column("Period", "Period", 45), new Column("Account", "Account", 65), new Column("Name", "Name", 140), new Column("Budget", "Budget YTD", 95), new Column("Actual", "Actual YTD", 95),
             new Column("Author", "Author", 85), new Column("Comment", "Explanation", 260)], AsAt);
        yield return Listing("B37", "Departmental Management Accounts", "Budget", "Per department, year to date: income and expenditure against budget, and the surplus or deficit.",
            "BgtDepartments", [new Column("Department", "Department", 100), new Column("IncomeBudget", "Income budget", 95), new Column("Income", "Income", 95), new Column("ExpenseBudget", "Expense budget", 95),
             new Column("Expense", "Expenditure", 95), new Column("Surplus", "Surplus / (deficit)", 100, true)], AsAt);
        yield return Listing("B38", "Full-Year Forecast / Outturn Projection", "Budget", "Per vote: the approved budget, actual to date, the forecast for the rest of the year (latest reforecast, else the remaining budget) and the projected outturn against budget.",
            "BgtOutturn", [new Column("Account", "Vote", 70), new Column("Name", "Name", 150), new Column("Department", "Department", 100), new Column("Budget", "Budget", 95), new Column("Actual", "Actual to date", 95),
             new Column("Forecast", "Forecast remaining", 100), new Column("Outturn", "Projected outturn", 100), new Column("Variance", "Under / (over)", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B39", "Forecast vs Forecast Movement", "Budget", "Between the last two reforecast rounds of the year: each vote's forecast before and after, the movement and the reason given.",
            "BgtForecastMovement", [new Column("Account", "Vote", 70), new Column("Name", "Name", 150), new Column("Previous", "Previous round", 100), new Column("Latest", "Latest round", 100), new Column("Reason", "Reason", 200),
             new Column("Movement", "Movement", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B40", "Rolling Forecast", "Budget", "The next N months from the date: per vote, the latest reforecast (else the budget) month by month.",
            "BgtRolling", [new Column("Account", "Vote", 70), new Column("Name", "Name", 150), new Column("Month", "Month", 65), new Column("Source", "Source", 80), new Column("Amount", "Forecast", 95, true)],
            [("AsAtDate", ParameterType.Date), ("Months", ParameterType.Integer)]);
        yield return Listing("B41", "Cash Budget vs Actual Cash Flow", "Budget", "Every month of the year: the cash budget's net flow against the actual net movement on the bank accounts up to the date, "
            + "then the cash forecast (receivables and payables due, payment runs, scheduled and manual flows) for the months still to come, and the variance.",
            "BgtCash", [new Column("Period", "Period", 60), new Column("Month", "Month", 70), new Column("Basis", "Actual / forecast", 70), new Column("Budget", "Budgeted net cash", 110),
             new Column("Actual", "Actual / forecast net cash", 120), new Column("Variance", "Variance", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B42", "Capital Project Budget vs Actual vs Commitment", "Budget", "Each capital project: budget, cost to date, commitments, the share spent and the cost to complete.",
            "BgtCapitalProjects", [new Column("Project", "Project", 80), new Column("Name", "Name", 150), new Column("Status", "Status", 90), new Column("Budget", "Budget", 95), new Column("Actual", "Actual", 95),
             new Column("Committed", "Committed", 90), new Column("Spent", "Spent %", 60), new Column("ToComplete", "Cost to complete", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B43", "Revenue Budget Performance", "Budget", "Per revenue source (income account), year to date: budget, billed, collected (the share of the billed "
            + "invoices since paid), billed against budget and the collection rate.", "BgtRevenuePerformance",
            [new Column("Account", "Source", 65), new Column("Name", "Name", 150), new Column("Budget", "Budget YTD", 100), new Column("Billed", "Billed", 100), new Column("BilledPct", "Billed vs budget", 70),
             new Column("Collected", "Collected", 100), new Column("CollectionPct", "Collection rate", 70, true)], AsAt);
        yield return Listing("B44", "Budget Accuracy Review", "Budget", "For the year before the as-at date's year: each account's full-year budget against actual, the variance and how accurate the budget was.",
            "BgtAccuracy", [new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("Budget", "Budget", 95), new Column("Actual", "Actual", 95), new Column("Percent", "Variance %", 70),
             new Column("Accuracy", "Accuracy", 80), new Column("Variance", "Variance", 95, true)], AsAt);
        yield return Listing("B45", "Budget Audit Trail", "Budget", "Every recorded change in the year to budgets, lines, virements, commitments and variance comments.",
            "BgtAudit", [new Column("When", "When", 115), new Column("User", "User", 90), new Column("Entity", "Record", 120), new Column("Id", "Id", 60), new Column("Action", "Action", 60),
             new Column("Changed", "Fields changed", 260)], AsAt);
    }
}
