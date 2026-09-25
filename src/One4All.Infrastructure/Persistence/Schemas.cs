namespace One4All.Infrastructure.Persistence;

/// <summary>
/// The PostgreSQL schema names, one per module (brief §9). Centralised so entity
/// configurations reference a constant rather than a string literal.
/// </summary>
public static class Schemas
{
    /// <summary>System Manager: companies, branches, periods, currencies, rates, number sequences, tax codes.</summary>
    public const string System = "sys";

    /// <summary>Security: users, roles, groups, permissions, scopes, sessions.</summary>
    public const string Security = "sec";

    /// <summary>Audit: the append-only change log and hash chain.</summary>
    public const string Audit = "audit";

    /// <summary>General/Nominal Ledger.</summary>
    public const string GeneralLedger = "gl";

    /// <summary>Accounts Receivable (Sales Ledger).</summary>
    public const string AccountsReceivable = "ar";

    /// <summary>Accounts Payable (Purchase Ledger).</summary>
    public const string AccountsPayable = "ap";

    /// <summary>Trading sub-ledger shared between AR and AP (open items, allocations).</summary>
    public const string Trading = "trd";

    /// <summary>Workflow &amp; approvals engine.</summary>
    public const string Workflow = "wf";

    /// <summary>Cash Book / Treasury.</summary>
    public const string CashBook = "cb";

    /// <summary>Inventory / Stock Control.</summary>
    public const string Inventory = "inv";

    /// <summary>Point of Sale.</summary>
    public const string PointOfSale = "pos";

    /// <summary>Restaurant / Hospitality (menu items, recipes, tables).</summary>
    public const string Restaurant = "res";

    /// <summary>Voucher management (gift cards, coupons, payment vouchers).</summary>
    public const string Vouchers = "vou";

    /// <summary>Purchase Order Processing / Procurement.</summary>
    public const string Purchasing = "pop";

    /// <summary>Sales Order Processing / Distribution.</summary>
    public const string SalesOrders = "sop";

    /// <summary>Municipality / Council billing.</summary>
    public const string Municipal = "mun";

    /// <summary>Housing, Land &amp; Estates.</summary>
    public const string Housing = "hse";

    /// <summary>Schools.</summary>
    public const string School = "sch";

    /// <summary>Construction / Job Costing.</summary>
    public const string JobCosting = "job";

    /// <summary>Fixed Assets.</summary>
    public const string FixedAssets = "fa";

    /// <summary>Human Resources.</summary>
    public const string HumanResources = "hr";

    /// <summary>Payroll (walled-garden schema).</summary>
    public const string Payroll = "pay";

    /// <summary>Employee expense claims.</summary>
    public const string Expenses = "exp";

    /// <summary>Budgeting, Planning &amp; Commitment Control.</summary>
    public const string Budget = "bud";

    /// <summary>CRM &amp; Service Desk (leads, opportunities, activities).</summary>
    public const string Crm = "crm";

    /// <summary>Cashiering &amp; Receipting (stations, sessions, receipts).</summary>
    public const string Receipting = "rcp";

    /// <summary>Reporting: report definitions, schedules, delivery logs.</summary>
    public const string Reporting = "rpt";

    /// <summary>Licensing status and history.</summary>
    public const string Licensing = "lic";
}
