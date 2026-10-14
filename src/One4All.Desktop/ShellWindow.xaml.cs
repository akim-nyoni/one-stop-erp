using System.Windows;
using System.Windows.Input;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>The main shell window: menus, a search box, the module list and a home page of modules; each screen opens in its own window.</summary>
public partial class ShellWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IdleLock _idleLock;

    /// <summary>Gets the command that locks the session (Ctrl+L).</summary>
    public static RoutedCommand LockCommand { get; } = new(nameof(LockCommand), typeof(ShellWindow));

    /// <summary>Gets the command that puts the cursor in the search box (Ctrl+F).</summary>
    public static RoutedCommand SearchCommand { get; } = new(nameof(SearchCommand), typeof(ShellWindow));

    /// <summary>Creates the shell window bound to a session view model.</summary>
    /// <param name="viewModel">The shell view model.</param>
    /// <param name="services">The application service provider (used to open tool windows such as the report viewer).</param>
    public ShellWindow(ShellViewModel viewModel, IServiceProvider services)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();
        BuildMenus(viewModel);
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

    // The menus are built from the navigation: Modules ▸ module ▸ group ▸ screen, and each kind of work (Maintenance, Transactions, Enquiries,
    // Utilities) ▸ module ▸ (group ▸) screen, so a module's submenu only ever shows that module's screens.
    private void BuildMenus(ShellViewModel viewModel)
    {
        foreach (Navigation.NavModule module in viewModel.NavModules)
        {
            System.Windows.Controls.MenuItem moduleItem = new() { Header = $"{module.Glyph}  {Localization.UiText.T(module.Title)}" };
            foreach (Navigation.NavGroup group in module.Groups)
            {
                System.Windows.Controls.MenuItem groupItem = new() { Header = Localization.UiText.T(group.Title) };
                AddEntries(groupItem, group.Entries);
                moduleItem.Items.Add(groupItem);
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
                List<Navigation.NavGroup> groups = [.. module.Groups.Select(g => g with { Entries = [.. g.Entries.Where(e => e.Area == area)] }).Where(g => g.Entries.Count > 0)];
                if (groups.Count == 0)
                {
                    continue;
                }

                System.Windows.Controls.MenuItem moduleItem = new() { Header = $"{module.Glyph}  {Localization.UiText.T(module.Title)}" };
                if (groups.Count == 1 || groups.Sum(g => g.Entries.Count) <= 8)
                {
                    // Few screens: list them straight under the module.
                    AddEntries(moduleItem, groups.SelectMany(g => g.Entries));
                }
                else
                {
                    foreach (Navigation.NavGroup group in groups)
                    {
                        System.Windows.Controls.MenuItem groupItem = new() { Header = Localization.UiText.T(group.Title) };
                        AddEntries(groupItem, group.Entries);
                        moduleItem.Items.Add(groupItem);
                    }
                }

                menu.Items.Add(moduleItem);
            }
        }
    }

    private void AddEntries(System.Windows.Controls.MenuItem parent, IEnumerable<Navigation.NavEntry> entries)
    {
        foreach (Navigation.NavEntry entry in entries)
        {
            System.Windows.Controls.MenuItem item = new() { Header = Localization.UiText.T(entry.Title) + "…", ToolTip = entry.Purpose };
            item.Click += (_, _) => OpenEntry(entry);
            parent.Items.Add(item);
        }
    }

    // Every screen opens in its own window: an action in a window of its own (its form and its list), or its dedicated window.
    private void OpenEntry(Navigation.NavEntry entry)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        if (entry.Operation is Operations.Operation operation)
        {
            new OperationsWindow(_services, vm.UserId, operation, entry.Title) { Owner = this }.Show();
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
        Navigation.NavModule? module = e.NewValue switch
        {
            Navigation.NavModule m => m,
            Navigation.NavGroup g => vm.NavModules.FirstOrDefault(m => m.Groups.Contains(g)),
            Navigation.NavEntry entry => vm.NavModules.FirstOrDefault(m => m.Title == entry.ModuleTitle),
            _ => null,
        };
        if (module is not null)
        {
            vm.SelectedModule = module;
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
        vm.SelectedModule = vm.NavModules.FirstOrDefault(m => m.Title == entry.ModuleTitle);
        vm.SearchText = string.Empty;
        OpenEntry(entry);
    }

    private void HowTo_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Three ways to find a screen:" + Environment.NewLine + Environment.NewLine
        + "1.  Type what you want to do in the search box at the top (for example \"add a product\", \"pay a supplier\", \"register an applicant\") and press Enter."
        + Environment.NewLine + Environment.NewLine
        + "2.  Click a module on the home page (or in the list on the left). Its page shows only that module's screens, grouped by the work they are for."
        + Environment.NewLine + Environment.NewLine
        + "3.  Use the menus: Modules ▸ module ▸ group ▸ screen; or by the kind of work — Maintenance (setting up), Transactions (day-to-day work), "
        + "Enquiries (looking things up), Utilities (period end and housekeeping) — ▸ module ▸ screen."
        + Environment.NewLine + Environment.NewLine
        + "Every screen opens in its own window, so you can keep several open at once.",
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
