using System.Windows;
using System.Windows.Controls;

namespace One4All.Desktop.Navigation;

/// <summary>
/// A module's page in the Explorer: "Related Tasks" (its everyday screens) on the left, and its kinds of work side by side on the right —
/// Maintenance, Transactions, Enquiries, Utilities — each listing its screens as tiles.
/// </summary>
public partial class ModulePage : UserControl
{
    /// <summary>Creates the page.</summary>
    /// <param name="module">The module.</param>
    public ModulePage(NavModule module)
    {
        Module = module;
        RelatedTasks = [.. module.Categories.Where(c => c.Area == Application.Navigation.MenuArea.Transactions).SelectMany(c => c.Entries).Take(4)];
        if (RelatedTasks.Count == 0)
        {
            RelatedTasks = [.. module.Entries.Take(4)];
        }

        DataContext = this;
        InitializeComponent();
    }

    /// <summary>Gets the module.</summary>
    public NavModule Module { get; }

    /// <summary>Gets the everyday screens shown first.</summary>
    public IReadOnlyList<NavEntry> RelatedTasks { get; }

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
