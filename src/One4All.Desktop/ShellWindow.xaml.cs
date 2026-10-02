using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>The main shell window (Sage Evolution layout: menu, toolbar, System Tree, module grid).</summary>
public partial class ShellWindow : Window
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the shell window bound to a session view model.</summary>
    /// <param name="viewModel">The shell view model.</param>
    /// <param name="services">The application service provider (used to open tool windows such as the report viewer).</param>
    public ShellWindow(ShellViewModel viewModel, IServiceProvider services)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

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

    private void OpenApprovals_Click(object sender, RoutedEventArgs e)
    {
        ShellViewModel vm = (ShellViewModel)DataContext;
        ApprovalsWindow approvals = new(_services, vm.UserId) { Owner = this };
        approvals.Show();
    }
}
