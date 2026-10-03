using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The bank reconciliation screen. All behaviour lives in <see cref="BankReconciliationViewModel"/>; this
/// code-behind only constructs it and loads the bank accounts.
/// </summary>
public partial class BankReconciliationWindow : Window
{
    private readonly BankReconciliationViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public BankReconciliationWindow(IServiceProvider services)
    {
        _viewModel = new BankReconciliationViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.InitialiseAsync().ConfigureAwait(true);
    }
}
