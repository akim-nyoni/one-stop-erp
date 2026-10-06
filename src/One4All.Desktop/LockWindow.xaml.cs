using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;

namespace One4All.Desktop;

/// <summary>
/// The lock screen shown over an idle (or deliberately locked) session: the same user's password, and authenticator code when two-factor
/// is on, unlocks it; too many misses end the session, leaving only signing out.
/// </summary>
public partial class LockWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly string _sessionId;
    private bool _released;

    /// <summary>Creates the lock screen.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="sessionId">The locked session.</param>
    /// <param name="userName">Who is signed in.</param>
    /// <param name="reason">Why it locked.</param>
    public LockWindow(IServiceProvider services, string sessionId, string? userName, string reason)
    {
        _services = services;
        _sessionId = sessionId;
        InitializeComponent();
        UserText.Text = $"{userName} — {reason}";
        Loaded += (_, _) => PasswordBox.Focus();
    }

    /// <summary>Gets a value indicating whether the user chose to sign out rather than unlock.</summary>
    public bool SignedOut { get; private set; }

    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        // Only unlocking or signing out leaves the lock screen (not Alt+F4).
        e.Cancel = !_released;
        base.OnClosing(e);
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e) => await UnlockAsync().ConfigureAwait(true);

    private async void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await UnlockAsync().ConfigureAwait(true);
        }
    }

    private async Task UnlockAsync()
    {
        UnlockButton.IsEnabled = false;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result result = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
                .ReauthenticateAsync(_sessionId, PasswordBox.Password, string.IsNullOrWhiteSpace(CodeBox.Text) ? null : CodeBox.Text.Trim()).ConfigureAwait(true);
            PasswordBox.Clear();
            CodeBox.Clear();
            if (result.IsSuccess)
            {
                _released = true;
                DialogResult = true;
                return;
            }

            MessageText.Text = result.Error.Message;
            if (result.Error.Code == "Security.SessionEnded")
            {
                PasswordBox.IsEnabled = false;
                CodeBox.IsEnabled = false;
                UnlockButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MessageText.Text = $"Could not check the password: {ex.Message}";
        }
        finally
        {
            UnlockButton.IsEnabled = true;
            PasswordBox.Focus();
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        SignedOut = true;
        _released = true;
        DialogResult = false;
    }
}
