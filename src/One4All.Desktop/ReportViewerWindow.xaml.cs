using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The report viewer window: pick a catalogue report, fill its parameters, Run to preview the HTML, and Save
/// to export (HTML/PDF/CSV/text). It renders through the same <c>IReportRunner</c> the server uses.
/// </summary>
public partial class ReportViewerWindow : Window
{
    private readonly ReportViewerViewModel _viewModel;

    /// <summary>Creates the window against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public ReportViewerWindow(IServiceProvider services)
    {
        _viewModel = new ReportViewerViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += async (_, _) =>
        {
            await _viewModel.LoadAsync().ConfigureAwait(true);
            ShowPreview();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportViewerViewModel.PreviewHtml))
        {
            ShowPreview();
        }
    }

    private void ShowPreview()
    {
        if (!string.IsNullOrEmpty(_viewModel.PreviewHtml))
        {
            Preview.NavigateToString(_viewModel.PreviewHtml);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedReport is null)
        {
            MessageBox.Show(this, "Select a report first.", "Report Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ReportFormat format = _viewModel.SelectedFormat;
        Result<RenderedDocument> result = await _viewModel.RenderForExportAsync(format).ConfigureAwait(true);
        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Report Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RenderedDocument document = result.Value;
        SaveFileDialog dialog = new()
        {
            FileName = document.FileName,
            Filter = FilterFor(format),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await File.WriteAllBytesAsync(dialog.FileName, document.Content).ConfigureAwait(true);
        _viewModel.StatusMessage = $"Saved {Path.GetFileName(dialog.FileName)} ({document.Content.Length:N0} bytes).";
    }

    private static string FilterFor(ReportFormat format) => format switch
    {
        ReportFormat.Pdf => "PDF document (*.pdf)|*.pdf",
        ReportFormat.Xlsx => "Excel workbook (*.xlsx)|*.xlsx",
        ReportFormat.Csv => "CSV file (*.csv)|*.csv",
        ReportFormat.Text => "Text file (*.txt)|*.txt",
        _ => "HTML file (*.html)|*.html",
    };

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
