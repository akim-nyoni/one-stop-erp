using System.IO;
using System.Windows;
using Microsoft.Win32;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The supplier payment run screen. All behaviour lives in <see cref="PaymentRunViewModel"/>; this code-behind
/// constructs it, loads the banks and owns the EFT save dialog.
/// </summary>
public partial class PaymentRunWindow : Window
{
    private readonly PaymentRunViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public PaymentRunWindow(IServiceProvider services)
    {
        _viewModel = new PaymentRunViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }

    private async void SaveEft_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.EftFile.Length == 0)
        {
            _viewModel.StatusMessage = "Run the payment run first.";
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Save EFT file",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = _viewModel.EftFileName,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await File.WriteAllBytesAsync(dialog.FileName, _viewModel.EftFile).ConfigureAwait(true);
            _viewModel.StatusMessage = $"Saved {dialog.FileName}.";
        }
    }
}
