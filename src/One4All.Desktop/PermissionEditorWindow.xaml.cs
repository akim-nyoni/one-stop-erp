using System.Windows;
using One4All.Core.Common;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The roles &amp; permissions editor: pick a role and tick, per feature, the operations it may perform. Grants
/// are applied through the <c>Role</c> aggregate and saved to the database; the server authorises against them.
/// </summary>
public partial class PermissionEditorWindow : Window
{
    private readonly PermissionGridViewModel _viewModel;

    /// <summary>Creates the window against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public PermissionEditorWindow(IServiceProvider services)
    {
        _viewModel = new PermissionGridViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Result result = await _viewModel.SaveAsync().ConfigureAwait(true);
        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Roles & Permissions", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
