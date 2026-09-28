using System.IO;
using System.Windows;
using Microsoft.Win32;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The full-screen touch POS terminal: a menu-item product grid, a running cart, a tender pad and X/Z
/// reads. All behaviour lives in <see cref="PosTerminalViewModel"/> (which wraps the tested
/// <c>PosTicket</c> engine); this code-behind only constructs the view model and triggers the initial load.
/// </summary>
public partial class PosTerminalWindow : Window
{
    private readonly PosTerminalViewModel _viewModel;

    /// <summary>Creates the terminal for the signed-in cashier.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cashierUserId">The signed-in cashier's user id.</param>
    /// <param name="cashierName">The cashier's display name.</param>
    public PosTerminalWindow(IServiceProvider services, long cashierUserId, string cashierName)
    {
        _viewModel = new PosTerminalViewModel(services, cashierUserId, cashierName);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void PrintReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.LastReceiptText))
        {
            MessageBox.Show(this, "No receipt yet — complete a sale first.", "Receipt", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveFileDialog dialog = new()
        {
            FileName = "receipt.txt",
            Filter = "Text file (*.txt)|*.txt",
        };
        if (dialog.ShowDialog(this) == true)
        {
            await File.WriteAllTextAsync(dialog.FileName, _viewModel.LastReceiptText).ConfigureAwait(false);
        }
    }
}
