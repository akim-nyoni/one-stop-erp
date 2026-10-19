using System.Windows;
using System.Windows.Controls;

namespace One4All.Desktop.Navigation;

/// <summary>
/// A module's page in the Explorer: what the module is for, then its kinds of work side by side - Maintenance, Transactions, Enquiries,
/// Utilities - each listing its screens as tiles.
/// </summary>
public partial class ModulePage : UserControl
{
    /// <summary>Creates the page.</summary>
    /// <param name="module">The module.</param>
    public ModulePage(NavModule module)
    {
        Module = module;
        DataContext = this;
        InitializeComponent();
    }

    /// <summary>Gets the module.</summary>
    public NavModule Module { get; }

    /// <summary>Raised when a screen tile is clicked.</summary>
    public event Action<NavEntry>? EntryOpened;

    /// <summary>Raised when a report tile is clicked, with the report code.</summary>
    public event Action<string>? ReportOpened;

    private void Report_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is NavReport report)
        {
            ReportOpened?.Invoke(report.Code);
        }
    }

    private void Entry_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is NavEntry entry)
        {
            EntryOpened?.Invoke(entry);
        }
    }
}
