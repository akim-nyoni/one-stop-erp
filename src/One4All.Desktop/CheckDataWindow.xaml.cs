using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The Check Data screen. All behaviour lives in <see cref="CheckDataViewModel"/>; this code-behind only
/// constructs it and runs the check on load.
/// </summary>
public partial class CheckDataWindow : Window
{
    private readonly CheckDataViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public CheckDataWindow(IServiceProvider services)
    {
        _viewModel = new CheckDataViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.RunAsync().ConfigureAwait(true);
    }
}
