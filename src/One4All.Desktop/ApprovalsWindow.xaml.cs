using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The "My Approvals" inbox: workflow items waiting on the signed-in user, with approve / reject / return.
/// All behaviour lives in <see cref="ApprovalsViewModel"/>; this code-behind only constructs it and
/// triggers the initial load.
/// </summary>
public partial class ApprovalsWindow : Window
{
    private readonly ApprovalsViewModel _viewModel;

    /// <summary>Creates the inbox for a signed-in user.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    public ApprovalsWindow(IServiceProvider services, long userId)
    {
        _viewModel = new ApprovalsViewModel(services, userId);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }
}
