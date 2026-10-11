using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Desktop.Reporting;
using One4All.Desktop.ViewModels;
using One4All.Reporting.Engine;

namespace One4All.Desktop;

/// <summary>
/// The report viewer window: pick a catalogue report, fill its parameters, Run to preview the HTML, and Save
/// to export (HTML/PDF/CSV/text). It renders through the same <c>IReportRunner</c> the server uses.
/// </summary>
public partial class ReportViewerWindow : Window
{
    private readonly ReportViewerViewModel _viewModel;
    private readonly IServiceProvider _services;
    private IReadOnlyList<LayoutPage> _pages = [];
    private int _lastHit = -1;

    /// <summary>Creates the window against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public ReportViewerWindow(IServiceProvider services)
    {
        _services = services;
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
        if (e.PropertyName == nameof(ReportViewerViewModel.Layout))
        {
            ShowPreview();
        }
    }

    private void ShowPreview()
    {
        _lastHit = -1;
        SearchResult.Text = string.Empty;
        if (_viewModel.Layout is not RenderModel layout)
        {
            Viewer.Document = null;
            _pages = [];
            return;
        }

        (FixedDocument document, _pages) = FixedReportDocument.Build(layout, Drill);
        Viewer.Document = document;
    }

    private async void Drill(string target, string key)
    {
        try
        {
            await DrillThroughOpener.OpenAsync(_services, this, _viewModel.CompanyId, target, key).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MessageBox.Show(this, $"Could not open {target} {key}: {ex.Message}", "Report Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Each Find moves to the next page holding the term.
    private void Find_Click(object sender, RoutedEventArgs e) => Find();

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            Find();
        }
    }

    private void Find()
    {
        IReadOnlyList<(int Page, string Text)> hits = ReportLayout.Search(_pages, SearchBox.Text);
        if (hits.Count == 0)
        {
            SearchResult.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? string.Empty : "Not found.";
            return;
        }

        _lastHit = (_lastHit + 1) % hits.Count;
        (int page, string text) = hits[_lastHit];
        Viewer.GoToPage(page);
        SearchResult.Text = $"{_lastHit + 1} of {hits.Count}: page {page} — {(text.Length > 40 ? text[..40] + "…" : text)}";
    }

    private void Print_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => Print_Click(sender, e);

    // Printer, tray (printer preferences), copies and page range come from the print dialog; the print itself is a fresh audited run, so
    // the run log has every print and numbered reprint.
    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedReport is null)
        {
            MessageBox.Show(this, "Select a report first.", "Report Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PrintDialog dialog = new() { UserPageRangeEnabled = true };
        await PrintPreferences.ApplyWorkstationAsync(dialog, _services, _viewModel.CompanyId, Core.System.PrintPurpose.Reports).ConfigureAwait(true);
        PrintPreferences.Apply(dialog, _viewModel.SelectedReport.DefinitionJson);
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Result<(RenderModel Layout, int? Copy)> run = await _viewModel.RenderForPrintAsync().ConfigureAwait(true);
        if (run.IsFailure)
        {
            MessageBox.Show(this, run.Error.Message, "Report Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RenderModel printed = run.Value.Layout;
        if (run.Value.Copy is int copy && copy > 1 && string.IsNullOrWhiteSpace(printed.Page.Watermark))
        {
            printed.Page.Watermark = $"COPY {copy}";
        }

        (FixedDocument document, _) = FixedReportDocument.Build(printed, drill: null);
        dialog.PrintDocument(document.DocumentPaginator, $"{_viewModel.SelectedReport.Code} {_viewModel.SelectedReport.Name}");
        _viewModel.StatusMessage = run.Value.Copy is int n && n > 1 ? $"Printed (reprint, copy {n})." : "Printed.";
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
        ReportFormat.Xlsx or ReportFormat.XlsxLayout => "Excel workbook (*.xlsx)|*.xlsx",
        ReportFormat.Csv or ReportFormat.CsvLayout => "CSV file (*.csv)|*.csv",
        ReportFormat.Docx => "Word document (*.docx)|*.docx",
        ReportFormat.Text => "Text file (*.txt)|*.txt",
        _ => "HTML file (*.html)|*.html",
    };

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
