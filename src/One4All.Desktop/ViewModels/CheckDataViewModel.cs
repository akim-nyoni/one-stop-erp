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

/// <summary>A check-data finding (pre-formatted for display).</summary>
/// <param name="Severity">"Error" or "Warning".</param>
/// <param name="Check">The check, in words.</param>
/// <param name="Subject">What it concerns.</param>
/// <param name="Expected">The recomputed value, or blank.</param>
/// <param name="Actual">The stored value, or blank.</param>
/// <param name="Detail">The explanation.</param>
/// <param name="IsError">Whether it is an error (drives row colouring).</param>
public sealed record IntegrityRowVm(string Severity, string Check, string Subject, string Expected, string Actual, string Detail, bool IsError);

/// <summary>
/// Backs the Check Data screen: runs <see cref="ILedgerIntegrityService.CheckAsync"/> for the company and lists every
/// finding, and offers the one safe repair - rebuilding the cached GL period balances from the posted lines. All rules
/// live in the tested service; this class formats the result.
/// </summary>
public sealed partial class CheckDataViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public CheckDataViewModel(IServiceProvider services) => _services = services;

    /// <summary>The findings.</summary>
    public ObservableCollection<IntegrityRowVm> Rows { get; } = [];

    /// <summary>The verdict banner.</summary>
    [ObservableProperty]
    private string _verdict = string.Empty;

    /// <summary>True when the last check found nothing.</summary>
    [ObservableProperty]
    private bool _isClean;

    /// <summary>What was checked.</summary>
    [ObservableProperty]
    private string _coverage = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Run the check to verify the ledgers agree with themselves.";

    /// <summary>True when the last check found period-balance differences (enables the rebuild).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildPeriodBalancesCommand))]
    private bool _hasPeriodBalanceIssues;

    /// <summary>True while running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(RebuildPeriodBalancesCommand))]
    private bool _isBusy;

    private bool CanRun => !IsBusy;

    private bool CanRebuild => HasPeriodBalanceIssues && !IsBusy;

    /// <summary>Runs every check.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task RunAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IServiceProvider sp = scope.ServiceProvider;
            if (!await EnsureCompanyAsync(sp, ct).ConfigureAwait(true))
            {
                return;
            }

            Result<LedgerIntegrityReport> result = await sp.GetRequiredService<ILedgerIntegrityService>().CheckAsync(_companyId, ct).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            Show(result.Value);
            StatusMessage = $"Checked {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not run the check: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Rebuilds the cached GL period balances from the posted journal lines, then re-runs the check.</summary>
    [RelayCommand(CanExecute = nameof(CanRebuild))]
    public async Task RebuildPeriodBalancesAsync()
    {
        IsBusy = true;
        string message;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<PeriodBalanceRebuildResult> result = await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>()
                .RebuildPeriodBalancesAsync(_companyId).ConfigureAwait(true);
            message = result.IsFailure
                ? result.Error.Message
                : $"Period balances rebuilt: {result.Value.Corrected} corrected, {result.Value.Created} created, {result.Value.Removed} removed.";
        }
        catch (Exception ex)
        {
            message = $"Rebuild failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await RunAsync().ConfigureAwait(true);
        StatusMessage = message;
    }

    private void Show(LedgerIntegrityReport report)
    {
        Rows.Clear();
        foreach (IntegrityIssue issue in report.Issues)
        {
            Rows.Add(new IntegrityRowVm(
                issue.Severity == IntegritySeverity.Error ? "Error" : "Warning",
                Describe(issue.Check),
                issue.Subject,
                Format(issue.Expected),
                Format(issue.Actual),
                issue.Detail,
                issue.Severity == IntegritySeverity.Error));
        }

        IsClean = report.IsClean;
        HasPeriodBalanceIssues = report.Issues.Any(i => i.Check == IntegrityCheck.PeriodBalance);
        Verdict = report.IsClean
            ? "Data is consistent: every check passed."
            : $"{report.ErrorCount} error(s) and {report.WarningCount} warning(s) found.";
        Coverage = $"Checked {report.JournalsChecked:N0} posted journals, {report.PeriodBalancesChecked:N0} period balances, {report.StockBalancesChecked:N0} stock balances and {report.OpenItemsChecked:N0} open items.";
    }

    private async Task<bool> EnsureCompanyAsync(IServiceProvider sp, CancellationToken ct)
    {
        if (_companyId != 0)
        {
            return true;
        }

        One4AllDbContext db = sp.GetRequiredService<One4AllDbContext>();
        _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        if (_companyId == 0)
        {
            StatusMessage = "No company is set up.";
            return false;
        }

        return true;
    }

    private static string Describe(IntegrityCheck check) => check switch
    {
        IntegrityCheck.UnbalancedJournal => "Unbalanced journal",
        IntegrityCheck.JournalNumberGap => "Journal number gap",
        IntegrityCheck.PeriodBalance => "Period balance",
        IntegrityCheck.StockQuantity => "Stock quantity",
        IntegrityCheck.StockValue => "Stock value",
        IntegrityCheck.OpenItemOutstanding => "Open item outstanding",
        _ => check.ToString(),
    };

    private static string Format(decimal? value) => value is { } v ? v.ToString("N2", CultureInfo.CurrentCulture) : string.Empty;
}
