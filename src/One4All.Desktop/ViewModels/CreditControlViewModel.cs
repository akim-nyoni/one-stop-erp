using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A customer row on the credit-control screen (pre-formatted for display).</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Code">The account reference.</param>
/// <param name="Name">The customer name.</param>
/// <param name="CreditLimit">The credit limit ("Unlimited" when none).</param>
/// <param name="Balance">The outstanding balance.</param>
/// <param name="Available">The remaining credit.</param>
/// <param name="Status">"On hold", "Over limit" or "OK".</param>
/// <param name="OnHold">Whether the account is on stop (drives which action button applies).</param>
public sealed record CreditRowVm(long CustomerId, string Code, string Name, string CreditLimit, string Balance, string Available, string Status, bool OnHold);

/// <summary>
/// Backs the credit-control screen: the exposure of every customer with a balance, limit or hold, the
/// periodic review that places over-limit / overdue customers on stop, and the hand hold and release.
/// Every rule lives in the tested <see cref="ICreditControlService"/>; this class lists and forwards.
/// </summary>
public sealed partial class CreditControlViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public CreditControlViewModel(IServiceProvider services) => _services = services;

    /// <summary>The customers on the exposure list.</summary>
    public ObservableCollection<CreditRowVm> Rows { get; } = [];

    /// <summary>The selected customer (hold / release apply to it).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HoldCommand), nameof(ReleaseCommand))]
    private CreditRowVm? _selectedRow;

    /// <summary>The reason recorded with a hold or release.</summary>
    [ObservableProperty]
    private string _reason = string.Empty;

    /// <summary>Days past due a debt may be before the review places the customer on stop.</summary>
    [ObservableProperty]
    private int _overdueToleranceDays = 14;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while loading or acting.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HoldCommand), nameof(ReleaseCommand), nameof(ReviewCommand))]
    private bool _isBusy;

    private bool CanHold => SelectedRow is { OnHold: false } && !IsBusy;

    private bool CanRelease => SelectedRow is { OnHold: true } && !IsBusy;

    private bool CanReview => !IsBusy;

    /// <summary>Loads (or reloads) the exposure list.</summary>
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
            One4AllDbContext db = sp.GetRequiredService<One4AllDbContext>();

            if (_companyId == 0)
            {
                _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
                if (_companyId == 0)
                {
                    StatusMessage = "No company is set up.";
                    return;
                }
            }

            CreditExposureReport report = await sp.GetRequiredService<ITradingReports>()
                .CreditExposureAsync(_companyId, DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(true);
            Dictionary<string, long> ids = await db.Customers
                .Where(c => c.CompanyId == _companyId)
                .ToDictionaryAsync(c => c.Code, c => c.Id, ct).ConfigureAwait(true);

            Rows.Clear();
            foreach (CreditExposureRow row in report.Rows)
            {
                string status = row.OnHold ? "On hold" : row.OverLimit ? "Over limit" : "OK";
                Rows.Add(new CreditRowVm(ids.GetValueOrDefault(row.CustomerCode), row.CustomerCode, row.CustomerName,
                    row.CreditLimit == 0m ? "Unlimited" : Money(row.CreditLimit), Money(row.Balance),
                    row.CreditLimit == 0m ? "–" : Money(row.Available), status, row.OnHold));
            }

            SelectedRow = null;
            StatusMessage = $"{Rows.Count} customer(s) with exposure — {Rows.Count(r => r.OnHold)} on hold. Refreshed {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load credit exposure: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs the credit review as at today with the current overdue tolerance.</summary>
    [RelayCommand(CanExecute = nameof(CanReview))]
    public async Task ReviewAsync()
    {
        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            ICreditControlService credit = scope.ServiceProvider.GetRequiredService<ICreditControlService>();
            Result<CreditReviewResult> result = await credit.ReviewAsync(_companyId, DateOnly.FromDateTime(DateTime.Today), OverdueToleranceDays).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = $"Review failed: {result.Error.Message}";
                return;
            }

            CreditReviewResult review = result.Value;
            StatusMessage = review.Held.Count == 0
                ? $"Review: {review.Reviewed} customer(s) examined, none placed on hold."
                : $"Review: {review.Reviewed} examined, {review.Held.Count} placed on hold — {string.Join(", ", review.Held.Select(h => $"{h.CustomerCode} ({h.Reason})"))}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Review failed: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Places the selected customer on hold.</summary>
    [RelayCommand(CanExecute = nameof(CanHold))]
    public Task HoldAsync() => ActAsync((credit, id, reason) => credit.HoldAsync(_companyId, id, reason), "placed on hold");

    /// <summary>Releases the selected customer from hold.</summary>
    [RelayCommand(CanExecute = nameof(CanRelease))]
    public Task ReleaseAsync() => ActAsync((credit, id, reason) => credit.ReleaseAsync(_companyId, id, reason), "released from hold");

    private async Task ActAsync(Func<ICreditControlService, long, string, Task<Result>> action, string done)
    {
        CreditRowVm? row = SelectedRow;
        if (row is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            ICreditControlService credit = scope.ServiceProvider.GetRequiredService<ICreditControlService>();
            Result result = await action(credit, row.CustomerId, Reason.Trim()).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = $"{row.Code}: {result.Error.Message}";
                return;
            }

            Reason = string.Empty;
            StatusMessage = $"{row.Code} {done}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{row.Code}: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);
}
