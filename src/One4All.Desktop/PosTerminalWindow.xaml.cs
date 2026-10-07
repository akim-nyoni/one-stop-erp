using System.IO;
using System.Windows;
using System.Windows.Media;
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

        _viewModel.SupervisorPinCleared += () => SupervisorPinBox.Clear();
        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
        FlowDirection = Localization.UiText.Direction;
        try
        {
            ContrastToggle.IsChecked = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\One4All\Pos")?.GetValue("HighContrast") is 1
                || SystemParameters.HighContrast;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            ContrastToggle.IsChecked = SystemParameters.HighContrast;
        }
    }

    private void SupervisorPin_Changed(object sender, RoutedEventArgs e) => _viewModel.SupervisorPin = SupervisorPinBox.Password;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // The high-contrast palette (brief §15 accessibility): black, white and yellow, every button outlined.
    private void Contrast_Changed(object sender, RoutedEventArgs e)
    {
        bool on = ContrastToggle.IsChecked == true;
        SetBrush("WindowBg", on ? Colors.Black : Color.FromRgb(0x1E, 0x27, 0x2E));
        SetBrush("Card", on ? Colors.Black : Color.FromRgb(0x1F, 0x2A, 0x33));
        SetBrush("Bar", on ? Colors.Black : Color.FromRgb(0x0F, 0x15, 0x19));
        SetBrush("Panel", on ? Colors.Black : Color.FromRgb(0x26, 0x31, 0x3A));
        SetBrush("Ink", on ? Colors.White : Colors.White);
        SetBrush("Muted", on ? Colors.Yellow : Color.FromRgb(0x9A, 0xA7, 0xB0));
        SetBrush("EdgeBrush", on ? Colors.Yellow : Colors.Transparent);
        Resources["Edge"] = new Thickness(on ? 3 : 0);
        try
        {
            Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\One4All\Pos")?.SetValue("HighContrast", on ? 1 : 0);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // The choice is just not remembered.
        }
    }

    private void SetBrush(string key, Color color) => Resources[key] = new SolidColorBrush(color);

    private async void PrintReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.LastReceiptText))
        {
            MessageBox.Show(this, "No receipt yet — complete a sale first.", "Receipt", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // A connected receipt printer gets it directly; otherwise it is saved as a file.
        if (await _viewModel.PrintLastReceiptAsync().ConfigureAwait(true) is null)
        {
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
