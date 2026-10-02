using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The period-close checklist screen. All behaviour lives in <see cref="PeriodCloseViewModel"/>; this
/// code-behind only constructs it and runs the checks for the default period end on load.
/// </summary>
public partial class PeriodCloseWindow : Window
{
    private readonly PeriodCloseViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public PeriodCloseWindow(IServiceProvider services)
    {
        _viewModel = new PeriodCloseViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.RunAsync().ConfigureAwait(true);
    }
}
