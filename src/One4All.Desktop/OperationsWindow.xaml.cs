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
    public OperationsWindow(IServiceProvider services, long userId, string module)
    {
        _services = services;
        DataContext = new OperationsViewModel(services, userId, module);
        InitializeComponent();
    }

    // The selected row's Id (and the record type to choose) in the record-history window.
    private void WhoTouched_Click(object sender, RoutedEventArgs e)
    {
        string? id = ListGrid.SelectedItem is System.Data.DataRowView row && row.Row.Table.Columns.Contains("Id") ? Convert.ToString(row["Id"], System.Globalization.CultureInfo.InvariantCulture) : null;
        new RecordHistoryWindow(_services, null, id) { Owner = this }.Show();
    }
}
