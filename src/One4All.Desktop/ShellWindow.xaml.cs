using System.Windows;
using System.Windows.Input;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>A screen open in its own window, listed on the shell's "Open screens" bar.</summary>
/// <param name="Entry">The screen.</param>
/// <param name="Window">Its window.</param>
public sealed record OpenScreen(Navigation.NavEntry Entry, Window Window);

/// <summary>
/// The main shell window: menus, a search box, the module list and the page — Home (the modules) or a module (every screen by kind of work).
/// Each screen opens in its own window; the "Open screens" bar lists them to jump between.
/// </summary>
public partial class ShellWindow : Window, System.ComponentModel.INotifyPropertyChanged
{
    private readonly IServiceProvider _services;
    private readonly IdleLock _idleLock;

    /// <summary>Gets the command that locks the session (Ctrl+L).</summary>
    public static RoutedCommand LockCommand { get; } = new(nameof(LockCommand), typeof(ShellWindow));

    /// <summary>Gets the command that puts the cursor in the search box (Ctrl+F).</summary>
    public static RoutedCommand SearchCommand { get; } = new(nameof(SearchCommand), typeof(ShellWindow));

    /// <summary>Gets the screens open in their own windows.</summary>
    public System.Collections.ObjectModel.ObservableCollection<OpenScreen> OpenScreens { get; } = [];

    /// <summary>Gets a value indicating whether any screen is open.</summary>
    public bool HasOpenScreens => OpenScreens.Count > 0;

    /// <summary>Creates the shell window bound to a session view model.</summary>
    /// <param name="viewModel">The shell view model.</param>
    /// <param name="services">The application service provider (used to open tool windows such as the report viewer).</param>
    public ShellWindow(ShellViewModel viewModel, IServiceProvider services)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();
        BuildMenus(viewModel);

        OpenScreens.CollectionChanged += (_, _) => NotifyOpenScreens();

        // Going to a module (from the tree, a card, the back button or search) starts its page at the top; a kind of work scrolls to its section.
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.SelectedModule))
            {
                PageScroller.ScrollToTop();
            }
        };
        viewModel.CategoryRequested += category =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                ModuleSections.UpdateLayout();
                if (ModuleSections.ItemContainerGenerator.ContainerFromItem(category) is FrameworkElement section)
                {
                    Point at = section.TranslatePoint(new Point(0, 0), PageScroller);
                    PageScroller.ScrollToVerticalOffset(PageScroller.VerticalOffset + at.Y - 8);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        FlowDirection = Localization.UiText.Direction;
        StartLicenceChecks(viewModel);
        _idleLock = new IdleLock(services);
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
    // before expiry — or while it is in grace, read-only or unusable — the user is reminded once a day (brief §11.5).
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

    // A screen opens in its own window (one already open is brought to the front) and is listed on the "Open screens" bar until it closes.
    private void OpenEntry(Navigation.NavEntry entry)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        if (OpenScreens.FirstOrDefault(o => ReferenceEquals(o.Entry, entry)) is OpenScreen already)
        {
            Bring(already.Window);
            return;
        }

        if (entry.Operation is Operations.Operation operation)
        {
            string area = Navigation.NavCategory.Kinds.First(k => k.Area == entry.Area).Title;
            OperationsWindow window = new(_services, vm.UserId, operation, entry.Title, $"{entry.ModuleTitle}  ›  {area}  ›  {entry.GroupTitle}") { Owner = this };
            Track(entry, window);
            window.Show();
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

    private void Entry_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Navigation.NavEntry entry)
        {
            OpenEntry(entry);
        }
    }

    // Choosing a module (or anything in it) in the tree shows that module's page; clicking a screen opens it.
    private void ModuleTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        switch (e.NewValue)
        {
            case Navigation.NavModule module:
                vm.OpenModuleCommand.Execute(module);
                break;
            case Navigation.NavCategory category:
                vm.OpenCategoryCommand.Execute(category);
                break;
            case Navigation.NavEntry entry:
                vm.ShowPageOf(entry);
                break;
        }
    }

    // ---- Open screens: every screen window, listed on the bar; click a chip to bring it to the front, × to close it. ----
    private void Track(Navigation.NavEntry entry, Window window)
    {
        OpenScreen open = new(entry, window);
        OpenScreens.Add(open);
        window.Closed += (_, _) => OpenScreens.Remove(open);
    }

    private static void Bring(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private void NotifyOpenScreens()
    {
        // The bar shows while anything is open; a plain property, so the binding is told by hand.
        System.ComponentModel.PropertyChangedEventHandler? handler = PropertyChanged;
        handler?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasOpenScreens)));
    }

    /// <inheritdoc />
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void ShowScreen_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is OpenScreen open)
        {
            Bring(open.Window);
        }
    }

    private void CloseScreen_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is OpenScreen open)
        {
            open.Window.Close();
        }
    }

    private void CloseAllScreens_Click(object sender, RoutedEventArgs e)
    {
        foreach (OpenScreen open in OpenScreens.ToList())
        {
            open.Window.Close();
        }
    }

    private void ModuleTree_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null && at != ModuleTree; at = System.Windows.Media.VisualTreeHelper.GetParent(at))
        {
            if (at is System.Windows.Controls.TreeViewItem item)
            {
                if (item.DataContext is Navigation.NavEntry entry)
                {
                    OpenEntry(entry);
                }

                return;
            }
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

    // The search: typing lists matching screens under the box; Down moves into the list, Enter opens the first (or chosen) one, Escape clears it.
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
            ((System.Windows.Controls.ListBoxItem?)SearchList.ItemContainerGenerator.ContainerFromIndex(0))?.Focus();
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
        ShellViewModel vm = (ShellViewModel)DataContext;
        vm.ShowPageOf(entry);
        vm.SearchText = string.Empty;
        OpenEntry(entry);
    }

    private void HowTo_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Three ways to find a screen:" + Environment.NewLine + Environment.NewLine
        + "1.  Type what you want to do in the search box at the top (for example \"add a product\", \"pay a supplier\", \"register an applicant\") and press Enter."
        + Environment.NewLine + Environment.NewLine
        + "2.  Click a module on the Home tab (or in the list on the left — the module you are in is lit). It shows four kinds of work: Maintenance (setting up), "
        + "Transactions (day-to-day work), Enquiries (looking things up) and Utilities (period end and housekeeping); choose one to see its screens. "
        + "The line at the top (Home › module › kind of work) shows where you are, and the back button goes up one level."
        + Environment.NewLine + Environment.NewLine
        + "3.  Use the menus: Modules ▸ module ▸ kind of work ▸ screen; or Maintenance / Transactions / Enquiries / Utilities ▸ module ▸ screen."
        + Environment.NewLine + Environment.NewLine
        + "Every screen opens in its own window, so you can work on several at once. The \"Open screens\" bar at the top of the page lists them: "
        + "click one to bring it to the front, or ✕ to close it.",
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
