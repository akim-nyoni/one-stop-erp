using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using One4All.Core.Common;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The report designer window: open a catalogue listing report, drag its columns to reorder, edit each
/// column's header/width/money flag, then save it as a company-scoped variant the viewer can run. Layout is
/// rebuilt through <c>SystemReports.Listing</c>, so a designed report renders exactly like a system one.
/// </summary>
public partial class ReportDesignerWindow : Window
{
    private readonly ReportDesignerViewModel _viewModel;
    private Point _dragStart;
    private DesignColumn? _dragItem;

    /// <summary>Creates the window against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public ReportDesignerWindow(IServiceProvider services)
    {
        _viewModel = new ReportDesignerViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }

    private void Block_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = (sender as ListBoxItem)?.DataContext as DesignColumn;
    }

    private void Block_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
        {
            return;
        }

        Vector moved = e.GetPosition(null) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _dragItem, DragDropEffects.Move);
    }

    private void Block_Drop(object sender, DragEventArgs e)
    {
        if (_dragItem is null || (sender as ListBoxItem)?.DataContext is not DesignColumn target)
        {
            return;
        }

        _viewModel.MoveColumn(_dragItem, _viewModel.Columns.IndexOf(target));
        _dragItem = null;
        e.Handled = true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Result result = await _viewModel.SaveAsync().ConfigureAwait(true);
        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Report Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(this, "Saved. Open it from the Report Viewer to run it.", "Report Designer", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
