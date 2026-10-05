using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The operations console: the day-to-day actions of a module (till events, stock movements, weighbridge, assets, contracts,
/// HR, banking, deliveries, wallets) and the document printer. All behaviour lives in <see cref="OperationsViewModel"/>.
/// </summary>
public partial class OperationsWindow : Window
{
    /// <summary>Creates the console.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="module">The module to open on.</param>
    public OperationsWindow(IServiceProvider services, long userId, string module)
    {
        DataContext = new OperationsViewModel(services, userId, module);
        InitializeComponent();
    }
}
