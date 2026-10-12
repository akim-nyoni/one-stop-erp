using One4All.Core.Licensing;
using One4All.Core.Security;

namespace One4All.Application.Modules;

/// <summary>
/// A functional module registering itself with the system (brief §10 plug-in model): its id (the permission module), name, the licence
/// module that gates it, the API areas, scheduled tasks and desktop operation groups it owns, and its tile. The shell, the API gate and the
/// job scheduler work from these, so a module that is not licensed, or for which the user holds no permission, is simply absent.
/// Built-in modules are listed in <see cref="BuiltInModules"/>; a plug-in registers another <see cref="IModuleDescriptor"/> in DI.
/// </summary>
public interface IModuleDescriptor
{
    /// <summary>Gets the id: the permission module its permissions are grouped under (e.g. <c>Budget</c>).</summary>
    string Code { get; }

    /// <summary>Gets the display name.</summary>
    string Name { get; }

    /// <summary>Gets the licence module that gates it (see <see cref="LicenceModules"/>).</summary>
    string LicenceModule { get; }

    /// <summary>Gets the tile glyph.</summary>
    string Glyph { get; }

    /// <summary>Gets the tile accent colour.</summary>
    string Accent { get; }

    /// <summary>Gets the API areas (<c>/api/{area}/…</c>) it owns; an area two modules share is open to either's users.</summary>
    IReadOnlyList<string> ApiAreas { get; }

    /// <summary>Gets the scheduled task keys it owns: they run only while it is licensed.</summary>
    IReadOnlyList<string> TaskKeys { get; }

    /// <summary>Gets the desktop operation groups (menu entries) it owns.</summary>
    IReadOnlyList<string> MenuEntries { get; }
}

/// <summary>A module description.</summary>
/// <param name="Code">The id (permission module).</param>
/// <param name="Name">The display name.</param>
/// <param name="LicenceModule">The gating licence module.</param>
/// <param name="Glyph">The tile glyph.</param>
/// <param name="Accent">The tile colour.</param>
/// <param name="ApiAreas">The API areas it owns.</param>
/// <param name="TaskKeys">The scheduled tasks it owns.</param>
/// <param name="MenuEntries">The desktop operation groups it owns.</param>
public sealed record ModuleDescriptor(string Code, string Name, string LicenceModule, string Glyph, string Accent, IReadOnlyList<string> ApiAreas, IReadOnlyList<string> TaskKeys,
    IReadOnlyList<string> MenuEntries) : IModuleDescriptor
{
    /// <summary>Gets the module's permissions (from the permission catalogue).</summary>
    public IEnumerable<PermissionDefinition> Permissions => PermissionCatalogue.All.Where(p => string.Equals(p.Module, Code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The modules that ship with the product, in menu order (the Sage-style home grid).</summary>
public static class BuiltInModules
{
    /// <summary>Gets every built-in module.</summary>
    public static IReadOnlyList<IModuleDescriptor> All { get; } =
    [
        new ModuleDescriptor("System", "System Manager", LicenceModules.Core, "⚙", "#455A64", ["system", "offline"], ["system.backup", "messages.despatch", "security.reencrypt-fields", "system.idempotency-purge", "workflow.escalate"],
            ["System Manager", "Approvals", "Documents"]),
        new ModuleDescriptor("Security", "Users & Security", LicenceModules.Core, "\U0001F510", "#546E7A", ["audit"], [], []),
        new ModuleDescriptor("GeneralLedger", "General Ledger", LicenceModules.GeneralLedger, "\U0001F4D8", "#1565C0", ["gl", "accounting", "interco", "consolidation"], ["tax.certificate-expiry"], ["General Ledger"]),
        new ModuleDescriptor("Sales", "Accounts Receivable", LicenceModules.AccountsReceivable, "\U0001F9FE", "#2E7D32", ["ar", "credit", "trading", "receipting", "pricing", "messages"],
            ["statements.email"], ["Sales & Schools", "Customers"]),
        new ModuleDescriptor("Purchasing", "Accounts Payable", LicenceModules.AccountsPayable, "\U0001F4B8", "#C62828", ["ap", "trading"], [], ["Suppliers"]),
        new ModuleDescriptor("CashBook", "Cash Book", LicenceModules.CashBook, "\U0001F3E6", "#00838F", ["banking", "treasury", "receipting"], [], ["Banking", "Receipting"]),
        new ModuleDescriptor("Inventory", "Inventory", LicenceModules.Inventory, "\U0001F4E6", "#6D4C41", ["stock", "pricing"], [], ["Stock", "Weighbridge"]),
        new ModuleDescriptor("Procurement", "Procurement", LicenceModules.Procurement, "\U0001F4CB", "#5E35B1", ["pop"], [], ["Purchasing"]),
        new ModuleDescriptor("SalesOrders", "Order Entry", LicenceModules.SalesOrders, "\U0001F5D2", "#00695C", ["sop"], [], []),
        new ModuleDescriptor("Pos", "Point of Sale", LicenceModules.PointOfSale, "\U0001F6D2", "#AD1457", [], ["pos.fiscal-retry"], ["Point of Sale", "Restaurant"]),
        new ModuleDescriptor("Voucher", "Voucher Management", LicenceModules.Vouchers, "\U0001F3AB", "#EF6C00", ["vouchers"], [], []),
        new ModuleDescriptor("Municipal", "Municipality", LicenceModules.Municipal, "\U0001F3DB", "#3949AB", ["municipal"], ["servicedesk.escalate"], ["Municipality"]),
        new ModuleDescriptor("Housing", "Housing & Land", LicenceModules.Housing, "\U0001F3D8", "#00796B", ["housing"], ["housing.lapse-offers"], ["Housing"]),
        new ModuleDescriptor("School", "School Management", LicenceModules.School, "\U0001F393", "#7B1FA2", [], [], ["Sales & Schools"]),
        new ModuleDescriptor("JobCosting", "Job Costing", LicenceModules.JobCosting, "\U0001F3D7", "#4E342E", ["jobs"], [], ["Contracts"]),
        new ModuleDescriptor("FixedAssets", "Fixed Assets", LicenceModules.FixedAssets, "\U0001F5A5", "#37474F", [], [], ["Fixed Assets"]),
        new ModuleDescriptor("Crm", "CRM & Service Desk", LicenceModules.Crm, "\U0001F4DE", "#0277BD", ["crm", "messages"], [], ["CRM"]),
        new ModuleDescriptor("Payroll", "HR & Payroll", LicenceModules.Payroll, "\U0001F465", "#283593", ["hr", "payroll"], ["payroll.leave-accrual", "hr.expiry-alerts"], ["HR & Attendance"]),
        new ModuleDescriptor("Budget", "Budgeting", LicenceModules.Budget, "\U0001F4CA", "#1B5E20", ["budgets"], ["budget.reconcile-commitments", "budget.notices"], ["Budgeting"]),
        new ModuleDescriptor("Reporting", "Reports & Designer", LicenceModules.Core, "\U0001F4C4", "#455A64", ["reports", "analytics"], ["reports.subscriptions", "reporting.refresh-snapshots"], []),
    ];
}

/// <summary>The registered modules, and what is available to whom.</summary>
public interface IModuleRegistry
{
    /// <summary>Gets every registered module, built-in first.</summary>
    IReadOnlyList<IModuleDescriptor> All { get; }

    /// <summary>The module owning a scheduled task.</summary>
    /// <param name="taskKey">The task key.</param>
    /// <returns>The module, or null when no module claims it.</returns>
    IModuleDescriptor? ForTask(string taskKey);

    /// <summary>The modules owning a desktop operation group.</summary>
    /// <param name="menuEntry">The group.</param>
    /// <returns>The modules.</returns>
    IReadOnlyList<IModuleDescriptor> ForMenuEntry(string menuEntry);

    /// <summary>The modules owning an API area.</summary>
    /// <param name="area">The area.</param>
    /// <returns>The modules.</returns>
    IReadOnlyList<IModuleDescriptor> ForApiArea(string area);

    /// <summary>The modules a user sees: licensed, and holding at least one of its permissions.</summary>
    /// <param name="isLicensed">Whether a licence module is licensed.</param>
    /// <param name="permissions">The user's permissions.</param>
    /// <returns>The modules, in order.</returns>
    IReadOnlyList<IModuleDescriptor> Available(Func<string, bool> isLicensed, EffectivePermissions permissions);

    /// <summary>Whether a desktop operation group is available: some module owning it is (an unclaimed group is not).</summary>
    /// <param name="menuEntry">The group.</param>
    /// <param name="isLicensed">Whether a licence module is licensed.</param>
    /// <param name="permissions">The user's permissions.</param>
    /// <returns>True when it should be shown.</returns>
    bool IsMenuEntryAvailable(string menuEntry, Func<string, bool> isLicensed, EffectivePermissions permissions);

    /// <summary>Checks the registrations: unique ids, known licence modules, each task claimed once, every permission module and every given task described.</summary>
    /// <param name="registeredTaskKeys">The task keys actually registered.</param>
    /// <returns>The problems (none when consistent).</returns>
    IReadOnlyList<string> Problems(IEnumerable<string> registeredTaskKeys);
}

/// <summary>The module registry over the built-in modules and any plug-ins registered in DI.</summary>
public sealed class ModuleRegistry : IModuleRegistry
{
    /// <summary>Creates the registry.</summary>
    /// <param name="plugIns">Plug-in modules registered in DI (built-ins are always included).</param>
    public ModuleRegistry(IEnumerable<IModuleDescriptor>? plugIns = null) => All = [.. BuiltInModules.All, .. plugIns ?? []];

    /// <inheritdoc />
    public IReadOnlyList<IModuleDescriptor> All { get; }

    /// <inheritdoc />
    public IModuleDescriptor? ForTask(string taskKey) => All.FirstOrDefault(m => m.TaskKeys.Contains(taskKey, StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IReadOnlyList<IModuleDescriptor> ForMenuEntry(string menuEntry) => [.. All.Where(m => m.MenuEntries.Contains(menuEntry, StringComparer.OrdinalIgnoreCase))];

    /// <inheritdoc />
    public IReadOnlyList<IModuleDescriptor> ForApiArea(string area) => [.. All.Where(m => m.ApiAreas.Contains(area, StringComparer.OrdinalIgnoreCase))];

    /// <inheritdoc />
    public IReadOnlyList<IModuleDescriptor> Available(Func<string, bool> isLicensed, EffectivePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(isLicensed);
        ArgumentNullException.ThrowIfNull(permissions);
        return [.. All.Where(m => isLicensed(m.LicenceModule) && permissions.HasAnyInModule(m.Code))];
    }

    /// <inheritdoc />
    public bool IsMenuEntryAvailable(string menuEntry, Func<string, bool> isLicensed, EffectivePermissions permissions)
        => ForMenuEntry(menuEntry).Any(m => isLicensed(m.LicenceModule) && permissions.HasAnyInModule(m.Code));

    /// <inheritdoc />
    public IReadOnlyList<string> Problems(IEnumerable<string> registeredTaskKeys)
    {
        List<string> problems = [];
        problems.AddRange(All.GroupBy(m => m.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => $"Module {g.Key} is registered {g.Count()} times."));
        problems.AddRange(All.Where(m => !LicenceModules.All.Contains(m.LicenceModule, StringComparer.OrdinalIgnoreCase)).Select(m => $"Module {m.Code} is gated by unknown licence module {m.LicenceModule}."));
        problems.AddRange(All.SelectMany(m => m.TaskKeys.Select(t => (Task: t, Module: m.Code))).GroupBy(x => x.Task, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => $"Task {g.Key} is claimed by {string.Join(" and ", g.Select(x => x.Module))}."));
        problems.AddRange(PermissionCatalogue.All.Select(p => p.Module).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(module => All.All(m => !string.Equals(m.Code, module, StringComparison.OrdinalIgnoreCase))).Select(module => $"Permission module {module} has no module descriptor."));
        problems.AddRange((registeredTaskKeys ?? []).Where(t => ForTask(t) is null).Select(t => $"Scheduled task {t} belongs to no module."));
        return problems;
    }
}
