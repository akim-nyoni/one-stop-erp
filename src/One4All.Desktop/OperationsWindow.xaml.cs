using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The operations console: the day-to-day actions of a module (till events, stock movements, weighbridge, assets, contracts,
/// HR, banking, deliveries, wallets) and the document printer. All behaviour lives in <see cref="OperationsViewModel"/>.
/// </summary>
public partial class OperationsWindow : Window
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the console.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="module">The module to open on.</param>
    /// <param name="area">The menu area to show (null: all the module's actions).</param>
    public OperationsWindow(IServiceProvider services, long userId, string module, Application.Navigation.MenuArea? area = null)
    {
        _services = services;
        DataContext = new OperationsViewModel(services, userId, module, area);
        InitializeComponent();
    }

    /// <summary>Opens one action in its own window (from the navigation).</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="operation">The action.</param>
    /// <param name="title">The name the navigation shows for it.</param>
    public OperationsWindow(IServiceProvider services, long userId, Operations.Operation operation, string? title = null)
    {
        _services = services;
        DataContext = new OperationsViewModel(services, userId, operation, title);
        InitializeComponent();
        Title = title ?? operation.Title;
        Width = 1000;
        ActionColumn.Width = new GridLength(0);
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
