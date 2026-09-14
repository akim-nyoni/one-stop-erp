using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>The main shell window (Sage Evolution layout: menu, toolbar, System Tree, module grid).</summary>
public partial class ShellWindow : Window
{
    /// <summary>Creates the shell window bound to a session view model.</summary>
    /// <param name="viewModel">The shell view model.</param>
    public ShellWindow(ShellViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
