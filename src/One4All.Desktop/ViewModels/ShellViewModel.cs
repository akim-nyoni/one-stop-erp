using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Application.Modules;
using One4All.Core.Security;
using One4All.Desktop.Navigation;
using One4All.Licensing.Core;

namespace One4All.Desktop.ViewModels;

/// <summary>
/// The main window's state. Navigation is by business module: the left list and the home page show only the modules the user's licence and permissions
/// cover; choosing one shows just that module's screens in plain-language groups; a search finds any screen by what it does.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly EffectivePermissions _permissions;

    /// <summary>Creates the view model.</summary>
    /// <param name="auth">The signed-in user.</param>
    /// <param name="licence">The licence.</param>
    /// <param name="registry">The registered modules.</param>
    public ShellViewModel(AuthenticationResult auth, ILicenceService licence, IModuleRegistry? registry = null)
    {
        _permissions = auth.Permissions;
        UserId = auth.UserId;
        UserDisplayName = auth.DisplayName;
        _licenceStatusText = StatusLine(licence);
        Registry = registry ?? new ModuleRegistry();
        IsLicensed = licence.IsModuleLicensed;
        foreach (NavModule module in NavigationBuilder.Build(Operations.OperationCatalogue.All(), IsMenuEntryAvailable))
        {
            NavModules.Add(module);
        }

        _welcome = NavModules.Count == 0 ? "No modules are available for your account. Ask your administrator to give you a role."
            : $"Welcome, {UserDisplayName}. Choose what you want to work on, or type in the search box what you want to do (for example \"add a product\").";
    }

    /// <summary>Gets the registered modules.</summary>
    public IModuleRegistry Registry { get; }

    /// <summary>Gets whether a licence module is usable.</summary>
    public Func<string, bool> IsLicensed { get; }

    /// <summary>Whether an operations-console module is licensed and permitted.</summary>
    /// <param name="menuEntry">The console module.</param>
    /// <returns>True when the user may use it.</returns>
    public bool IsMenuEntryAvailable(string menuEntry) => Registry.IsMenuEntryAvailable(menuEntry, IsLicensed, _permissions);

    /// <summary>Gets the signed-in user.</summary>
    public long UserId { get; }

    /// <summary>Gets the user's name.</summary>
    public string UserDisplayName { get; }

    /// <summary>Gets the modules the user may use.</summary>
    public ObservableCollection<NavModule> NavModules { get; } = [];

    /// <summary>Gets the screens matching the search.</summary>
    public ObservableCollection<NavEntry> SearchResults { get; } = [];

    [ObservableProperty]
    private string _licenceStatusText;

    [ObservableProperty]
    private string _welcome;

    /// <summary>The module shown (null: the home page).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome), nameof(IsModulePage), nameof(PageTitle), nameof(PageSubtitle))]
    private NavModule? _selectedModule;

    /// <summary>What is typed in the search box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchResults))]
    private string _searchText = string.Empty;

    /// <summary>Gets a value indicating whether the home page is shown.</summary>
    public bool IsHome => SelectedModule is null;

    /// <summary>Gets a value indicating whether a module's page is shown.</summary>
    public bool IsModulePage => SelectedModule is not null;

    /// <summary>Gets a value indicating whether the search found anything.</summary>
    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>Gets the page's title.</summary>
    public string PageTitle => SelectedModule?.Title ?? "Home";

    /// <summary>Gets the line under the page's title.</summary>
    public string PageSubtitle => SelectedModule?.Purpose ?? Welcome;

    /// <summary>Re-reads the licence status line.</summary>
    /// <param name="licence">The licence.</param>
    public void RefreshLicence(ILicenceService licence) => LicenceStatusText = StatusLine(licence);

    private static string StatusLine(ILicenceService licence)
    {
        LicenseValidationResult status = licence.Current;
        string customer = status.Payload?.Customer.Name ?? "Unlicensed";
        return licence.Warning is string warning ? $"{customer}  •  {status.Status}  •  {warning}" : $"{customer}  •  {status.Status}";
    }

    // Every word typed must appear in the screen's name, purpose, module or group; the closest names come first.
    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();
        string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length > 0 && value.Trim().Length >= 2)
        {
            foreach (NavEntry entry in NavModules.SelectMany(m => m.Entries)
                .Where(e => words.All(w => e.SearchText.Contains(Stem(w), StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => words.Count(w => e.Title.Contains(Stem(w), StringComparison.OrdinalIgnoreCase)))
                .ThenBy(e => e.Title.Length).Take(20))
            {
                SearchResults.Add(entry);
            }
        }

        OnPropertyChanged(nameof(HasSearchResults));
    }

    // "products" finds "product", "items" finds "item": a plural matches its singular.
    private static string Stem(string word) => word.Length > 3 && word.EndsWith('s') ? word[..^1] : word;

    /// <summary>Shows the home page.</summary>
    [RelayCommand]
    private void GoHome() => SelectedModule = null;

    /// <summary>Shows a module's page.</summary>
    /// <param name="module">The module.</param>
    [RelayCommand]
    private void OpenModule(NavModule? module) => SelectedModule = module;
}
