using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The account enquiry: nominal activity with running balance and journal drill-down, on to a journal line's account (double-click) and the
/// source document as it prints. The behaviour lives in <see cref="AccountEnquiryViewModel"/>; this code-behind constructs it, wires the
/// drill-through and passes on the double-click.
/// </summary>
public partial class AccountEnquiryWindow : Window
{
    private readonly AccountEnquiryViewModel _viewModel;

    /// <summary>Creates the enquiry against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="accountCode">An account to show straight away (a report drilling through to it).</param>
    public AccountEnquiryWindow(IServiceProvider services, string? accountCode = null)
    {
        _viewModel = new AccountEnquiryViewModel(services);
        _viewModel.OpenDrill = (target, key) => Reporting.DrillThroughOpener.OpenAsync(services, this, _viewModel.CompanyId, target, key);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            await _viewModel.InitialiseAsync().ConfigureAwait(true);
            if (accountCode is not null && _viewModel.Accounts.FirstOrDefault(a => a.Code == accountCode) is AccountChoiceVm account)
            {
                _viewModel.SelectedAccount = account;
                await _viewModel.LoadAsync().ConfigureAwait(true);
            }
        };
    }

    private async void JournalLine_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView { SelectedItem: JournalLineRowVm { AccountCode: string account } })
        {
            await _viewModel.ShowAccountAsync(account).ConfigureAwait(true);
        }
    }
}
