using System.IO;
using System.Windows;
using Microsoft.Win32;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The customer documents screen: invoices, credit notes and statements previewed as text and saved to a
/// file. All behaviour lives in <see cref="CustomerDocumentsViewModel"/>; this code-behind constructs it,
/// triggers the initial load and owns the save dialog.
/// </summary>
public partial class CustomerDocumentsWindow : Window
{
    private readonly CustomerDocumentsViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public CustomerDocumentsWindow(IServiceProvider services)
    {
        _viewModel = new CustomerDocumentsViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.PreviewText))
        {
            _viewModel.StatusMessage = "Preview a document first.";
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Save document",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = _viewModel.PreviewFileName,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await File.WriteAllTextAsync(dialog.FileName, _viewModel.PreviewText).ConfigureAwait(true);
            _viewModel.StatusMessage = $"Saved {dialog.FileName}.";
        }
    }
}
