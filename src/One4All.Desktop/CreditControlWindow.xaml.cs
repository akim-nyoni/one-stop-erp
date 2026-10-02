using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The credit-control screen: customer exposure with the review run and hand hold / release. All
/// behaviour lives in <see cref="CreditControlViewModel"/>; this code-behind only constructs it and
/// triggers the initial load.
/// </summary>
public partial class CreditControlWindow : Window
{
    private readonly CreditControlViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public CreditControlWindow(IServiceProvider services)
    {
        _viewModel = new CreditControlViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }
}
