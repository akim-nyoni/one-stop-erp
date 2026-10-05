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
        yield return Listing("B12", "Budget Version Comparison", "Budget", "For each budget with more than one version: the latest against the one before, per account.",
            "BgtVersions", [new Column("Budget", "Budget", 130), new Column("Versions", "Versions", 70), new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("Previous", "Previous", 95),
             new Column("Current", "Current", 95), new Column("Percent", "Change %", 65), new Column("Change", "Change", 95, true)], AsAt);
        yield return Listing("B13", "Original vs Recommended vs Approved", "Budget", "Per department and account: what was first requested, the last draft recommended, what was approved, and the cut.",
            "BgtCutsTrail", [new Column("Department", "Department", 110), new Column("Account", "Account", 70), new Column("Name", "Name", 150), new Column("Requested", "Requested", 95), new Column("Recommended", "Recommended", 95),
             new Column("Approved", "Approved", 95), new Column("Cut", "Cut", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B15", "Multi-Year Budget & Actual Comparison", "Budget", "Each budgeted account's budget and actual for this year and the two before.",
            "BgtMultiYear", [new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("Budget2", "Budget Y-2", 90), new Column("Actual2", "Actual Y-2", 90), new Column("Budget1", "Budget Y-1", 90),
             new Column("Actual1", "Actual Y-1", 90), new Column("Budget0", "Budget this year", 95), new Column("Actual0", "Actual to date", 95, true)], AsAt);
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
        yield return Listing("B24", "Vote Book", "Budget", "Per budgeted account and department: the year's budget, actual to date, open commitments and what remains.",
            "BgtVoteBook", [new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("Department", "Department", 85), new Column("Budget", "Annual budget", 100), new Column("Actual", "Actual", 95),
             new Column("Committed", "Committed", 95), new Column("Remaining", "Remaining", 100, true)], AsAt);
        yield return Listing("B25", "Commitment / Encumbrance Register", "Budget", "Every commitment of the year with reference, account, department, period and status.",
            "BgtCommitments", [new Column("Reference", "Reference", 110), new Column("Account", "Account", 65), new Column("Department", "Department", 85), new Column("Period", "Period", 45), new Column("Type", "Budget", 90),
             new Column("Status", "Status", 90), new Column("Amount", "Amount", 95, true)], AsAt);
        yield return Listing("B26", "Commitments vs Open Purchase Orders", "Budget", "Open commitments against the open value of the purchase order they reference, with any difference.",
            "BgtCommitmentVsPo", [new Column("Reference", "Reference", 110), new Column("Order", "Order status", 100), new Column("OpenValue", "PO open value", 100), new Column("Committed", "Committed", 100),
             new Column("Difference", "Difference", 95, true)], AsAt);
        yield return Listing("B28", "Expenditure Against Unappropriated Votes", "Budget", "Expense accounts with spending this year but no line in an approved budget.",
            "BgtUnappropriated", [new Column("Account", "Account", 65), new Column("Name", "Name", 180), new Column("Postings", "Postings", 60), new Column("Actual", "Actual to date", 100, true)], AsAt);
        yield return Listing("B29", "Year-End Commitment Carry-Forward Schedule", "Budget", "Commitments of the year that lapsed or were carried into the next year at year end, with who approved the carry-forward.",
            "BgtCarryForward", [new Column("Reference", "Reference", 100), new Column("Account", "Account", 70), new Column("Department", "Department", 100), new Column("Period", "Period", 50), new Column("Outcome", "Outcome", 100),
             new Column("ApprovedBy", "Approved by", 90), new Column("Amount", "Amount", 95, true)], [("AsAtDate", ParameterType.Date)]);
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
        yield return Listing("B41", "Cash Budget vs Actual Cash Flow", "Budget", "Month by month to the date: the cash budget's net flow against the actual net movement on the bank accounts, and the variance.",
            "BgtCash", [new Column("Period", "Period", 60), new Column("Month", "Month", 70), new Column("Budget", "Budgeted net cash", 110), new Column("Actual", "Actual net cash", 110), new Column("Variance", "Variance", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B42", "Capital Project Budget vs Actual vs Commitment", "Budget", "Each capital project: budget, cost to date, commitments, the share spent and the cost to complete.",
            "BgtCapitalProjects", [new Column("Project", "Project", 80), new Column("Name", "Name", 150), new Column("Status", "Status", 90), new Column("Budget", "Budget", 95), new Column("Actual", "Actual", 95),
             new Column("Committed", "Committed", 90), new Column("Spent", "Spent %", 60), new Column("ToComplete", "Cost to complete", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("B44", "Budget Accuracy Review", "Budget", "For the year before the as-at date's year: each account's full-year budget against actual, the variance and how accurate the budget was.",
            "BgtAccuracy", [new Column("Account", "Account", 65), new Column("Name", "Name", 150), new Column("Budget", "Budget", 95), new Column("Actual", "Actual", 95), new Column("Percent", "Variance %", 70),
             new Column("Accuracy", "Accuracy", 80), new Column("Variance", "Variance", 95, true)], AsAt);
        yield return Listing("B45", "Budget Audit Trail", "Budget", "Every recorded change in the year to budgets, lines, virements, commitments and variance comments.",
            "BgtAudit", [new Column("When", "When", 115), new Column("User", "User", 90), new Column("Entity", "Record", 120), new Column("Id", "Id", 60), new Column("Action", "Action", 60),
             new Column("Changed", "Fields changed", 260)], AsAt);
    }
}
