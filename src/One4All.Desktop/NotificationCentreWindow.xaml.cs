using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The Notification Centre. All behaviour lives in <see cref="NotificationCentreViewModel"/>; this code-behind only
/// constructs it and loads the signed-in user's notifications.
/// </summary>
public partial class NotificationCentreWindow : Window
{
    private readonly NotificationCentreViewModel _viewModel;

    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public NotificationCentreWindow(IServiceProvider services)
    {
        _viewModel = new NotificationCentreViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
    }
}
