using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The analytics dashboard: working-capital ratios, today's cash position and the sales pipeline, read from
/// the tested read-side services. All behaviour lives in <see cref="AnalyticsViewModel"/>; this code-behind
/// only constructs it and triggers the initial load.
/// </summary>
public partial class AnalyticsWindow : Window
{
    private readonly AnalyticsViewModel _viewModel;

    /// <summary>Creates the dashboard against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public AnalyticsWindow(IServiceProvider services)
    {
        _viewModel = new AnalyticsViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }
}
