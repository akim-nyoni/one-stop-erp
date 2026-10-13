using System.Windows;
using System.Windows.Input;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>The main shell window (Sage Evolution layout: menu, toolbar, System Tree, module grid).</summary>
public partial class ShellWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IdleLock _idleLock;

    /// <summary>Gets the command that locks the session (Ctrl+L).</summary>
    public static RoutedCommand LockCommand { get; } = new(nameof(LockCommand), typeof(ShellWindow));

    /// <summary>Creates the shell window bound to a session view model.</summary>
    /// <param name="viewModel">The shell view model.</param>
    /// <param name="services">The application service provider (used to open tool windows such as the report viewer).</param>
    public ShellWindow(ShellViewModel viewModel, IServiceProvider services)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();
        BuildAreaMenus(viewModel);
        FlowDirection = Localization.UiText.Direction;
        StartLicenceChecks(viewModel);
        _idleLock = new IdleLock(services);
        Loaded += (_, _) => HideUnavailableOperations(viewModel);
    }

    // Operation groups belong to modules: a group no licensed, permitted module owns is not offered (brief §10).
    private void HideUnavailableOperations(ShellViewModel viewModel)
    {
        foreach (System.Windows.Controls.MenuItem item in LogicalTreeHelperItems(MainMenu))
        {
            if (item.Tag is string group && group.Length > 0)
            {
                item.Visibility = viewModel.IsMenuEntryAvailable(group) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static IEnumerable<System.Windows.Controls.MenuItem> LogicalTreeHelperItems(System.Windows.Controls.ItemsControl parent)
    {
        foreach (System.Windows.Controls.MenuItem item in parent.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            yield return item;
            foreach (System.Windows.Controls.MenuItem child in LogicalTreeHelperItems(item))
            {
                yield return child;
            }
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

    // Each of Maintenance, Transactions, Enquiries and Utilities lists, after its own windows, the modules that have actions of that kind; each opens
    // the module's console showing just those actions (Sage style).
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

    private void BuildAreaMenus(ShellViewModel viewModel)
    {
        IReadOnlyList<Operations.Operation> all = Operations.OperationCatalogue.All();
        foreach ((System.Windows.Controls.MenuItem menu, Application.Navigation.MenuArea area) in new[]
        {
            (MaintenanceMenu, Application.Navigation.MenuArea.Maintenance), (TransactionsMenu, Application.Navigation.MenuArea.Transactions),
            (EnquiriesMenu, Application.Navigation.MenuArea.Enquiries), (UtilitiesMenu, Application.Navigation.MenuArea.Utilities),
        })
        {
            List<string> modules = [.. Operations.OperationCatalogue.Modules.Where(m => all.Any(o => o.Module == m && o.MenuArea == area) && viewModel.IsMenuEntryAvailable(m))];
            if (modules.Count == 0)
            {
                continue;
            }

            menu.Items.Add(new System.Windows.Controls.Separator());
            foreach (string module in modules)
            {
                System.Windows.Controls.MenuItem item = new() { Header = Localization.UiText.T(module) + "…", Tag = (module, area) };
                item.Click += OpenArea_Click;
                menu.Items.Add(item);
            }
        }
    }

    private void OpenArea_Click(object sender, RoutedEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        (string module, Application.Navigation.MenuArea area) = ((string, Application.Navigation.MenuArea))((FrameworkElement)sender).Tag;
        new OperationsWindow(_services, vm.UserId, module, area) { Owner = this }.Show();
    }

    private void OpenOperations_Click(object sender, RoutedEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        OperationsWindow operations = new(_services, vm.UserId, (string)((FrameworkElement)sender).Tag) { Owner = this };
        operations.Show();
    }

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
