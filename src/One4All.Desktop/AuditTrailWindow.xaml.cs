using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The audit trail screen. All behaviour lives in <see cref="AuditTrailViewModel"/>; this code-behind only
/// constructs it and runs the default search (the last week) on load.
/// </summary>
public partial class AuditTrailWindow : Window
{
    private readonly AuditTrailViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public AuditTrailWindow(IServiceProvider services)
    {
        _viewModel = new AuditTrailViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        _services = services;
        Loaded += async (_, _) => await _viewModel.SearchAsync().ConfigureAwait(true);
    }

    private readonly IServiceProvider _services;

    // The selected entry's record, or the record named in the filters.
    private void WhoTouched_Click(object sender, RoutedEventArgs e)
    {
        Application.Abstractions.Security.AuditEntryView? entry = _viewModel.SelectedEntry?.Entry;
        new RecordHistoryWindow(_services, entry?.EntityType ?? _viewModel.EntityType, entry?.EntityId ?? _viewModel.EntityId) { Owner = this }.Show();
    }

    // Exports what the filters select; the export is itself written to the audit log.
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        Application.Abstractions.Reporting.ReportFormat format = Enum.Parse<Application.Abstractions.Reporting.ReportFormat>((string)((FrameworkElement)sender).Tag);
        Core.Common.Result<Application.Abstractions.Reporting.RenderedDocument> exported = await _viewModel.ExportAsync(format).ConfigureAwait(true);
        if (exported.IsFailure)
        {
            MessageBox.Show(this, exported.Error.Message, "Audit Trail", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Microsoft.Win32.SaveFileDialog dialog = new() { FileName = exported.Value.FileName, Filter = $"{format} file|*{System.IO.Path.GetExtension(exported.Value.FileName)}" };
        if (dialog.ShowDialog(this) == true)
        {
            await System.IO.File.WriteAllBytesAsync(dialog.FileName, exported.Value.Content).ConfigureAwait(true);
        }
    }
}
