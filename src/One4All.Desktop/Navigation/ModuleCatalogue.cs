using One4All.Application.Modules;

namespace One4All.Desktop.Navigation;

/// <summary>The shell's module tiles: the registered modules (built-in and plug-in), in menu order.</summary>
public static class ModuleCatalogue
{
    /// <summary>The standard Sage-style child groups shown under a module in the tree.</summary>
    public static readonly string[] ChildGroups = ["Maintenance", "Transactions", "Enquiries", "Reports"];

    /// <summary>Gets the built-in modules (the shell uses the registry, which adds plug-ins).</summary>
    public static IReadOnlyList<IModuleDescriptor> All => BuiltInModules.All;
}
