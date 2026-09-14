using One4All.Core.Licensing;

namespace One4All.Desktop.Navigation;

/// <summary>The catalogue of shell modules, in menu order (mirrors the Sage Evolution home grid).</summary>
public static class ModuleCatalogue
{
    /// <summary>All module descriptors.</summary>
    public static IReadOnlyList<ModuleDescriptor> All { get; } =
    [
        new("System", "System Manager", LicenceModules.Core, "⚙", "#455A64"),
        new("Security", "Users & Security", LicenceModules.Core, "\U0001F510", "#546E7A"),
        new("GeneralLedger", "General Ledger", LicenceModules.GeneralLedger, "\U0001F4D8", "#1565C0"),
        new("Sales", "Accounts Receivable", LicenceModules.AccountsReceivable, "\U0001F9FE", "#2E7D32"),
        new("Purchasing", "Accounts Payable", LicenceModules.AccountsPayable, "\U0001F4B8", "#C62828"),
        new("CashBook", "Cash Book", LicenceModules.CashBook, "\U0001F3E6", "#00838F"),
        new("Inventory", "Inventory", LicenceModules.Inventory, "\U0001F4E6", "#6D4C41"),
        new("Procurement", "Procurement", LicenceModules.Procurement, "\U0001F4CB", "#5E35B1"),
        new("SalesOrders", "Order Entry", LicenceModules.SalesOrders, "\U0001F5D2", "#00695C"),
        new("Pos", "Point of Sale", LicenceModules.PointOfSale, "\U0001F6D2", "#AD1457"),
        new("Voucher", "Voucher Management", LicenceModules.Vouchers, "\U0001F3AB", "#EF6C00"),
        new("Municipal", "Municipality", LicenceModules.Municipal, "\U0001F3DB", "#3949AB"),
        new("Housing", "Housing & Land", LicenceModules.Housing, "\U0001F3D8", "#00796B"),
        new("School", "School Management", LicenceModules.School, "\U0001F393", "#7B1FA2"),
        new("JobCosting", "Job Costing", LicenceModules.JobCosting, "\U0001F3D7", "#4E342E"),
        new("FixedAssets", "Fixed Assets", LicenceModules.FixedAssets, "\U0001F5A5", "#37474F"),
        new("Crm", "CRM & Service Desk", LicenceModules.Crm, "\U0001F4DE", "#0277BD"),
        new("Payroll", "HR & Payroll", LicenceModules.Payroll, "\U0001F465", "#283593"),
        new("Budget", "Budgeting", LicenceModules.Budget, "\U0001F4CA", "#1B5E20"),
        new("Reporting", "Reports & Designer", LicenceModules.Core, "\U0001F4C4", "#455A64"),
    ];
}
