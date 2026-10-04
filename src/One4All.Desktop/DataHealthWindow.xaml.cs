using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The Data Health dashboard. All behaviour lives in <see cref="DataHealthViewModel"/>; this code-behind only constructs it
/// and takes the dashboard on load.
/// </summary>
public partial class DataHealthWindow : Window
{
    private readonly DataHealthViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public DataHealthWindow(IServiceProvider services)
    {
        _viewModel = new DataHealthViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
    }
}
