using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The stock replenishment screen: reorder suggestions with editable quantities and a one-click purchase
/// order. All behaviour lives in <see cref="ReplenishmentViewModel"/>; this code-behind only constructs it
/// and triggers the initial load.
/// </summary>
public partial class ReplenishmentWindow : Window
{
    private readonly ReplenishmentViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public ReplenishmentWindow(IServiceProvider services)
    {
        _viewModel = new ReplenishmentViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }
}
