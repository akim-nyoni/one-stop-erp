using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A stage's ageing (pre-formatted for display).</summary>
/// <param name="Stage">The stage.</param>
/// <param name="Count">Applications at it.</param>
/// <param name="AverageDays">Average days at it.</param>
/// <param name="OldestDays">Longest at it.</param>
public sealed record StageAgeingVm(string Stage, int Count, int AverageDays, int OldestDays);

/// <summary>
/// Backs the Title Deed Pipeline (brief §4.10.6): the pipeline as at a date - ageing by stage and every open application
/// with its days at the stage - and the next step for the selected application with its particulars. All rules live in
/// the tested <see cref="ITitleDeedService"/>.
/// </summary>
public sealed partial class TitlePipelineViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public TitlePipelineViewModel(IServiceProvider services) => _services = services;

    /// <summary>Ageing by stage.</summary>
    public ObservableCollection<StageAgeingVm> Stages { get; } = [];

    /// <summary>The open applications.</summary>
    public ObservableCollection<OpenTitle> Applications { get; } = [];

    /// <summary>The stages an application can move to.</summary>
    public IReadOnlyList<TitleStage> NextStages { get; } = [.. Enum.GetValues<TitleStage>().Where(s => s != TitleStage.Applied)];

    /// <summary>The date the pipeline is aged at.</summary>
    [ObservableProperty]
    private DateTime _asAt = DateTime.Today;

    /// <summary>The selected application.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private OpenTitle? _selected;

    /// <summary>The stage to move to.</summary>
    [ObservableProperty]
    private TitleStage _nextStage = TitleStage.ConveyancerAssigned;

    /// <summary>The conveyancer, the registry's query, the deed number or who signed - as the stage needs.</summary>
    [ObservableProperty]
    private string _particulars = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private bool HasSelection => Selected is not null;

    /// <summary>Ages the pipeline as at the date.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        if (_companyId == 0)
        {
            _companyId = await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        }

        TitlePipelineReport report = await scope.ServiceProvider.GetRequiredService<ITitleDeedService>().PipelineAsync(_companyId, DateOnly.FromDateTime(AsAt), ct).ConfigureAwait(true);
        Stages.Clear();
        foreach (StageAgeing stage in report.Stages)
        {
            Stages.Add(new StageAgeingVm(stage.Stage.ToString(), stage.Count, stage.AverageDays, stage.OldestDays));
        }

        string? keep = Selected?.ApplicationNumber;
        Applications.Clear();
        foreach (OpenTitle title in report.Applications)
        {
            Applications.Add(title);
        }

        Selected = Applications.FirstOrDefault(a => a.ApplicationNumber == keep);
        StatusMessage = $"{Applications.Count} open title application(s) as at {AsAt:dd MMM yyyy}.";
    }

    /// <summary>Moves the selected application to the chosen stage, today.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task AdvanceAsync(CancellationToken ct = default)
    {
        string number = Selected!.ApplicationNumber;
        string? text = string.IsNullOrWhiteSpace(Particulars) ? null : Particulars.Trim();
        TitleStepInput step = new()
        {
            Stage = NextStage, Date = DateOnly.FromDateTime(DateTime.Today),
            Conveyancer = NextStage == TitleStage.ConveyancerAssigned ? text : null,
            DeedNumber = NextStage == TitleStage.Registered ? text : null,
            ReceivedBy = NextStage == TitleStage.HandedOver ? text : null,
            Note = NextStage is TitleStage.QueryRaised or TitleStage.QueryResolved or TitleStage.Lodged or TitleStage.InCustody ? text : null,
        };

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result moved = await scope.ServiceProvider.GetRequiredService<ITitleDeedService>().AdvanceAsync(_companyId, number, step, ct).ConfigureAwait(true);
        StatusMessage = moved.IsSuccess ? $"{number} is now {NextStage}." : moved.Error.Message;
        if (moved.IsSuccess)
        {
            Particulars = string.Empty;
            await RefreshAsync(ct).ConfigureAwait(true);
        }
    }

    partial void OnAsAtChanged(DateTime value) => _ = RefreshAsync();
}
