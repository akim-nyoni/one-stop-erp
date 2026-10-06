using One4All.Application.Modules;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Desktop.Navigation;
using One4All.Licensing.Core;

namespace One4All.Desktop.ViewModels;

/// <summary>A node in the left System Tree.</summary>
public sealed class NavNode
{
    /// <summary>Creates a node.</summary>
    /// <param name="title">The node title.</param>
    public NavNode(string title) => Title = title;

    /// <summary>The node title.</summary>
    public string Title { get; }

    /// <summary>The child nodes.</summary>
    public ObservableCollection<NavNode> Children { get; } = [];

    /// <summary>Whether the node is expanded initially.</summary>
    public bool IsExpanded { get; init; }
}

/// <summary>
/// Backs the main shell window (Sage Evolution layout). Builds the System Tree and the module icon grid
/// from the modules that are both <em>licensed</em> and <em>permitted</em> for the signed-in user
/// (brief §18 item 5), and reports the licence status.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly EffectivePermissions _permissions;

    /// <summary>Creates the shell view model for an authenticated session.</summary>
    /// <param name="auth">The authentication result (user + permissions).</param>
    /// <param name="licence">The licence service.</param>
    /// <param name="registry">The module registry (null: built-in modules only).</param>
    public ShellViewModel(AuthenticationResult auth, ILicenceService licence, IModuleRegistry? registry = null)
    {
        _permissions = auth.Permissions;
        UserId = auth.UserId;
        UserDisplayName = auth.DisplayName;

        LicenseValidationResult status = licence.Current;
        string customer = status.Payload?.Customer.Name ?? "Unlicensed";
        LicenceStatusText = licence.Warning is string warning ? $"{customer}  •  {status.Status}  •  {warning}" : $"{customer}  •  {status.Status}";

        // Navigation is built from the registered modules that are licensed and permitted (brief §10).
        Registry = registry ?? new ModuleRegistry();
        IsLicensed = licence.IsModuleLicensed;
        foreach (IModuleDescriptor descriptor in Registry.Available(IsLicensed, _permissions))
        {
            Modules.Add(new ModuleTileViewModel(descriptor));

            NavNode node = new(descriptor.Name) { IsExpanded = false };
            foreach (string group in ModuleCatalogue.ChildGroups)
            {
                node.Children.Add(new NavNode(group));
            }

            TreeRoot.Children.Add(node);
        }

        ContentHeader = "My Desktop";
        ContentMessage = Modules.Count == 0
            ? "No modules are available for your account."
            : $"{Modules.Count} module(s) available. Select a module to begin.";
    }

    /// <summary>Gets the module registry the navigation was built from.</summary>
    public IModuleRegistry Registry { get; }

    /// <summary>Gets whether a licence module is licensed.</summary>
    public Func<string, bool> IsLicensed { get; }

    /// <summary>Whether a desktop operation group should be offered to this user.</summary>
    /// <param name="menuEntry">The operation group (a menu item's tag).</param>
    /// <returns>True when a licensed module the user has permissions in owns it.</returns>
    public bool IsMenuEntryAvailable(string menuEntry) => Registry.IsMenuEntryAvailable(menuEntry, IsLicensed, _permissions);

    /// <summary>The signed-in user's id (the cashier at the POS terminal).</summary>
    public long UserId { get; }

    /// <summary>The signed-in user's display name.</summary>
    public string UserDisplayName { get; }

    /// <summary>The licence status line for the status bar.</summary>
    public string LicenceStatusText { get; }

    /// <summary>The root of the System Tree.</summary>
    public NavNode TreeRoot { get; } = new("One4All") { IsExpanded = true };

    /// <summary>The licensed + permitted module tiles.</summary>
    public ObservableCollection<ModuleTileViewModel> Modules { get; } = [];

    /// <summary>The header shown above the content area.</summary>
    [ObservableProperty]
    private string _contentHeader = string.Empty;

    /// <summary>The placeholder message shown in the content area.</summary>
    [ObservableProperty]
    private string _contentMessage = string.Empty;

    /// <summary>Opens a module workspace (placeholder until module UIs are built in later phases).</summary>
    /// <param name="tile">The selected module tile.</param>
    [RelayCommand]
    private void OpenModule(ModuleTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        ContentHeader = tile.Title;
        ContentMessage =
            $"{tile.Title} workspace.\n\nMaintenance • Transactions • Enquiries • Reports\n\n" +
            "(Module screens are delivered in later phases; this shell already gates access by licence and permission.)";
    }
}
