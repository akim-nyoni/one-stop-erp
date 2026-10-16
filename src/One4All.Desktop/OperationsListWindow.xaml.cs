using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>A screen's list in a window of its own (see <see cref="OperationsListView"/>), apart from the form that adds or changes the records.</summary>
public partial class OperationsListWindow : Window
{
    /// <summary>Creates the list window.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="viewModel">The screen's state (the list, its paging, the operation).</param>
    public OperationsListWindow(IServiceProvider services, OperationsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        View.Services = services;
    }
}
