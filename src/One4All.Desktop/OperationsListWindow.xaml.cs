using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// A screen's list in a window of its own (the customers, the invoices, the count sheets…), apart from the form that adds or changes them.
/// It shares the form's <see cref="OperationsViewModel"/>, so it refreshes when the form saves; opened on its own it loads the list at once.
/// </summary>
public partial class OperationsListWindow : Window
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the list window.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="viewModel">The screen's state (the list, its paging, the operation).</param>
    public OperationsListWindow(IServiceProvider services, OperationsViewModel viewModel)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (viewModel.List is null && viewModel.RefreshCommand.CanExecute(null))
            {
                viewModel.RefreshCommand.Execute(null);
            }
        };
    }

    // The selected row's Id (and the record type to choose) in the record-history window.
    private void WhoTouched_Click(object sender, RoutedEventArgs e)
    {
        string? id = ListGrid.SelectedItem is System.Data.DataRowView row && row.Row.Table.Columns.Contains("Id") ? Convert.ToString(row["Id"], System.Globalization.CultureInfo.InvariantCulture) : null;
        new RecordHistoryWindow(_services, null, id) { Owner = this }.Show();
    }

    // The list's column headers in the company's language (the table keeps its English column names for the code that reads them).
    private void ListGrid_AutoGeneratingColumn(object sender, System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e)
        => e.Column.Header = Localization.UiText.T(e.PropertyName);
}
