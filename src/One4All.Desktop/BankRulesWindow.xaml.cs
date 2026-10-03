using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The bank rules screen. All behaviour lives in <see cref="BankRulesViewModel"/>; this code-behind only
/// constructs it and triggers the initial load.
/// </summary>
public partial class BankRulesWindow : Window
{
    private readonly BankRulesViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public BankRulesWindow(IServiceProvider services)
    {
        _viewModel = new BankRulesViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }
}
