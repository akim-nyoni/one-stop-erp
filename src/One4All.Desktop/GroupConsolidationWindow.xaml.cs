using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The Group Consolidation enquiry. All behaviour lives in <see cref="GroupConsolidationViewModel"/>; this
/// code-behind constructs it and loads the companies on open.
/// </summary>
public partial class GroupConsolidationWindow : Window
{
    private readonly GroupConsolidationViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public GroupConsolidationWindow(IServiceProvider services)
    {
        _viewModel = new GroupConsolidationViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }
}
