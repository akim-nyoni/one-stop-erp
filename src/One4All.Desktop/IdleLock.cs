using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;

namespace One4All.Desktop;

/// <summary>
/// Locks the desktop after a spell without keyboard or mouse input (brief §5.2), and keeps the server's idea of the session alive while
/// the user works: every couple of minutes with input the session is touched, and a session the server has closed locks at once.
/// The idle time comes from <c>ONE4ALL_LOCK_MINUTES</c> (default 10; 0 turns locking off).
/// </summary>
public sealed class IdleLock
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(2);
    private readonly IServiceProvider _services;
    private readonly DesktopSession _session;
    private readonly TimeSpan? _lockAfter;
    private readonly DispatcherTimer _timer;
    private DateTime _lastInput = DateTime.UtcNow;
    private DateTime _lastTouch = DateTime.UtcNow;
    private bool _locked;

    /// <summary>Creates the monitor and starts watching input.</summary>
    /// <param name="services">The application service provider.</param>
    public IdleLock(IServiceProvider services)
    {
        _services = services;
        _session = services.GetRequiredService<DesktopSession>();
        _lockAfter = LockAfter(Environment.GetEnvironmentVariable("ONE4ALL_LOCK_MINUTES"));
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs or MouseWheelEventArgs
                || (e.StagingItem.Input is MouseEventArgs && e.StagingItem.Input.RoutedEvent == Mouse.MouseMoveEvent))
            {
                _lastInput = DateTime.UtcNow;
            }
        };
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, async (_, _) => await TickAsync().ConfigureAwait(true), Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    /// <summary>Reads the idle minutes setting: a whole number of minutes, 0 for never, anything else the default of ten.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>How long before locking, or null for never.</returns>
    public static TimeSpan? LockAfter(string? setting)
        => int.TryParse(setting, out int minutes) && minutes >= 0 ? (minutes == 0 ? null : TimeSpan.FromMinutes(minutes)) : TimeSpan.FromMinutes(10);

    /// <summary>Locks now (the user stepping away).</summary>
    /// <param name="reason">Why.</param>
    public void Lock(string reason = "locked by you")
    {
        if (_locked || _session.SessionId is not string sessionId)
        {
            return;
        }

        _locked = true;
        List<Window> shown = [.. System.Windows.Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible)];
        shown.ForEach(w => w.Hide());
        LockWindow screen = new(_services, sessionId, _session.UserName, reason);
        screen.ShowDialog();
        if (screen.SignedOut)
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }

        shown.ForEach(w => w.Show());
        _lastInput = _lastTouch = DateTime.UtcNow;
        _locked = false;
    }

    private async Task TickAsync()
    {
        if (_locked || _session.SessionId is not string sessionId)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (_lockAfter is TimeSpan after && now - _lastInput >= after)
        {
            Lock($"locked after {after.TotalMinutes:0} minutes idle");
            return;
        }

        if (_lastInput > _lastTouch && now - _lastTouch >= Heartbeat)
        {
            _lastTouch = now;
            try
            {
                await using AsyncServiceScope scope = _services.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().TouchSessionAsync(sessionId).ConfigureAwait(true))
                {
                    Lock("the session was closed");
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A database blip must not lock anyone out; the next heartbeat tries again.
            }
        }
    }
}
