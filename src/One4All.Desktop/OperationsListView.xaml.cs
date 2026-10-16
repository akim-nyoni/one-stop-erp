using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// A screen's list of records, Explorer style: a toolbar (Add…, Refresh, History, a search box), the grid, and how many records at the bottom.
/// Shown in a tab of the main window or in <see cref="OperationsListWindow"/>; Add… opens the screen's form in its own window, sharing the
/// view model so the list refreshes when the form saves.
/// </summary>
public partial class OperationsListView : UserControl
{
    private OperationsWindow? _form;

    /// <summary>Creates the view.</summary>
    public OperationsListView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is OperationsViewModel old)
            {
                old.PropertyChanged -= Model_PropertyChanged;
            }

            if (e.NewValue is OperationsViewModel model)
            {
                model.PropertyChanged += Model_PropertyChanged;
                if (model.List is null && model.RefreshCommand.CanExecute(null))
                {
                    model.RefreshCommand.Execute(null);
                }

                UpdateStatus();
            }
        };
    }

    /// <summary>Gets or sets the application service provider.</summary>
    public IServiceProvider? Services { get; set; }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationsViewModel.List))
        {
            ApplyFilter();
            UpdateStatus();
        }
    }

    // Add… opens the form window (one per list) and brings it to the front when it is already open.
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Services is null || DataContext is not OperationsViewModel model)
        {
            return;
        }

        if (_form is { IsLoaded: true })
        {
            _form.Activate();
            return;
        }

        _form = new OperationsWindow(Services, model) { Owner = Window.GetWindow(this) };
        _form.Closed += (_, _) => _form = null;
        _form.Show();
    }

    private void ListGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Add_Click(sender, e);

    // The search box filters the rows on every column.
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (DataContext is not OperationsViewModel { List: DataView view })
        {
            return;
        }

        string text = SearchBox.Text.Trim().Replace("'", "''");
        view.RowFilter = text.Length == 0 ? string.Empty
            : string.Join(" OR ", view.Table!.Columns.Cast<DataColumn>().Select(c => $"CONVERT([{c.ColumnName}], 'System.String') LIKE '%{text}%'"));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int count = DataContext is OperationsViewModel { List: DataView view } ? view.Count : 0;
        Status.Text = count == 1 ? "1 record" : $"{count} records";
    }

    // The selected row's Id (and the record type to choose) in the record-history window.
    private void WhoTouched_Click(object sender, RoutedEventArgs e)
    {
        if (Services is null)
        {
            return;
        }

        string? id = ListGrid.SelectedItem is DataRowView row && row.Row.Table.Columns.Contains("Id") ? Convert.ToString(row["Id"], System.Globalization.CultureInfo.InvariantCulture) : null;
        new RecordHistoryWindow(Services, null, id) { Owner = Window.GetWindow(this) }.Show();
    }

    // The list's column headers in the company's language (the table keeps its English column names for the code that reads them).
    private void ListGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        => e.Column.Header = Localization.UiText.T(e.PropertyName);
}
