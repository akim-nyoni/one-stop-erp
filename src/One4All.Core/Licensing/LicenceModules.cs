namespace One4All.Core.Licensing;

/// <summary>
/// The canonical licensing module codes (brief §11.3). Menu visibility, API authorisation and job
/// scheduling all gate on these. <see cref="Core"/> is always available (the System Manager and
/// security are never separately licensed); every other module can carry its own expiry and limits.
/// </summary>
public static class LicenceModules
{
    /// <summary>System Manager + Security + Audit. Always licensed; the product cannot run without it.</summary>
    public const string Core = "CORE";

    /// <summary>General/Nominal Ledger.</summary>
    public const string GeneralLedger = "GL";

    /// <summary>Accounts Receivable.</summary>
    public const string AccountsReceivable = "AR";

    /// <summary>Accounts Payable.</summary>
    public const string AccountsPayable = "AP";

    /// <summary>Cash Book / Treasury.</summary>
    public const string CashBook = "CB";

    /// <summary>Inventory / Stock Control.</summary>
    public const string Inventory = "INV";

    /// <summary>Procurement (Requisition → PO → GRN → match).</summary>
    public const string Procurement = "POP";

    /// <summary>Sales Order Processing / Distribution.</summary>
    public const string SalesOrders = "SOP";

    /// <summary>Point of Sale.</summary>
    public const string PointOfSale = "POS";

    /// <summary>Voucher Management.</summary>
    public const string Vouchers = "VOU";

    /// <summary>Municipality / Council billing.</summary>
    public const string Municipal = "MUNI";

    /// <summary>Housing, Land &amp; Estates.</summary>
    public const string Housing = "HSE";

    /// <summary>Schools.</summary>
    public const string School = "SCH";

    /// <summary>Construction / Job Costing / Weighbridge.</summary>
    public const string JobCosting = "JOB";

    /// <summary>Fixed Assets.</summary>
    public const string FixedAssets = "FA";

    /// <summary>CRM &amp; Service Desk.</summary>
    public const string Crm = "CRM";

    /// <summary>Human Resources &amp; Payroll.</summary>
    public const string Payroll = "PAY";

    /// <summary>Budgeting, Planning &amp; Commitment Control.</summary>
    public const string Budget = "BUD";

    /// <summary>Report Designer (build/edit custom reports).</summary>
    public const string ReportDesigner = "REPORT_DESIGNER";

    /// <summary>All module codes.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Core, GeneralLedger, AccountsReceivable, AccountsPayable, CashBook, Inventory,
        Procurement, SalesOrders, PointOfSale, Vouchers, Municipal, Housing, School,
        JobCosting, FixedAssets, Crm, Payroll, Budget, ReportDesigner,
    ];
}
