using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A dashboard indicator (pre-formatted for display).</summary>
/// <param name="Name">The indicator, in words.</param>
/// <param name="Status">OK, Warning or Error.</param>
/// <param name="Count">What it found.</param>
/// <param name="Value">The money involved, or blank.</param>
/// <param name="Findings">The findings, for drill-down.</param>
/// <param name="IsError">Whether it is an error (drives colouring).</param>
/// <param name="IsWarning">Whether it is a warning (drives colouring).</param>
public sealed record HealthIndicatorVm(string Name, string Status, int Count, string Value, IReadOnlyList<string> Findings, bool IsError, bool IsWarning);

/// <summary>
/// Backs the Data Health dashboard (brief §4.17): takes <see cref="IDataHealthService.SnapshotAsync"/> as at today and
/// shows each indicator with its status, count and money, and the selected indicator's findings. All rules live in the
/// tested service; this class formats the result.
/// </summary>
public sealed partial class DataHealthViewModel : ObservableObject
{
    private static readonly Dictionary<HealthIndicatorKind, string> Names = new()
    {
        [HealthIndicatorKind.UnbalancedBatches] = "Unbalanced batches",
        [HealthIndicatorKind.ControlVariances] = "Control account variances",
        [HealthIndicatorKind.NegativeStock] = "Negative stock",
        [HealthIndicatorKind.UnpostedBatches] = "Unposted batches",
        [HealthIndicatorKind.OrphanRecords] = "Orphan records",
        [HealthIndicatorKind.LedgerIntegrity] = "Other ledger integrity",
    };

    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public DataHealthViewModel(IServiceProvider services) => _services = services;

    /// <summary>The indicators.</summary>
    public ObservableCollection<HealthIndicatorVm> Indicators { get; } = [];

    /// <summary>The selected indicator's findings.</summary>
    public ObservableCollection<string> Findings { get; } = [];

    /// <summary>The selected indicator.</summary>
    [ObservableProperty]
    private HealthIndicatorVm? _selectedIndicator;

    /// <summary>The overall verdict.</summary>
    [ObservableProperty]
    private string _verdict = string.Empty;

    /// <summary>The overall status: OK, Warning or Error.</summary>
    [ObservableProperty]
    private string _overall = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Refresh to take the company's data health.";

    /// <summary>True while running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    private bool CanRefresh => !IsBusy;

    /// <summary>Takes the dashboard as at today.</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
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

            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            Result<DataHealthReport> result = await sp.GetRequiredService<IDataHealthService>().SnapshotAsync(_companyId, today, ct).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            Show(result.Value);
            StatusMessage = $"Taken {DateTime.Now:HH:mm} - control accounts reconciled as at {today:dd MMM yyyy}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedIndicatorChanged(HealthIndicatorVm? value)
    {
        Findings.Clear();
        foreach (string finding in value?.Findings ?? [])
        {
            Findings.Add(finding);
        }
    }

    private void Show(DataHealthReport report)
    {
        Indicators.Clear();
        foreach (HealthIndicator indicator in report.Indicators)
        {
            Indicators.Add(new HealthIndicatorVm(Names[indicator.Kind], indicator.Status.ToString(), indicator.Count,
                indicator.Value == 0m ? string.Empty : indicator.Value.ToString("N2", System.Globalization.CultureInfo.CurrentCulture),
                indicator.Items, indicator.Status == HealthStatus.Error, indicator.Status == HealthStatus.Warning));
        }

        Overall = report.Overall.ToString();
        Verdict = report.Overall switch
        {
            HealthStatus.Ok => "Data is healthy: nothing needs attention.",
            HealthStatus.Warning => $"{report.Indicators.Count(i => i.Status == HealthStatus.Warning)} area(s) need attention.",
            _ => $"{report.Indicators.Count(i => i.Status == HealthStatus.Error)} area(s) hold errors that must be corrected.",
        };
        SelectedIndicator = Indicators.FirstOrDefault(i => i.IsError) ?? Indicators.FirstOrDefault(i => i.IsWarning);
    }
}
