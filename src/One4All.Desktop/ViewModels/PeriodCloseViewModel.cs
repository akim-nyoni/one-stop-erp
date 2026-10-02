using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A checklist row (pre-formatted for display).</summary>
/// <param name="Name">The check.</param>
/// <param name="Count">Outstanding items.</param>
/// <param name="Value">The value involved, or blank.</param>
/// <param name="Severity">"Blocks close" or "Warning".</param>
/// <param name="Result">"Pass" or "Outstanding".</param>
/// <param name="Detail">What was counted.</param>
/// <param name="Passes">Whether the check passes (drives row colouring).</param>
public sealed record PeriodCloseRowVm(string Name, int Count, string Value, string Severity, string Result, string Detail, bool Passes);

/// <summary>
/// Backs the period-close screen: runs the cross-module readiness checklist as at a chosen period end and
/// shows each check with its count, value and whether it blocks the close. Every rule lives in the tested
/// <see cref="IPeriodCloseService"/>; this class formats the result.
/// </summary>
public sealed partial class PeriodCloseViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public PeriodCloseViewModel(IServiceProvider services) => _services = services;

    /// <summary>The checks.</summary>
    public ObservableCollection<PeriodCloseRowVm> Rows { get; } = [];

    /// <summary>The period-end date the checks are measured at (defaults to the last day of last month).</summary>
    [ObservableProperty]
    private DateTime _periodEnd = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1);

    /// <summary>The verdict banner.</summary>
    [ObservableProperty]
    private string _verdict = string.Empty;

    /// <summary>True when the period may be closed (no blocking check outstanding).</summary>
    [ObservableProperty]
    private bool _isReady;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Choose a period end and run the checks.";

    /// <summary>True while running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isBusy;

    private bool CanRun => !IsBusy;

    /// <summary>Runs the checklist as at the period end.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task RunAsync(CancellationToken ct = default)
    {
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

            DateOnly asAt = DateOnly.FromDateTime(PeriodEnd);
            PeriodCloseChecklist list = await sp.GetRequiredService<IPeriodCloseService>().ChecklistAsync(_companyId, asAt, ct).ConfigureAwait(true);

            Rows.Clear();
            foreach (PeriodCloseCheck check in list.Checks)
            {
                Rows.Add(new PeriodCloseRowVm(
                    check.Name,
                    check.Count,
                    check.Value == 0m ? string.Empty : check.Value.ToString("N2", CultureInfo.CurrentCulture),
                    check.IsBlocking ? "Blocks close" : "Warning",
                    check.Passes ? "Pass" : "Outstanding",
                    check.Detail,
                    check.Passes));
            }

            IsReady = list.IsReadyToClose;
            Verdict = list.IsReadyToClose
                ? (list.Outstanding == 0 ? $"Ready to close {asAt:yyyy-MM-dd}: every check passes." : $"Ready to close {asAt:yyyy-MM-dd}: {list.Outstanding} warning(s) to review, nothing blocking.")
                : $"NOT ready to close {asAt:yyyy-MM-dd}: {list.Checks.Count(c => !c.Passes && c.IsBlocking)} blocking check(s) outstanding.";
            StatusMessage = $"Checked {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not run the checklist: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
