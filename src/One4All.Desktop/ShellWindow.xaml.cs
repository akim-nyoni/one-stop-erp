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
        FlowDirection = Localization.UiText.Direction;
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

    private void OpenPosTerminal_Click(object sender, RoutedEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        PosTerminalWindow terminal = new(_services, vm.UserId, vm.UserDisplayName) { Owner = this };
        terminal.Show();
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
