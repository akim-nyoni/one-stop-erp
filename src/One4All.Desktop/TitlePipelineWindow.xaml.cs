using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The Title Deed Pipeline. All behaviour lives in <see cref="TitlePipelineViewModel"/>; this code-behind only constructs it
/// and ages the pipeline on load.
/// </summary>
public partial class TitlePipelineWindow : Window
{
    private readonly TitlePipelineViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public TitlePipelineWindow(IServiceProvider services)
    {
        _viewModel = new TitlePipelineViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
    }
}
