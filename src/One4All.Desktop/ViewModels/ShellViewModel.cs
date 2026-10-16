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

        // The five things most people do first; only those the user may open.
        foreach (string title in new[] { "Open the till (touch screen)", "Take a payment (teller screen)", "Add a product or service", "Add a customer", "Add or change an employee" })
        {
            if (NavModules.SelectMany(m => m.Entries).FirstOrDefault(e => e.Title == title) is NavEntry entry)
            {
                QuickStart.Add(entry);
            }
        }

        foreach (NavModule module in NavModules)
        {
            FilteredModules.Add(module);
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

    /// <summary>Gets the modules the tree shows: all of them, or only the screens whose name contains the filter.</summary>
    public ObservableCollection<NavModule> FilteredModules { get; } = [];

    /// <summary>What is typed in the tree's filter box.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>Gets the company shown in the status bar.</summary>
    public string CompanyText => _companyText;

    private string _companyText = string.Empty;

    /// <summary>Sets the company shown in the status bar.</summary>
    /// <param name="text">The company's name.</param>
    public void SetCompany(string text)
    {
        _companyText = text;
        OnPropertyChanged(nameof(CompanyText));
    }

    // The filter keeps the modules and kinds that still have a matching screen.
    partial void OnFilterTextChanged(string value)
    {
        FilteredModules.Clear();
        string text = value.Trim();
        foreach (NavModule module in NavModules)
        {
            if (text.Length == 0)
            {
                FilteredModules.Add(module);
                continue;
            }

            List<NavGroup> groups = [.. module.Groups.Select(g => g with { Entries = [.. g.Entries.Where(e => e.Title.Contains(text, StringComparison.OrdinalIgnoreCase) || e.GroupTitle.Contains(text, StringComparison.OrdinalIgnoreCase))] })
                .Where(g => g.Entries.Count > 0)];
            if (groups.Count > 0)
            {
                FilteredModules.Add(new NavModule(module.Title, module.Glyph, module.Accent, module.Purpose, groups) { IsExpanded = true });
            }
        }
    }

    /// <summary>Gets the everyday tasks shown first on the home page (those the user may use).</summary>
    public ObservableCollection<NavEntry> QuickStart { get; } = [];

    /// <summary>Gets a value indicating whether there is a quick-start row to show.</summary>
    public bool HasQuickStart => QuickStart.Count > 0;

    /// <summary>Gets the screens matching the search.</summary>
    public ObservableCollection<NavEntry> SearchResults { get; } = [];

    [ObservableProperty]
    private string _licenceStatusText;

    [ObservableProperty]
    private string _welcome;

    /// <summary>The module shown (null: the home page).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome), nameof(IsModulePage), nameof(PageTitle), nameof(PageSubtitle), nameof(Breadcrumb))]
    private NavModule? _selectedModule;

    /// <summary>Raised when a kind of work is chosen (the page scrolls to its section).</summary>
    public event Action<NavCategory>? CategoryRequested;

    /// <summary>What is typed in the search box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchResults))]
    private string _searchText = string.Empty;

    /// <summary>Gets a value indicating whether the home page is shown.</summary>
    public bool IsHome => SelectedModule is null;

    /// <summary>Gets a value indicating whether a module's page is shown: all its screens, under Maintenance, Transactions, Enquiries and Utilities.</summary>
    public bool IsModulePage => SelectedModule is not null;

    /// <summary>Gets where the page is: Home › module.</summary>
    public string Breadcrumb => SelectedModule is null ? "Home" : $"Home  ›  {SelectedModule.Title}";

    /// <summary>Gets a value indicating whether the search found anything.</summary>
    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>Gets the page's title.</summary>
    public string PageTitle => SelectedModule?.Title ?? "Home";

    /// <summary>Gets the line under the page's title.</summary>
    public string PageSubtitle => SelectedModule is not null
        ? $"{SelectedModule.Purpose} Its screens are below by kind of work — Maintenance, Transactions, Enquiries, Utilities; each opens in its own window."
        : Welcome;

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

    /// <summary>Goes back to the home page.</summary>
    [RelayCommand]
    private void GoBack() => GoHome();

    /// <summary>Shows a module's page.</summary>
    /// <param name="module">The module.</param>
    [RelayCommand]
    private void OpenModule(NavModule? module) => SelectedModule = module;

    /// <summary>Shows a kind of work: its module's page, scrolled to its section.</summary>
    /// <param name="category">The kind of work.</param>
    [RelayCommand]
    private void OpenCategory(NavCategory? category)
    {
        if (category is not null)
        {
            SelectedModule = NavModules.FirstOrDefault(m => m.Categories.Contains(category)) ?? SelectedModule;
            CategoryRequested?.Invoke(category);
        }
    }

    /// <summary>Shows the page a screen is on, at its kind of work.</summary>
    /// <param name="entry">The screen.</param>
    public void ShowPageOf(NavEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        NavModule? module = NavModules.FirstOrDefault(m => m.Title == entry.ModuleTitle);
        if (module?.CategoryOf(entry) is NavCategory category)
        {
            SelectedModule = module;
            CategoryRequested?.Invoke(category);
        }
    }

    // The module shown is lit, and opened, in the module list.
    partial void OnSelectedModuleChanged(NavModule? value)
    {
        foreach (NavModule module in NavModules)
        {
            module.IsActive = ReferenceEquals(module, value);
            if (module.IsActive)
            {
                module.IsExpanded = true;
            }
        }
    }
}
