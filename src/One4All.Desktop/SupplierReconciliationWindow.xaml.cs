using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The supplier statement reconciliation screen. All behaviour lives in
/// <see cref="SupplierReconciliationViewModel"/>; this code-behind only constructs it and loads the suppliers.
/// </summary>
public partial class SupplierReconciliationWindow : Window
{
    private readonly SupplierReconciliationViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public SupplierReconciliationWindow(IServiceProvider services)
    {
        _viewModel = new SupplierReconciliationViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.InitialiseAsync().ConfigureAwait(true);
    }
}
