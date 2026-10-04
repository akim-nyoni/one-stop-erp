using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A ticket on the board (pre-formatted for display).</summary>
/// <param name="Reference">The public reference.</param>
/// <param name="Category">The fault.</param>
/// <param name="Priority">The priority.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Response">The response target's state.</param>
/// <param name="Resolution">The resolution target's state.</param>
/// <param name="IsBreached">Whether the resolution target is breached (drives colouring).</param>
/// <param name="IsAtRisk">Whether the resolution target is at risk (drives colouring).</param>
public sealed record TicketVm(string Reference, string Category, string Priority, string Status, string Response, string Resolution, bool IsBreached, bool IsAtRisk);

/// <summary>
/// Backs the Fault Desk (brief §4.16): logs faults against a category and property, shows the SLA board, and lets the
/// signed-in officer take, resolve, close or reopen the selected ticket. All rules live in the tested
/// <see cref="IServiceDeskService"/>.
/// </summary>
public sealed partial class FaultDeskViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public FaultDeskViewModel(IServiceProvider services) => _services = services;

    /// <summary>The SLA board.</summary>
    public ObservableCollection<TicketVm> Tickets { get; } = [];

    /// <summary>The fault categories by code.</summary>
    public ObservableCollection<string> Categories { get; } = [];

    /// <summary>The priorities.</summary>
    public IReadOnlyList<TicketPriority> Priorities { get; } = Enum.GetValues<TicketPriority>();

    /// <summary>The selected ticket.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakeCommand), nameof(ResolveCommand), nameof(CloseCommand), nameof(ReopenCommand))]
    private TicketVm? _selected;

    /// <summary>The new fault's category.</summary>
    [ObservableProperty]
    private string? _category;

    /// <summary>The new fault's stand number (optional).</summary>
    [ObservableProperty]
    private string _standNumber = string.Empty;

    /// <summary>The new fault's priority.</summary>
    [ObservableProperty]
    private TicketPriority _priority = TicketPriority.Normal;

    /// <summary>Who reports it.</summary>
    [ObservableProperty]
    private string _reporterName = string.Empty;

    /// <summary>How to reach them.</summary>
    [ObservableProperty]
    private string _reporterContact = string.Empty;

    /// <summary>What is wrong, and where.</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>The resolution or reopening note for the selected ticket.</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>The board summary.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private bool HasSelection => Selected is not null;

    /// <summary>Loads the categories and the board as at now.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        if (_companyId == 0)
        {
            _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        }

        List<string> codes = await db.Set<TicketCategory>().Where(c => c.CompanyId == _companyId).OrderBy(c => c.Code).Select(c => c.Code).ToListAsync(ct).ConfigureAwait(true);
        Categories.Clear();
        foreach (string code in codes)
        {
            Categories.Add(code);
        }

        Category ??= Categories.FirstOrDefault();
        SlaBoard board = await scope.ServiceProvider.GetRequiredService<IServiceDeskService>().SlaBoardAsync(_companyId, DateTimeOffset.UtcNow, ct).ConfigureAwait(true);
        string? keep = Selected?.Reference;
        Tickets.Clear();
        foreach (TicketSla t in board.Tickets.Where(t => t.Status != TicketStatus.Closed))
        {
            Tickets.Add(new TicketVm(t.Reference, t.Category, t.Priority.ToString(), t.Status.ToString(), t.Response.ToString(), t.Resolution.ToString(),
                t.Resolution == SlaState.Breached, t.Resolution == SlaState.AtRisk));
        }

        Selected = Tickets.FirstOrDefault(t => t.Reference == keep);
        Summary = $"{Tickets.Count} open ticket(s); {board.ResolutionBreaches} breached in all; average {board.AverageHoursToResolve:0.#} h to resolve.";
    }

    /// <summary>Logs the new fault.</summary>
    [RelayCommand]
    public async Task LogAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<string> logged = await scope.ServiceProvider.GetRequiredService<IServiceDeskService>().LogAsync(new FaultReport
        {
            CompanyId = _companyId, CategoryCode = Category ?? string.Empty, StandNumber = string.IsNullOrWhiteSpace(StandNumber) ? null : StandNumber, Priority = Priority,
            ReporterName = ReporterName, ReporterContact = ReporterContact, Description = Description, ReportedAt = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(true);
        if (logged.IsFailure)
        {
            StatusMessage = logged.Error.Message;
            return;
        }

        StatusMessage = $"Logged {logged.Value} — give the reporter this reference.";
        (StandNumber, ReporterName, ReporterContact, Description) = (string.Empty, string.Empty, string.Empty, string.Empty);
        await RefreshAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Assigns the selected ticket to the signed-in officer.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public Task TakeAsync(CancellationToken ct = default) => ActAsync((desk, reference, now) =>
        _services.GetService<DesktopSession>()?.UserId is long user
            ? desk.AssignAsync(_companyId, reference, user, now, "Taken at the fault desk", ct)
            : Task.FromResult(Result.Failure(Error.Rule("Svc.NotSignedIn", "Sign in to take a ticket."))), "assigned to you", ct);

    /// <summary>Records the fix on the selected ticket.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public Task ResolveAsync(CancellationToken ct = default) => ActAsync((desk, reference, now) => desk.ResolveAsync(_companyId, reference, Note, now, ct), "resolved", ct);

    /// <summary>Closes the selected ticket.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public Task CloseAsync(CancellationToken ct = default) => ActAsync((desk, reference, now) => desk.CloseAsync(_companyId, reference, now, ct), "closed", ct);

    /// <summary>Reopens the selected ticket.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public Task ReopenAsync(CancellationToken ct = default) => ActAsync((desk, reference, now) => desk.ReopenAsync(_companyId, reference, Note, now, ct), "reopened", ct);

    private async Task ActAsync(Func<IServiceDeskService, string, DateTimeOffset, Task<Result>> action, string done, CancellationToken ct)
    {
        if (Selected is null)
        {
            return;
        }

        string reference = Selected.Reference;
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result result = await action(scope.ServiceProvider.GetRequiredService<IServiceDeskService>(), reference, DateTimeOffset.UtcNow).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? $"{reference} {done}." : result.Error.Message;
        if (result.IsSuccess)
        {
            Note = string.Empty;
            await RefreshAsync(ct).ConfigureAwait(true);
        }
    }
}
