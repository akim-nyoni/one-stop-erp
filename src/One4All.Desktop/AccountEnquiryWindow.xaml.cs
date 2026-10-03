using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The account enquiry: nominal activity with running balance and journal drill-down. All behaviour lives
/// in <see cref="AccountEnquiryViewModel"/>; this code-behind only constructs it and loads the accounts.
/// </summary>
public partial class AccountEnquiryWindow : Window
{
    private readonly AccountEnquiryViewModel _viewModel;

    /// <summary>Creates the enquiry against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public AccountEnquiryWindow(IServiceProvider services)
    {
        _viewModel = new AccountEnquiryViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.InitialiseAsync().ConfigureAwait(true);
    }
}
