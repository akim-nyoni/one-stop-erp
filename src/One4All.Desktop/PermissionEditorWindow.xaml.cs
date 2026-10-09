using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using One4All.Core.Common;
using One4All.Core.Security;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>
/// The roles &amp; permissions editor: pick a role and tick, per feature, every operation it may perform, or pick a user and grant or deny
/// operations on top of their roles. Features are grouped by module. Grants are saved to the database; the server authorises against them.
/// </summary>
public partial class PermissionEditorWindow : Window
{
    private readonly PermissionGridViewModel _viewModel;

    /// <summary>Creates the window against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public PermissionEditorWindow(IServiceProvider services)
    {
        _viewModel = new PermissionGridViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();
        AddOperationColumns();
        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }

    // A column per operation flag, its header turned on its side. A role's cell is ticked or not; a user's is three-state (grant, deny, inherit).
    private void AddOperationColumns()
    {
        for (int i = 0; i < PermissionCells.Columns.Length; i++)
        {
            string cell = $"Cells[{i}]";
            DataTemplate template = (DataTemplate)XamlReader.Parse($$"""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Border Background="{Binding {{cell}}.Shade}" ToolTip="{Binding {{cell}}.Tip}">
                        <CheckBox IsChecked="{Binding {{cell}}.Value, UpdateSourceTrigger=PropertyChanged}" IsEnabled="{Binding {{cell}}.Offered}"
                                  IsThreeState="{Binding {{cell}}.ThreeState}" HorizontalAlignment="Center" Margin="2" />
                    </Border>
                </DataTemplate>
                """);
            Grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = new TextBlock { Text = Label(PermissionCells.Columns[i]), Style = (Style)FindResource("OpColumnHeader") },
                CellTemplate = template,
            });
        }
    }

    // "AuthoriseLevel1" → "Authorise level 1", "VoidReverse" → "Void reverse".
    private static string Label(OperationFlags flag)
    {
        string words = Regex.Replace(flag.ToString(), "(?<=[a-z])(?=[A-Z0-9])", " ");
        return words[..1] + words[1..].ToLowerInvariant();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Result result = await _viewModel.SaveAsync().ConfigureAwait(true);
        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Roles & Permissions", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
