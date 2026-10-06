using System.Windows;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop;

/// <summary>
/// The signed-in user's own authenticator enrolment: start (a key and otpauth link to add to the app), confirm with a code to turn
/// two-factor on, or turn it off after re-entering the password and a current code.
/// </summary>
public partial class TwoFactorWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly DesktopSession _session;

    /// <summary>Creates the window.</summary>
    /// <param name="services">The application service provider.</param>
    public TwoFactorWindow(IServiceProvider services)
    {
        _services = services;
        _session = services.GetRequiredService<DesktopSession>();
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        bool on = await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Users.AsNoTracking()
            .Where(u => u.Id == _session.UserId).Select(u => u.TwoFactorEnabled).FirstOrDefaultAsync().ConfigureAwait(true);
        StatusText.Text = on
            ? $"Two-factor is ON for {_session.UserName}: signing in and unlocking need a code from your authenticator app."
            : $"Two-factor is OFF for {_session.UserName}: only the password protects this account.";
        EnrolGroup.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        DisableGroup.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<(string Secret, string ProvisioningUri)> started = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
            .BeginTwoFactorAsync(_session.UserId ?? 0).ConfigureAwait(true);
        if (started.IsFailure)
        {
            Say(started.Error.Message, false);
            return;
        }

        // Groups of four are easier to type into a phone.
        KeyBox.Text = string.Join(' ', started.Value.Secret.Chunk(4).Select(c => new string(c)));
        UriBox.Text = started.Value.ProvisioningUri;
        ConfirmButton.IsEnabled = true;
        ConfirmCodeBox.Focus();
        Say("Add the key to your app, then type the code it shows.", true);
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result confirmed = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
            .ConfirmTwoFactorAsync(_session.UserId ?? 0, ConfirmCodeBox.Text.Trim()).ConfigureAwait(true);
        ConfirmCodeBox.Clear();
        if (confirmed.IsFailure)
        {
            Say(confirmed.Error.Message, false);
            return;
        }

        KeyBox.Clear();
        UriBox.Clear();
        Say("Two-factor is on. Keep the app: you will need a code at every sign-in.", true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (_session.SessionId is not string sessionId)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        Result reauth = await auth.ReauthenticateAsync(sessionId, DisablePasswordBox.Password, DisableCodeBox.Text.Trim()).ConfigureAwait(true);
        DisablePasswordBox.Clear();
        DisableCodeBox.Clear();
        Result off = reauth.IsSuccess ? await auth.DisableTwoFactorAsync(_session.UserId ?? 0).ConfigureAwait(true) : reauth;
        Say(off.IsSuccess ? "Two-factor is off." : off.Error.Message, off.IsSuccess);
        await RefreshAsync().ConfigureAwait(true);
    }

    private void CopyKey_Click(object sender, RoutedEventArgs e) => Copy(KeyBox.Text.Replace(" ", string.Empty, StringComparison.Ordinal));

    private void CopyUri_Click(object sender, RoutedEventArgs e) => Copy(UriBox.Text);

    private void Copy(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
            Say("Copied.", true);
        }
    }

    private void Say(string message, bool good)
    {
        MessageText.Text = message;
        MessageText.Foreground = good ? Brushes.DarkGreen : Brushes.Firebrick;
    }
}
