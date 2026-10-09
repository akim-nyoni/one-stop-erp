namespace One4All.Application.Navigation;

/// <summary>Where an action sits in the menus, Sage style (brief §12: the shell is grouped by what the user is doing, not by table).</summary>
public enum MenuArea
{
    /// <summary>Setting things up: master records, rules, rates, policies, templates.</summary>
    Maintenance = 1,

    /// <summary>Day-to-day documents and postings.</summary>
    Transactions = 2,

    /// <summary>Looking things up: statements, positions, searches, forecasts.</summary>
    Enquiries = 3,

    /// <summary>Period-end runs and system housekeeping.</summary>
    Utilities = 4,
}

/// <summary>Works out an action's menu area from its title — the console's actions read as verbs and nouns, so a few rules place nearly all of them. Pure.</summary>
public static class MenuAreas
{
    // Period-end runs and system housekeeping.
    private static readonly string[] UtilityWords =
    [
        "Year-end", "year-end", "Archive", "Back up", "Revalue", "Run the annual", "Escalate", "Refresh reporting", "encryption", "Reconcile budget commitments",
        "Expire loyalty", "Settle VAT", "Recognise deferred", "Raise finance charges", "Review direct database", "Licence", "webhooks", "Accrue leave",
        "Accrue unpaid", "Recurring", "Run standing orders", "Dunning", "Overhead allocation", "Hyperinflation", "Classify items", "Failed offline",
        "Stock write-down", "expected credit loss", "Expected credit loss", "Burst a report", "Report subscription", "Email customer statements", "Import",
        "Multi-year framework", "VAT return", "Withholding tax returns", "Preparation deadlines",
    ];

    // Documents and postings whose titles would otherwise read as set-up.
    private static readonly string[] TransactionStarts =
    [
        "Add an approver", "Add a cost to", "Add a driver-based", "Build the staff budget", "Create gift card stock",
    ];

    // Looking things up.
    private static readonly string[] EnquiryStarts =
    [
        "Show", "Find", "Quick search", "Supplier scorecard", "Property enquiry", "Ratepayer statement", "Customer statement", "Supplier statement", "Cash-flow forecast",
        "Cost to complete", "Vote book", "Budget vs actual", "Variance", "Stock availability", "Till cash-up position", "Aged debt", "Stock valuation", "Price a basket",
        "Scheme progress", "Land register", "Waiting list enquiry", "Job cost position", "Tariff catalogue", "Suggest picks", "Pick list", "Remittance advice",
        "Payslips", "Cheque register", "Grant disclosure", "Housing expected credit loss",
    ];

    // Setting things up.
    private static readonly string[] MaintenanceStarts =
    [
        "Set ", "Add ", "Create ", "Save ", "Define ", "Plan ", "Register a garnishee", "Manage ", "Change currency", "Reset a password", "Activate or deactivate a user",
        "Assign a role", "Join or leave a group", "Meter reading routes", "Map stands",
    ];

    private static readonly string[] MaintenanceWords =
    [
        "types", "policies", "policy", "layout", "classes", "Menu items", "Tables and sections", "Pay elements", "Tax exemption certificates", "Reporting trees",
        "Funds, projects", "Collection agents", "Manual receipt books", "Shift rosters", "Allocation quotas", "Stand pricing", "Tariff billing cycle",
        "Council service", "Price a list", "Future prices", "Till favourites", "Refund window", "Category under", "Language and terminology", "Exchange-rate",
        "Addresses and contacts", "Credit limits", "Supplier bank details", "Employee record", "master-data", "Delegate my approvals", "Email approval links",
        "API keys", "Salary scale", "establishment", "Academic years", "Classes and streams", "Bins:", "Development levies", "Development covenants",
        "Housing cooperatives", "Cemeteries", "Council licences", "Bank detail", "Promotional codes", "Asset classes", "Land, schemes and stands", "Layout plans",
        "Bursaries", "Staff children", "Sibling discount", "Delivery zone rate", "Customer contract", "Sales commissions", "Reason codes", "replenishment",
        "catch-weight", "sale limits", "Bank file layout", "logo", "Attachments", "Benefits in kind", "Term dates", "Pay group", "net-pay protection",
        "Optional fee",
    ];

    /// <summary>The area an action belongs to.</summary>
    /// <param name="title">Its title.</param>
    /// <returns>The area (Transactions when nothing marks it otherwise).</returns>
    public static MenuArea Classify(string title)
    {
        string text = (title ?? string.Empty).Trim();
        if (EnquiryStarts.Any(s => text.StartsWith(s, StringComparison.Ordinal)))
        {
            return MenuArea.Enquiries;
        }

        if (UtilityWords.Any(w => text.Contains(w, StringComparison.Ordinal)))
        {
            return MenuArea.Utilities;
        }

        if (TransactionStarts.Any(s => text.StartsWith(s, StringComparison.Ordinal)))
        {
            return MenuArea.Transactions;
        }

        if (MaintenanceStarts.Any(s => text.StartsWith(s, StringComparison.Ordinal)) || MaintenanceWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return MenuArea.Maintenance;
        }

        return MenuArea.Transactions;
    }
}
