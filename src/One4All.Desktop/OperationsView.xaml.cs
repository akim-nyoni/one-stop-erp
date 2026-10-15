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

    // The selected row's Id (and the record type to choose) in the record-history window.
    private void WhoTouched_Click(object sender, RoutedEventArgs e)
    {
        if (Services is null)
        {
            return;
        }

        string? id = ListGrid.SelectedItem is System.Data.DataRowView row && row.Row.Table.Columns.Contains("Id") ? Convert.ToString(row["Id"], System.Globalization.CultureInfo.InvariantCulture) : null;
        new RecordHistoryWindow(Services, null, id) { Owner = Window.GetWindow(this) }.Show();
    }

    // The list's column headers in the company's language (the table keeps its English column names for the code that reads them).
    private void ListGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        => e.Column.Header = Localization.UiText.T(e.PropertyName);
}
