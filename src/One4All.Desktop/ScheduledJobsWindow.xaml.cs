using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The Scheduled Jobs screen. All behaviour lives in <see cref="ScheduledJobsViewModel"/>; this code-behind only constructs
/// it and loads the jobs.
/// </summary>
public partial class ScheduledJobsWindow : Window
{
    private readonly ScheduledJobsViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public ScheduledJobsWindow(IServiceProvider services)
    {
        _viewModel = new ScheduledJobsViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
    }
}
