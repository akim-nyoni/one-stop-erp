namespace One4All.Desktop.Operations;

/// <summary>
/// Every action on the operations console, by module. Each operation reads its form, looks records up by their codes, calls the
/// tested application service and reports what happened; the rules all live in the services.
/// </summary>
public static partial class OperationCatalogue
{
    /// <summary>The modules, in menu order.</summary>
    public static readonly IReadOnlyList<string> Modules = ["Point of Sale", "Stock", "Weighbridge", "Fixed Assets", "Contracts", "HR & Attendance", "Banking", "Sales & Schools", "Documents"];

    /// <summary>Every operation.</summary>
    /// <returns>The operations, by module.</returns>
    public static IReadOnlyList<Operation> All() => [.. Retail(), .. Stock(), .. Weighbridge(), .. FixedAssets(), .. Contracts(), .. People(), .. Banking(), .. SalesAndSchools(), .. Workflow(), .. WorkflowRules(), .. Purchasing(), .. PurchaseOrders(), .. Budgeting(), .. Crm(), .. WhatsAppInbox(), .. Municipal(), .. Receipting(), .. Housing(), .. Trading(), .. Ledger(), .. MasterData(), .. Snapshots(), .. Licence(), .. Documents(), .. Workbench()];
}
