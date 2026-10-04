using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A notification (pre-formatted for display).</summary>
/// <param name="Id">The notification.</param>
/// <param name="Title">The title.</param>
/// <param name="Body">The text.</param>
/// <param name="When">When, in local time.</param>
/// <param name="IsUnread">Whether it is unread (drives the bold title).</param>
public sealed record NotificationVm(long Id, string Title, string Body, string When, bool IsUnread);

/// <summary>
/// Backs the Notification Centre (brief §8 in-app notification centre): the signed-in user's notifications, newest first,
/// optionally only the unread ones, with mark-read. All rules live in the tested <see cref="INotificationService"/>.
/// </summary>
public sealed partial class NotificationCentreViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public NotificationCentreViewModel(IServiceProvider services) => _services = services;

    /// <summary>The notifications.</summary>
    public ObservableCollection<NotificationVm> Notifications { get; } = [];

    /// <summary>The selected notification.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MarkReadCommand))]
    private NotificationVm? _selected;

    /// <summary>Whether only unread notifications are shown.</summary>
    [ObservableProperty]
    private bool _unreadOnly = true;

    /// <summary>The unread count, for the header.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private bool CanMarkRead => Selected is { IsUnread: true };

    /// <summary>Loads the user's notifications.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        long? userId = _services.GetService<DesktopSession>()?.UserId;
        if (userId is null)
        {
            StatusMessage = "Sign in to see your notifications.";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        if (_companyId == 0)
        {
            _companyId = await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        }

        INotificationService service = scope.ServiceProvider.GetRequiredService<INotificationService>();
        IReadOnlyList<NotificationEntry> all = await service.InboxAsync(_companyId, userId.Value, unreadOnly: false, ct).ConfigureAwait(true);
        Notifications.Clear();
        foreach (NotificationEntry entry in all.Where(n => !UnreadOnly || !n.Read))
        {
            Notifications.Add(new NotificationVm(entry.Id, entry.Title, entry.Body, entry.CreatedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture), !entry.Read));
        }

        int unread = all.Count(n => !n.Read);
        Summary = unread == 0 ? "No unread notifications." : $"{unread} unread notification(s).";
        StatusMessage = $"{all.Count} notification(s) in all.";
    }

    /// <summary>Marks the selected notification read.</summary>
    [RelayCommand(CanExecute = nameof(CanMarkRead))]
    public async Task MarkReadAsync(CancellationToken ct = default)
    {
        long? userId = _services.GetService<DesktopSession>()?.UserId;
        if (Selected is null || userId is null)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result marked = await scope.ServiceProvider.GetRequiredService<INotificationService>().MarkReadAsync(_companyId, userId.Value, Selected.Id, ct).ConfigureAwait(true);
        if (marked.IsFailure)
        {
            StatusMessage = marked.Error.Message;
            return;
        }

        await RefreshAsync(ct).ConfigureAwait(true);
    }

    partial void OnUnreadOnlyChanged(bool value) => _ = RefreshAsync();
}
