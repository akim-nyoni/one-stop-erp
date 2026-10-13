using System.Windows;
using System.Windows.Threading;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The waiter's floor screen. All behaviour lives in <see cref="FloorPlanViewModel"/>; this code-behind constructs it, loads the plan and refreshes
/// it every 30 seconds while the window is open.
/// </summary>
public partial class FloorPlanWindow : Window
{
    private readonly FloorPlanViewModel _viewModel;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public FloorPlanWindow(IServiceProvider services)
    {
        _viewModel = new FloorPlanViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            await _viewModel.RefreshAsync().ConfigureAwait(true);
            _timer.Start();
        };
        _timer.Tick += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
        Closed += (_, _) => _timer.Stop();
    }
}
