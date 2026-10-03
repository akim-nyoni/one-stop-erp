using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A journal awaiting approval, pre-formatted for display.</summary>
/// <param name="JournalId">The journal.</param>
/// <param name="Number">Its number.</param>
/// <param name="Date">Its posting date.</param>
/// <param name="Narrative">Its narrative.</param>
/// <param name="Value">Its value.</param>
/// <param name="PreparedBy">Who prepared it.</param>
/// <param name="IsOwn">Whether the signed-in user prepared it, and so cannot approve it.</param>
public sealed record JournalApprovalRowVm(long JournalId, string Number, string Date, string Narrative, string Value, string PreparedBy, bool IsOwn);

/// <summary>
/// Backs the "Journals Awaiting Approval" screen: manual journals held for a second user's approval, with approve (which
/// posts) and reject (reason required). The maker-checker rules live in the tested <see cref="IJournalApprovalService"/>;
/// this class lists, formats and forwards the action.
/// </summary>
public sealed partial class JournalApprovalsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model for a signed-in user.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    /// <param name="userId">The signed-in user.</param>
    public JournalApprovalsViewModel(IServiceProvider services, long userId)
    {
        _services = services;
        UserId = userId;
    }

    /// <summary>The signed-in user.</summary>
    public long UserId { get; }

    /// <summary>The journals awaiting approval.</summary>
    public ObservableCollection<JournalApprovalRowVm> Items { get; } = [];

    /// <summary>The selected journal.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(RejectCommand))]
    private JournalApprovalRowVm? _selectedItem;

    /// <summary>Why the journal is rejected; required to reject.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RejectCommand))]
    private string _reason = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while loading or acting.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(RejectCommand))]
    private bool _isBusy;

    private bool CanApprove => SelectedItem is { IsOwn: false } && !IsBusy;

    private bool CanReject => SelectedItem is not null && !IsBusy && !string.IsNullOrWhiteSpace(Reason);

    /// <summary>Loads (or reloads) the journals awaiting approval.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IServiceProvider sp = scope.ServiceProvider;
            if (_companyId == 0)
            {
                _companyId = await sp.GetRequiredService<One4AllDbContext>().Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
                if (_companyId == 0)
                {
                    StatusMessage = "No company is set up.";
                    return;
                }
            }

            IReadOnlyList<JournalAwaitingApproval> held = await sp.GetRequiredService<IJournalApprovalService>().AwaitingApprovalAsync(_companyId, ct).ConfigureAwait(true);
            Items.Clear();
            foreach (JournalAwaitingApproval j in held)
            {
                bool own = j.PreparedByUserId == UserId;
                Items.Add(new JournalApprovalRowVm(
                    j.JournalId,
                    j.JournalNumber,
                    j.PostingDate.ToString("d", CultureInfo.CurrentCulture),
                    j.Narrative ?? string.Empty,
                    j.TotalBaseDebit.ToString("N2", CultureInfo.CurrentCulture),
                    own ? "You" : $"User {j.PreparedByUserId}",
                    own));
            }

            SelectedItem = null;
            StatusMessage = Items.Count == 0
                ? "No journals are awaiting approval."
                : $"{Items.Count} journal(s) awaiting approval. You cannot approve your own. Refreshed {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load journals: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Approves and posts the selected journal.</summary>
    /// <returns>A task.</returns>
    [RelayCommand(CanExecute = nameof(CanApprove))]
    public async Task ApproveAsync()
    {
        JournalApprovalRowVm? item = SelectedItem;
        if (item is null)
        {
            return;
        }

        await ActAsync(item, async service =>
        {
            Result<PostingReceipt> result = await service.ApproveAsync(_companyId, item.JournalId, UserId).ConfigureAwait(true);
            return result.IsSuccess ? $"{item.Number} approved and posted." : $"Approval failed: {result.Error.Message}";
        }).ConfigureAwait(true);
    }

    /// <summary>Rejects the selected journal with the reason given.</summary>
    /// <returns>A task.</returns>
    [RelayCommand(CanExecute = nameof(CanReject))]
    public async Task RejectAsync()
    {
        JournalApprovalRowVm? item = SelectedItem;
        if (item is null)
        {
            return;
        }

        string reason = Reason.Trim();
        await ActAsync(item, async service =>
        {
            Result result = await service.RejectAsync(_companyId, item.JournalId, reason).ConfigureAwait(true);
            if (result.IsFailure)
            {
                return $"Rejection failed: {result.Error.Message}";
            }

            Reason = string.Empty;
            return $"{item.Number} rejected; it will not post.";
        }).ConfigureAwait(true);
    }

    private async Task ActAsync(JournalApprovalRowVm item, Func<IJournalApprovalService, Task<string>> action)
    {
        IsBusy = true;
        string message;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            message = await action(scope.ServiceProvider.GetRequiredService<IJournalApprovalService>()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            message = $"{item.Number}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
        StatusMessage = message;
    }
}
