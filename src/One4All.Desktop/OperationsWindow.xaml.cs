using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The operations console in a window of its own: a module's actions, or one action popped out of its tab. The form itself is <see cref="OperationsView"/>.
/// </summary>
public partial class OperationsWindow : Window
{
    /// <summary>Creates the console.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="module">The module to open on.</param>
    /// <param name="area">The menu area to show (null: all the module's actions).</param>
    public OperationsWindow(IServiceProvider services, long userId, string module, Application.Navigation.MenuArea? area = null)
        : this(services, new OperationsViewModel(services, userId, module, area))
    {
    }

    /// <summary>Opens one action in its own window.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="operation">The action.</param>
    /// <param name="title">The name the navigation shows for it.</param>
    /// <param name="context">Where it sits in the navigation.</param>
    public OperationsWindow(IServiceProvider services, long userId, Operations.Operation operation, string? title = null, string? context = null)
        : this(services, new OperationsViewModel(services, userId, operation, title, context))
    {
        Title = title ?? operation.Title;
        Width = 1000;
    }

    /// <summary>Shows an existing form (a tab popped out into its own window), keeping what was typed and its result.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="viewModel">The form's state.</param>
    public OperationsWindow(IServiceProvider services, OperationsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        View.Services = services;
        if (viewModel.IsSingle)
        {
            Title = viewModel.SingleTitle ?? Title;
            Width = 1000;
        }
    }
}
