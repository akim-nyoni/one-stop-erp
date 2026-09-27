using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>The login window. On success it opens the shell and closes itself.</summary>
public partial class LoginWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly LoginViewModel _viewModel;

    /// <summary>Creates the login window.</summary>
    /// <param name="services">The application service provider.</param>
    public LoginWindow(IServiceProvider services)
    {
        _services = services;
        _viewModel = services.GetRequiredService<LoginViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) => await SignInAsync();

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SignInAsync();
        }
    }

    private async Task SignInAsync()
    {
        SignInButton.IsEnabled = false;
        try
        {
            AuthenticationResult result = await _viewModel.AttemptAsync(PasswordBox.Password);
            if (!result.Succeeded)
            {
                return;
            }

            ILicenceService licence = _services.GetRequiredService<ILicenceService>();
            ShellWindow shell = new(new ShellViewModel(result, licence), _services);
            System.Windows.Application.Current.MainWindow = shell;
            shell.Show();
            Close();
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }
}
