using CommunityToolkit.Mvvm.ComponentModel;
using One4All.Application.Abstractions.Security;

namespace One4All.Desktop.ViewModels;

/// <summary>
/// Backs the login window. Password handling stays in the view (WPF <c>PasswordBox</c> is deliberately
/// not bindable), so the view calls <see cref="AttemptAsync"/> with the entered password.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;

    /// <summary>Creates the view model.</summary>
    /// <param name="authentication">The authentication service.</param>
    public LoginViewModel(IAuthenticationService authentication) => _authentication = authentication;

    /// <summary>The entered username.</summary>
    [ObservableProperty]
    private string _username = "admin";

    /// <summary>A status/error message for the login screen.</summary>
    [ObservableProperty]
    private string _statusMessage = "Enter your credentials to sign in.";

    /// <summary>The authenticator code, for users with two-factor sign-in.</summary>
    [ObservableProperty]
    private string _oneTimeCode = string.Empty;

    /// <summary>True while authenticating.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Attempts authentication with the entered username and the supplied password.</summary>
    /// <param name="password">The password from the view's PasswordBox.</param>
    /// <returns>The authentication result.</returns>
    public async Task<AuthenticationResult> AttemptAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(password))
        {
            StatusMessage = "Username and password are required.";
            return AuthenticationResult.Failure(AuthenticationOutcome.InvalidCredentials, StatusMessage);
        }

        IsBusy = true;
        StatusMessage = "Signing in…";
        try
        {
            AuthenticationResult result = await _authentication.AuthenticateWithCodeAsync(Username.Trim(), password, string.IsNullOrWhiteSpace(OneTimeCode) ? null : OneTimeCode.Trim(),
                Environment.MachineName).ConfigureAwait(true);
            StatusMessage = result.Message;
            return result;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sign-in failed: {ex.Message}";
            return AuthenticationResult.Failure(AuthenticationOutcome.InvalidCredentials, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
