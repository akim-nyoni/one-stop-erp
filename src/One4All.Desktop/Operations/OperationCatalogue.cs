namespace One4All.Desktop.Operations;

/// <summary>
/// Every action on the operations console, by module. Each operation reads its form, looks records up by their codes, calls the
/// tested application service and reports what happened; the rules all live in the services.
/// </summary>
public static partial class OperationCatalogue
{
    // The Operations menu's order; a module with operations but not listed here follows at the end.
    private static readonly string[] MenuOrder = ["System Manager", "General Ledger", "Customers", "Suppliers", "Point of Sale", "Restaurant", "Stock", "Purchasing", "Budgeting", "CRM",
        "Municipality", "Receipting", "Housing", "Weighbridge", "Fixed Assets", "Contracts", "HR & Attendance", "Banking", "Approvals", "Sales & Schools", "Documents"];

    private static readonly Lazy<IReadOnlyList<string>> ModuleList = new(() =>
    {
        HashSet<string> used = [.. All().Select(o => o.Module)];
        return [.. MenuOrder.Where(used.Contains), .. used.Except(MenuOrder).Order(StringComparer.Ordinal)];
    });

    /// <summary>Gets the modules that have operations, in menu order (every menu entry opens its own module, not the first one).</summary>
    public static IReadOnlyList<string> Modules => ModuleList.Value;

    /// <summary>Every operation.</summary>
    /// <returns>The operations, by module.</returns>
    public static IReadOnlyList<Operation> All() => [.. Retail(), .. Stock(), .. Weighbridge(), .. FixedAssets(), .. Contracts(), .. People(), .. Banking(), .. SalesAndSchools(), .. Workflow(), .. WorkflowRules(), .. Purchasing(), .. PurchaseOrders(), .. Budgeting(), .. Crm(), .. WhatsAppInbox(), .. Municipal(), .. Receipting(), .. Housing(), .. Trading(), .. Ledger(), .. MasterData(), .. Snapshots(), .. Licence(), .. Documents(), .. Workbench(), .. Vouchers(), .. Restaurant(), .. Control()];
}
