using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The audit trail screen. All behaviour lives in <see cref="AuditTrailViewModel"/>; this code-behind only
/// constructs it and runs the default search (the last week) on load.
/// </summary>
public partial class AuditTrailWindow : Window
{
    private readonly AuditTrailViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public AuditTrailWindow(IServiceProvider services)
    {
        _viewModel = new AuditTrailViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.SearchAsync().ConfigureAwait(true);
    }
}
