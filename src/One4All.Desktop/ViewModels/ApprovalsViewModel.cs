using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>An inbox row (pre-formatted for display).</summary>
/// <param name="InstanceId">The workflow instance.</param>
/// <param name="Document">The document type and reference, e.g. "PurchaseOrder PO-9".</param>
/// <param name="Value">The document value.</param>
/// <param name="Stage">The stage awaiting this user.</param>
/// <param name="Progress">Approvals so far against the quorum, e.g. "1 of 2".</param>
/// <param name="RaisedBy">The initiating user id.</param>
public sealed record ApprovalRowVm(long InstanceId, string Document, string Value, string Stage, string Progress, string RaisedBy);

/// <summary>
/// Backs the "My Approvals" inbox: the workflow instances waiting on the signed-in user, with approve /
/// reject / return actions. Eligibility and quorum rules live in the tested <see cref="IWorkflowService"/>;
/// this class only lists, formats, and forwards the chosen action.
/// </summary>
public sealed partial class ApprovalsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model for a signed-in user.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    /// <param name="userId">The signed-in user whose inbox this is.</param>
    public ApprovalsViewModel(IServiceProvider services, long userId)
    {
        _services = services;
        UserId = userId;
    }

    /// <summary>The signed-in user.</summary>
    public long UserId { get; }

    /// <summary>The items awaiting this user.</summary>
    public ObservableCollection<ApprovalRowVm> Items { get; } = [];

    /// <summary>The selected inbox row (actions apply to it).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(RejectCommand), nameof(ReturnCommand))]
    private ApprovalRowVm? _selectedItem;

    /// <summary>An optional comment recorded with the action.</summary>
    [ObservableProperty]
    private string _comment = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while loading or acting.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(RejectCommand), nameof(ReturnCommand))]
    private bool _isBusy;

    private bool CanAct => SelectedItem is not null && !IsBusy;

    /// <summary>Loads (or reloads) the inbox.</summary>
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
                One4AllDbContext db = sp.GetRequiredService<One4AllDbContext>();
                _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
                if (_companyId == 0)
                {
                    StatusMessage = "No company is set up.";
                    return;
                }
            }

            IReadOnlyList<PendingApproval> pending = await sp.GetRequiredService<IWorkflowService>()
                .PendingForUserAsync(_companyId, UserId, ct).ConfigureAwait(true);

            Items.Clear();
            foreach (PendingApproval p in pending)
            {
                Items.Add(new ApprovalRowVm(
                    p.InstanceId,
                    $"{p.DocumentType} {p.DocumentReference}",
                    p.Value.ToString("N2", CultureInfo.CurrentCulture),
                    $"{p.StageSequence}. {p.StageName}",
                    $"{p.ApprovalsSoFar} of {p.Quorum}",
                    $"User {p.InitiatedByUserId}"));
            }

            SelectedItem = null;
            StatusMessage = Items.Count == 0
                ? "Nothing is waiting for your approval."
                : $"{Items.Count} item(s) awaiting you - refreshed {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load approvals: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Approves the selected item.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    public Task ApproveAsync() => ActAsync(WorkflowActionType.Approve);

    /// <summary>Rejects the selected item.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    public Task RejectAsync() => ActAsync(WorkflowActionType.Reject);

    /// <summary>Returns the selected item to its originator.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    public Task ReturnAsync() => ActAsync(WorkflowActionType.Return);

    private async Task ActAsync(WorkflowActionType action)
    {
        ApprovalRowVm? item = SelectedItem;
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
            string? comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();

            Result<WorkflowStatus> result = await workflow.ActAsync(item.InstanceId, UserId, action, comment).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = $"{action} failed: {result.Error.Message}";
                return;
            }

            Comment = string.Empty;
            StatusMessage = $"{item.Document}: {Describe(action)} - workflow is now {result.Value}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{action} failed: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private static string Describe(WorkflowActionType action) => action switch
    {
        WorkflowActionType.Approve => "approved",
        WorkflowActionType.Reject => "rejected",
        WorkflowActionType.Return => "returned to originator",
        _ => action.ToString(),
    };
}
