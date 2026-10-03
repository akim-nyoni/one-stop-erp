using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The "Journals Awaiting Approval" screen: manual journals held for a second user, with approve (posts) and reject.
/// All behaviour lives in <see cref="JournalApprovalsViewModel"/>; this code-behind only constructs it and triggers the
/// initial load.
/// </summary>
public partial class JournalApprovalsWindow : Window
{
    private readonly JournalApprovalsViewModel _viewModel;

    /// <summary>Creates the screen for a signed-in user.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    public JournalApprovalsWindow(IServiceProvider services, long userId)
    {
        _viewModel = new JournalApprovalsViewModel(services, userId);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }
}
