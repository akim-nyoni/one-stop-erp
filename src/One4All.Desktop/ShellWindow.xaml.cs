using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Desktop.Navigation;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The Explorer: menus; a toolbar with Exit, Back / Forward, Favourites and Search; the tree (module ▸ kind of work ▸ screen) with a filter;
/// and document tabs - Home, each module's page, and each list opened. A screen's list opens as a tab; its form opens in a window of its own.
/// </summary>
public partial class ShellWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IdleLock _idleLock;
    private readonly List<TabItem> _history = [];
    private int _historyAt = -1;
    private bool _navigating;

    /// <summary>Gets the command that locks the session (Ctrl+L).</summary>
    public static RoutedCommand LockCommand { get; } = new(nameof(LockCommand), typeof(ShellWindow));

    /// <summary>Gets the command that puts the cursor in the search box (Ctrl+F).</summary>
    public static RoutedCommand SearchCommand { get; } = new(nameof(SearchCommand), typeof(ShellWindow));

    /// <summary>Gets the command that closes the tab shown (Ctrl+W).</summary>
    public static RoutedCommand CloseTabCommand { get; } = new(nameof(CloseTabCommand), typeof(ShellWindow));

    /// <summary>Creates the shell window bound to a session view model.</summary>
    /// <param name="viewModel">The shell view model.</param>
    /// <param name="services">The application service provider (used to open tool windows such as the report viewer).</param>
    public ShellWindow(ShellViewModel viewModel, IServiceProvider services)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();
        BuildMenus(viewModel);
        LoadFavourites(viewModel);
        FlowDirection = Localization.UiText.Direction;
        StartLicenceChecks(viewModel);
        _idleLock = new IdleLock(services);
        Loaded += async (_, _) => await ShowCompanyAsync(viewModel).ConfigureAwait(true);
        Remember(HomeTab);
    }

    // The company in the status bar: the first the user's data scope reaches.
    private async Task ShowCompanyAsync(ShellViewModel viewModel)
    {
        try
        {
            using IServiceScope scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.One4AllDbContext>();
            string? name = await db.Companies.OrderBy(c => c.Id).Select(c => c.Name).FirstOrDefaultAsync().ConfigureAwait(true);
            viewModel.SetCompany(name ?? "No company yet");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.NpgsqlException)
        {
            viewModel.SetCompany(string.Empty);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Lock_Click(object sender, RoutedEventArgs e) => _idleLock.Lock();

    private void OpenTwoFactor_Click(object sender, RoutedEventArgs e)
    {
        TwoFactorWindow twoFactor = new(_services) { Owner = this };
        twoFactor.ShowDialog();
    }

    private void OpenReportViewer_Click(object sender, RoutedEventArgs e)
    {
        ReportViewerWindow viewer = new(_services) { Owner = this };
        viewer.Show();
    }

    private void OpenReportDesigner_Click(object sender, RoutedEventArgs e)
    {
        ReportDesignerWindow designer = new(_services) { Owner = this };
        designer.Show();
    }

    private void OpenBandedDesigner_Click(object sender, RoutedEventArgs e)
    {
        BandedDesignerWindow designer = new(_services) { Owner = this };
        designer.Show();
    }

    private void OpenPermissionEditor_Click(object sender, RoutedEventArgs e)
    {
        PermissionEditorWindow editor = new(_services) { Owner = this };
        editor.Show();
    }

    // The update channel: the vendor-signed release, downloaded and checked (SHA-256) before the installer runs; the app closes so it can be replaced.
    private void LargerText_Click(object sender, RoutedEventArgs e) => UiScaling.Set(Application.Localization.UiScale.Larger(UiScaling.Current));

    private void SmallerText_Click(object sender, RoutedEventArgs e) => UiScaling.Set(Application.Localization.UiScale.Smaller(UiScaling.Current));

    private void NormalText_Click(object sender, RoutedEventArgs e) => UiScaling.Set(1.0);

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        Application.Abstractions.System.IUpdateService updates = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Application.Abstractions.System.IUpdateService>(_services);
        Application.Abstractions.System.UpdateCheck check = await updates.CheckAsync().ConfigureAwait(true);
        if (check.Manifest is null || check.Status == One4All.Licensing.Core.UpdateStatus.UpToDate)
        {
            MessageBox.Show(this, check.Problem ?? $"One4All {check.CurrentVersion} is the latest version.", "Updates", MessageBoxButton.OK,
                check.Problem is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
            return;
        }

        string question = $"One4All {check.Manifest.Version} is available (this is {check.CurrentVersion})"
            + (check.Status == One4All.Licensing.Core.UpdateStatus.Required ? " and this version must be updated." : ".")
            + (string.IsNullOrWhiteSpace(check.Manifest.Notes) ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}{check.Manifest.Notes}")
            + $"{Environment.NewLine}{Environment.NewLine}Download and install it now? One4All closes while it installs.";
        if (MessageBox.Show(this, question, "Updates", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Core.Common.Result<string> installer = await updates.DownloadAsync(check.Manifest).ConfigureAwait(true);
        if (installer.IsFailure)
        {
            MessageBox.Show(this, installer.Error.Message, "Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(installer.Value) { UseShellExecute = true });
        global::System.Windows.Application.Current.Shutdown();
    }

    private void OpenTeller_Click(object sender, RoutedEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        new TellerWindow(_services, vm.UserId) { Owner = this }.Show();
    }

    private void OpenPosTerminal_Click(object sender, RoutedEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        PosTerminalWindow terminal = new(_services, vm.UserId, vm.UserDisplayName) { Owner = this };
        terminal.Show();
    }

    // Every hour the licence is re-read (an expiry, a revocation list or a new licence installed elsewhere takes effect), and from seven days
    // before expiry - or while it is in grace, read-only or unusable - the user is reminded once a day (brief §11.5).
    private void StartLicenceChecks(ShellViewModel viewModel)
    {
        Application.Abstractions.Licensing.ILicenceService licence = (Application.Abstractions.Licensing.ILicenceService)_services.GetService(typeof(Application.Abstractions.Licensing.ILicenceService))!;
        DateOnly? lastReminded = null;
        void Remind()
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            if (Application.Licensing.LicenceReminder.ShouldRemind(licence.Current.Status, licence.DaysUntilExpiry, lastReminded, today) && licence.Warning is string warning)
            {
                lastReminded = today;
                MessageBox.Show(this, warning + Environment.NewLine + Environment.NewLine + Localization.UiText.T("Contact your vendor for a new licence."), Localization.UiText.T("Licence"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(1) };
        timer.Tick += async (_, _) =>
        {
            await licence.ReloadAsync().ConfigureAwait(true);
            viewModel.RefreshLicence(licence);
            Remind();
        };
        timer.Start();
        Loaded += (_, _) => Remind();
        Closed += (_, _) => timer.Stop();
    }

    // The menus are built from the navigation: Modules ▸ module ▸ kind of work ▸ (group ▸) screen, and each kind of work (Maintenance,
    // Transactions, Enquiries, Utilities) ▸ module ▸ (group ▸) screen, so a module's submenu only ever shows that module's screens.
    private void BuildMenus(ShellViewModel viewModel)
    {
        foreach (Navigation.NavModule module in viewModel.NavModules)
        {
            System.Windows.Controls.MenuItem moduleItem = new() { Header = Localization.UiText.T(module.Title), Icon = MenuIcon(module.Glyph, module.Accent) };
            foreach (Navigation.NavCategory category in module.Categories)
            {
                System.Windows.Controls.MenuItem categoryItem = new() { Header = Localization.UiText.T(category.Title), Icon = MenuIcon(category.Glyph, category.Accent), ToolTip = category.Purpose };
                AddGroups(categoryItem, category.Groups);
                moduleItem.Items.Add(categoryItem);
            }

            ModulesMenu.Items.Add(moduleItem);
            if (module.HasReports)
            {
                // Reports ▸ module ▸ report, as the Explorer has it: each opens the viewer on that report.
                System.Windows.Controls.MenuItem reportsItem = new() { Header = Localization.UiText.T(module.Title), Icon = MenuIcon(module.Glyph, module.Accent) };
                foreach (Navigation.NavReport report in module.Reports)
                {
                    System.Windows.Controls.MenuItem item = new() { Header = report.Name, ToolTip = report.Code };
                    item.Click += (_, _) => OpenReport(report.Code);
                    reportsItem.Items.Add(item);
                }

                ReportsMenu.Items.Add(reportsItem);
            }
        }

        foreach ((System.Windows.Controls.MenuItem menu, Application.Navigation.MenuArea area) in new[]
        {
            (MaintenanceMenu, Application.Navigation.MenuArea.Maintenance), (TransactionsMenu, Application.Navigation.MenuArea.Transactions),
            (EnquiriesMenu, Application.Navigation.MenuArea.Enquiries), (UtilitiesMenu, Application.Navigation.MenuArea.Utilities),
        })
        {
            foreach (Navigation.NavModule module in viewModel.NavModules)
            {
                if (module.Categories.FirstOrDefault(c => c.Area == area) is Navigation.NavCategory category)
                {
                    System.Windows.Controls.MenuItem moduleItem = new() { Header = Localization.UiText.T(module.Title), Icon = MenuIcon(module.Glyph, module.Accent) };
                    AddGroups(moduleItem, category.Groups);
                    menu.Items.Add(moduleItem);
                }
            }
        }
    }

    // A few screens are listed straight in the menu; more are split by their group.
    private void AddGroups(System.Windows.Controls.MenuItem parent, IReadOnlyList<Navigation.NavGroup> groups)
    {
        if (groups.Count == 1 || groups.Sum(g => g.Entries.Count) <= 8)
        {
            AddEntries(parent, groups.SelectMany(g => g.Entries));
            return;
        }

        foreach (Navigation.NavGroup group in groups)
        {
            System.Windows.Controls.MenuItem groupItem = new() { Header = Localization.UiText.T(group.Title), Icon = MenuIcon(group.Glyph, group.Accent) };
            AddEntries(groupItem, group.Entries);
            parent.Items.Add(groupItem);
        }
    }

    private void AddEntries(System.Windows.Controls.MenuItem parent, IEnumerable<Navigation.NavEntry> entries)
    {
        foreach (Navigation.NavEntry entry in entries)
        {
            System.Windows.Controls.MenuItem item = new() { Header = Localization.UiText.T(entry.Title) + "…", ToolTip = entry.Purpose, Icon = MenuIcon(entry.Glyph, entry.Accent) };
            item.Click += (_, _) => OpenEntry(entry);
            parent.Items.Add(item);
        }
    }

    // A menu item's icon: the glyph in the module's colour.
    private static System.Windows.Controls.TextBlock MenuIcon(string glyph, string accent) => new()
    {
        Text = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(accent)!,
    };

    // ---- Opening things: a module gets a page tab; a screen's list gets a tab; a form or a dedicated window opens on its own. ----
    /// <summary>Opens a screen: its list as a tab, its form or dedicated window on its own.</summary>
    /// <param name="entry">The screen.</param>
    public void OpenEntry(Navigation.NavEntry entry)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        if (entry.Operation is Operations.Operation operation)
        {
            string area = Navigation.NavCategory.Kinds.First(k => k.Area == entry.Area).Title;
            OperationsViewModel model = new(_services, vm.UserId, operation, entry.Title, $"{entry.ModuleTitle}  ›  {area}  ›  {entry.GroupTitle}");
            if (operation.HasList)
            {
                OpenListTab(entry, model);
            }
            else
            {
                new OperationsWindow(_services, model) { Owner = this }.Show();
            }

            StatusText.Text = entry.Purpose;
            return;
        }

        RoutedEventArgs args = new();
        Action? open = entry.WindowKey switch
        {
            "pos-terminal" => () => OpenPosTerminal_Click(this, args),
            "teller" => () => OpenTeller_Click(this, args),
            "approvals" => () => OpenApprovals_Click(this, args),
            "notifications" => () => OpenNotifications_Click(this, args),
            "fault-desk" => () => OpenFaultDesk_Click(this, args),
            "enquiry-desk" => () => OpenEnquiryDesk_Click(this, args),
            "floor-plan" => () => OpenFloorPlan_Click(this, args),
            "journal-approvals" => () => OpenJournalApprovals_Click(this, args),
            "replenishment" => () => OpenReplenishment_Click(this, args),
            "payment-run" => () => OpenPaymentRun_Click(this, args),
            "bank-rec" => () => OpenBankReconciliation_Click(this, args),
            "supplier-rec" => () => OpenSupplierReconciliation_Click(this, args),
            "account-enquiry" => () => OpenAccountEnquiry_Click(this, args),
            "analytics" => () => OpenAnalytics_Click(this, args),
            "credit-control" => () => OpenCreditControl_Click(this, args),
            "customer-documents" => () => OpenCustomerDocuments_Click(this, args),
            "group-consolidation" => () => OpenGroupConsolidation_Click(this, args),
            "title-pipeline" => () => OpenTitlePipeline_Click(this, args),
            "report-viewer" => () => OpenReportViewer_Click(this, args),
            "report-designer" => () => OpenBandedDesigner_Click(this, args),
            "quick-layout" => () => OpenReportDesigner_Click(this, args),
            "period-close" => () => OpenPeriodClose_Click(this, args),
            "bank-rules" => () => OpenBankRules_Click(this, args),
            "audit-trail" => () => OpenAuditTrail_Click(this, args),
            "record-history" => () => OpenRecordHistory_Click(this, args),
            "check-data" => () => OpenCheckData_Click(this, args),
            "data-health" => () => OpenDataHealth_Click(this, args),
            "scheduled-jobs" => () => OpenScheduledJobs_Click(this, args),
            "permissions" => () => OpenPermissionEditor_Click(this, args),
            "two-factor" => () => OpenTwoFactor_Click(this, args),
            _ => null,
        };
        open?.Invoke();
    }

    /// <summary>Opens a module's page as a tab (or brings it to the front).</summary>
    /// <param name="module">The module.</param>
    public void OpenModule(Navigation.NavModule module)
    {
        foreach (Navigation.NavModule m in ((ShellViewModel)DataContext).NavModules)
        {
            m.IsActive = ReferenceEquals(m, module) || m.Title == module.Title;
            if (m.IsActive)
            {
                m.IsExpanded = true;
            }
        }

        if (FindTab(module) is TabItem open)
        {
            Tabs.SelectedItem = open;
            return;
        }

        ModulePage page = new(module);
        page.EntryOpened += OpenEntry;
        page.ReportOpened += OpenReport;
        AddTab(module.Title, page, module);
    }

    private void OpenListTab(Navigation.NavEntry entry, OperationsViewModel model)
    {
        if (FindTab(entry) is TabItem open)
        {
            Tabs.SelectedItem = open;
            return;
        }

        OperationsListView view = new() { DataContext = model, Services = _services };
        AddTab(entry.Operation?.ListTitle ?? entry.Title, view, entry);
    }

    private TabItem? FindTab(object key) => Tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag is TabKey k && (ReferenceEquals(k.Key, key) || (k.Key is Navigation.NavModule a && key is Navigation.NavModule b && a.Title == b.Title)));

    private sealed record TabKey(object Key);

    // A document tab with a close button; Ctrl+W or the × closes it.
    private void AddTab(string title, UIElement content, object key)
    {
        TabItem tab = new() { Tag = new TabKey(key), Content = content };
        StackPanel header = new() { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        Button close = new() { Content = "✕", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(3, 0, 3, 0), FontSize = 10, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "Close (Ctrl+W)" };
        close.Click += (_, _) => CloseTabItem(tab);
        header.Children.Add(close);
        header.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                CloseTabItem(tab);
            }
        };
        tab.Header = header;
        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;
    }

    private void CloseTabItem(TabItem tab)
    {
        if (ReferenceEquals(tab, HomeTab))
        {
            return;
        }

        _history.RemoveAll(t => ReferenceEquals(t, tab));
        _historyAt = Math.Min(_historyAt, _history.Count - 1);
        Tabs.Items.Remove(tab);
        if (Tabs.SelectedItem is null)
        {
            Tabs.SelectedItem = _historyAt >= 0 ? _history[_historyAt] : HomeTab;
        }
    }

    private void CloseTab(object sender, ExecutedRoutedEventArgs e)
    {
        if (Tabs.SelectedItem is TabItem tab)
        {
            CloseTabItem(tab);
        }
    }

    // ---- Back / Forward over the tabs visited. ----
    private void Remember(TabItem tab)
    {
        if (_navigating)
        {
            return;
        }

        if (_historyAt < _history.Count - 1)
        {
            _history.RemoveRange(_historyAt + 1, _history.Count - _historyAt - 1);
        }

        if (_history.Count == 0 || !ReferenceEquals(_history[^1], tab))
        {
            _history.Add(tab);
        }

        _historyAt = _history.Count - 1;
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        BackButton.IsEnabled = _historyAt > 0;
        ForwardButton.IsEnabled = _historyAt < _history.Count - 1;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Tabs) && Tabs.SelectedItem is TabItem tab)
        {
            Remember(tab);
            if (tab.Tag is TabKey { Key: Navigation.NavModule module })
            {
                foreach (Navigation.NavModule m in ((ShellViewModel)DataContext).NavModules)
                {
                    m.IsActive = m.Title == module.Title;
                }
            }
        }
    }

    private void Go(int step)
    {
        int at = _historyAt + step;
        if (at < 0 || at >= _history.Count)
        {
            return;
        }

        _navigating = true;
        _historyAt = at;
        Tabs.SelectedItem = _history[at];
        _navigating = false;
        UpdateHistoryButtons();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Go(-1);

    private void Forward_Click(object sender, RoutedEventArgs e) => Go(1);

    private void Home_Click(object sender, RoutedEventArgs e) => Tabs.SelectedItem = HomeTab;

    /// <summary>Opens the Report Viewer on a report.</summary>
    /// <param name="code">The report code.</param>
    public void OpenReport(string code)
    {
        new ReportViewerWindow(_services, code) { Owner = this }.Show();
        StatusText.Text = $"Report {code} opened in the viewer.";
    }

    private void Module_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Navigation.NavModule module)
        {
            OpenModule(module);
        }
    }

    private void Entry_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Navigation.NavEntry entry)
        {
            OpenEntry(entry);
        }
    }

    // ---- Favourites: pinned screens, kept per user in %AppData%. ----
    private static string FavouritesFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "One4All", "favourites.txt");

    private void LoadFavourites(ShellViewModel vm)
    {
        try
        {
            if (!System.IO.File.Exists(FavouritesFile))
            {
                return;
            }

            foreach (string title in System.IO.File.ReadAllLines(FavouritesFile).Where(l => l.Length > 0).Distinct())
            {
                if (vm.NavModules.SelectMany(m => m.Entries).FirstOrDefault(e => e.Title == title) is Navigation.NavEntry entry)
                {
                    AddFavourite(entry);
                }
            }
        }
        catch (System.IO.IOException)
        {
            // Favourites are a convenience; start without them.
        }
    }

    private void AddFavourite(Navigation.NavEntry entry)
    {
        if (FavouritesMenu.Items.OfType<MenuItem>().Any(m => m.Tag is Navigation.NavEntry e && e.Title == entry.Title))
        {
            return;
        }

        MenuItem item = new() { Header = entry.Title, Tag = entry, Icon = MenuIcon(entry.Glyph, entry.Accent) };
        item.Click += (_, _) => OpenEntry(entry);
        FavouritesMenu.Items.Add(item);
    }

    private void PinTab_Click(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is TabItem { Tag: TabKey { Key: Navigation.NavEntry entry } })
        {
            AddFavourite(entry);
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FavouritesFile)!);
                System.IO.File.WriteAllLines(FavouritesFile, FavouritesMenu.Items.OfType<MenuItem>().Where(m => m.Tag is Navigation.NavEntry).Select(m => ((Navigation.NavEntry)m.Tag).Title));
            }
            catch (System.IO.IOException)
            {
                // Pinned for this session only.
            }

            StatusText.Text = $"{entry.Title} pinned to Favourites.";
        }
        else
        {
            StatusText.Text = "Open a screen's list first, then pin it.";
        }
    }

    // ---- The tree: a module opens its page; a kind of work opens the module's page; a screen opens on double-click or Enter. ----
    private void ModuleTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        switch (e.NewValue)
        {
            case Navigation.NavModule module:
                OpenModule(vm.NavModules.FirstOrDefault(m => m.Title == module.Title) ?? module);
                break;
            case Navigation.NavCategory category:
                OpenModule(vm.NavModules.First(m => m.Title == category.ModuleTitle));
                break;
            case Navigation.NavEntry entry:
                StatusText.Text = entry.Purpose;
                break;
        }
    }

    private void ModuleTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ModuleTree.SelectedItem is Navigation.NavEntry entry)
        {
            OpenEntry(entry);
            e.Handled = true;
        }
    }

    private void ModuleTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ModuleTree.SelectedItem is Navigation.NavEntry entry)
        {
            OpenEntry(entry);
            e.Handled = true;
        }
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e) => ((ShellViewModel)DataContext).FilterText = string.Empty;

    // ---- The search: typing lists matching screens under the box; Down moves into the list, Enter opens the first (or chosen) one, Escape clears it. ----
    private void FocusSearch(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        if (e.Key == Key.Escape)
        {
            vm.SearchText = string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && vm.SearchResults.Count > 0)
        {
            SearchList.SelectedIndex = 0;
            SearchList.UpdateLayout();
            ((ListBoxItem?)SearchList.ItemContainerGenerator.ContainerFromIndex(0))?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && vm.SearchResults.Count > 0)
        {
            OpenFromSearch(vm.SearchResults[0]);
            e.Handled = true;
        }
    }

    private void SearchList_Click(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is Navigation.NavEntry entry)
        {
            OpenFromSearch(entry);
        }
    }

    private void SearchList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchList.SelectedItem is Navigation.NavEntry entry)
        {
            OpenFromSearch(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ((ShellViewModel)DataContext).SearchText = string.Empty;
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void OpenFromSearch(Navigation.NavEntry entry)
    {
        ((ShellViewModel)DataContext).SearchText = string.Empty;
        OpenEntry(entry);
    }

    private void HowTo_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Three ways to find a screen:" + Environment.NewLine + Environment.NewLine
        + "1.  Type what you want to do in the Search box at the top right (for example \"add a product\", \"pay a supplier\") and press Enter."
        + Environment.NewLine + Environment.NewLine
        + "2.  Use the tree on the left: open a module, then Maintenance (setting up), Transactions (day-to-day work), Enquiries (looking things up) or Utilities "
        + "(period end and housekeeping), and double-click the screen. The Filter box above the tree narrows it to screens whose name contains what you type."
        + Environment.NewLine + Environment.NewLine
        + "3.  Use the menus: Modules ▸ module ▸ kind of work ▸ screen; or Maintenance / Transactions / Enquiries / Utilities ▸ module ▸ screen."
        + Environment.NewLine + Environment.NewLine
        + "A module and a screen's list open as tabs across the top; Back and Forward move between tabs you visited, and × or Ctrl+W closes one. "
        + "Add… on a list opens the form in its own window. Favourites keeps the screens you pin.",
        "How to find a screen", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OpenAccountEnquiry_Click(object sender, RoutedEventArgs e)
    {
        AccountEnquiryWindow enquiry = new(_services) { Owner = this };
        enquiry.Show();
    }

    private void OpenAnalytics_Click(object sender, RoutedEventArgs e)
    {
        AnalyticsWindow analytics = new(_services) { Owner = this };
        analytics.Show();
    }

    private void OpenCreditControl_Click(object sender, RoutedEventArgs e)
    {
        CreditControlWindow credit = new(_services) { Owner = this };
        credit.Show();
    }

    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e)
    {
        AuditTrailWindow audit = new(_services) { Owner = this };
        audit.Show();
    }

    private void OpenRecordHistory_Click(object sender, RoutedEventArgs e) => new RecordHistoryWindow(_services) { Owner = this }.Show();

    private void OpenGroupConsolidation_Click(object sender, RoutedEventArgs e)
    {
        GroupConsolidationWindow consolidation = new(_services) { Owner = this };
        consolidation.Show();
    }

    private void OpenCheckData_Click(object sender, RoutedEventArgs e)
    {
        CheckDataWindow check = new(_services) { Owner = this };
        check.Show();
    }

    private void OpenFloorPlan_Click(object sender, RoutedEventArgs e)
    {
        FloorPlanWindow floor = new(_services) { Owner = this };
        floor.Show();
    }

    private void OpenEnquiryDesk_Click(object sender, RoutedEventArgs e)
    {
        EnquiryDeskWindow desk = new(_services) { Owner = this };
        desk.Show();
    }

    private void OpenFaultDesk_Click(object sender, RoutedEventArgs e)
    {
        FaultDeskWindow desk = new(_services) { Owner = this };
        desk.Show();
    }

    private void OpenNotifications_Click(object sender, RoutedEventArgs e)
    {
        NotificationCentreWindow notifications = new(_services) { Owner = this };
        notifications.Show();
    }

    private void OpenTitlePipeline_Click(object sender, RoutedEventArgs e)
    {
        TitlePipelineWindow titles = new(_services) { Owner = this };
        titles.Show();
    }

    private void OpenScheduledJobs_Click(object sender, RoutedEventArgs e)
    {
        ScheduledJobsWindow jobs = new(_services) { Owner = this };
        jobs.Show();
    }

    private void OpenDataHealth_Click(object sender, RoutedEventArgs e)
    {
        DataHealthWindow health = new(_services) { Owner = this };
        health.Show();
    }

    private void OpenBankRules_Click(object sender, RoutedEventArgs e)
    {
        BankRulesWindow rules = new(_services) { Owner = this };
        rules.Show();
    }

    private void OpenPeriodClose_Click(object sender, RoutedEventArgs e)
    {
        PeriodCloseWindow close = new(_services) { Owner = this };
        close.Show();
    }

    private void OpenCustomerDocuments_Click(object sender, RoutedEventArgs e)
    {
        CustomerDocumentsWindow documents = new(_services) { Owner = this };
        documents.Show();
    }

    private void OpenSupplierReconciliation_Click(object sender, RoutedEventArgs e)
    {
        SupplierReconciliationWindow recon = new(_services) { Owner = this };
        recon.Show();
    }

    private void OpenBankReconciliation_Click(object sender, RoutedEventArgs e)
    {
        BankReconciliationWindow recon = new(_services) { Owner = this };
        recon.Show();
    }

    private void OpenPaymentRun_Click(object sender, RoutedEventArgs e)
    {
        PaymentRunWindow run = new(_services) { Owner = this };
        run.Show();
    }

    private void OpenReplenishment_Click(object sender, RoutedEventArgs e)
    {
        ReplenishmentWindow replenishment = new(_services) { Owner = this };
        replenishment.Show();
    }

    private void OpenApprovals_Click(object sender, RoutedEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        ApprovalsWindow approvals = new(_services, vm.UserId) { Owner = this };
        approvals.Show();
    }

    private void OpenJournalApprovals_Click(object sender, RoutedEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        JournalApprovalsWindow journals = new(_services, vm.UserId) { Owner = this };
        journals.Show();
    }
}
