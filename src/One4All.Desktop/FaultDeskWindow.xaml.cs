using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The Fault Desk. All behaviour lives in <see cref="FaultDeskViewModel"/>; this code-behind only constructs it and loads
/// the board.
/// </summary>
public partial class FaultDeskWindow : Window
{
    private readonly FaultDeskViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public FaultDeskWindow(IServiceProvider services)
    {
        _viewModel = new FaultDeskViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
    }
}
