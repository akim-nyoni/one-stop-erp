using System.Windows;
using System.Windows.Controls;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The operations form — an action's boxes, its result and its list — shown in a tab of the main window or in a window of its own.
/// All behaviour lives in <see cref="OperationsViewModel"/>.
/// </summary>
public partial class OperationsView : UserControl
{
    /// <summary>Creates the view; the services come from <see cref="Services"/> before the record-history window is opened.</summary>
    public OperationsView()
    {
        InitializeComponent();

        // One action (opened from the navigation) has no list of actions beside it.
        DataContextChanged += (_, _) => ActionColumn.Width = DataContext is OperationsViewModel { IsSingle: true } ? new GridLength(0) : new GridLength(280);
    }

    /// <summary>Gets or sets the application service provider.</summary>
    public IServiceProvider? Services { get; set; }

    private OperationsListWindow? _list;

    /// <summary>Raised when OK saved and the form closed, with the message (shown by the list that opened the form).</summary>
    public event Action<string>? Completed;

    // OK runs the operation; a plain one-line success closes the form (the list refreshes itself), while a document, an enquiry or an error stays to be read.
    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OperationsViewModel model || !model.RunCommand.CanExecute(null))
        {
            return;
        }

        await model.RunCommand.ExecuteAsync(null);
        if (!model.Failed && model.ResultIsShort && model.Selected is { IsDocument: false, HasList: true })
        {
            Completed?.Invoke(model.Result);
            if (Window.GetWindow(this) is OperationsWindow host)
            {
                host.Close();
            }
            else
            {
                MessageBox.Show(Window.GetWindow(this), model.Result, model.SingleTitle ?? "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        else if (!model.Failed && model.ResultIsShort && model.Selected is { IsDocument: false, HasList: false })
        {
            MessageBox.Show(Window.GetWindow(this), model.Result, model.SingleTitle ?? "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            if (Window.GetWindow(this) is OperationsWindow host)
            {
                host.Close();
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is OperationsWindow host)
        {
            host.Close();
        }
    }

    // The list opens in its own window, one per form, and comes to the front if it is already open.
    private void ShowList_Click(object sender, RoutedEventArgs e)
    {
        if (Services is null || DataContext is not OperationsViewModel viewModel)
        {
            return;
        }

        if (_list is { IsLoaded: true })
        {
            _list.Activate();
            return;
        }

        _list = new OperationsListWindow(Services, viewModel) { Owner = Window.GetWindow(this) };
        _list.Closed += (_, _) => _list = null;
        _list.Show();
    }
}
