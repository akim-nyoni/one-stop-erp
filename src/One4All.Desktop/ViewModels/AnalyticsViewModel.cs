using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Crm;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A pipeline stage row for the dashboard (pre-formatted for display).</summary>
/// <param name="Stage">The stage name.</param>
/// <param name="Count">The number of open opportunities.</param>
/// <param name="Estimated">The estimated value.</param>
/// <param name="Weighted">The probability-weighted value.</param>
public sealed record PipelineRowVm(string Stage, int Count, string Estimated, string Weighted);

/// <summary>A bank-account row for the dashboard (pre-formatted for display).</summary>
/// <param name="Account">The bank account code and name.</param>
/// <param name="Balance">The cash-book balance.</param>
/// <param name="Unreconciled">Unreconciled statement lines.</param>
public sealed record CashRowVm(string Account, string Balance, int Unreconciled);

/// <summary>
/// Backs the analytics dashboard: a one-glance view of the working-capital ratios (DSO/DPO/DIO and the cash
/// conversion cycle), today's cash position across the bank accounts, and the CRM pipeline forecast and
/// win-rate. It only reads — every figure comes from the already-tested read-side services, this class just
/// formats them for the screen.
/// </summary>
public sealed partial class AnalyticsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per refresh).</param>
    public AnalyticsViewModel(IServiceProvider services) => _services = services;

    /// <summary>The bank accounts on the cash position.</summary>
    public ObservableCollection<CashRowVm> CashRows { get; } = [];

    /// <summary>The open pipeline by stage.</summary>
    public ObservableCollection<PipelineRowVm> PipelineRows { get; } = [];

    /// <summary>The period the working-capital ratios cover.</summary>
    [ObservableProperty]
    private string _periodLabel = string.Empty;

    /// <summary>Days sales outstanding.</summary>
    [ObservableProperty]
    private string _dso = "–";

    /// <summary>Days payable outstanding.</summary>
    [ObservableProperty]
    private string _dpo = "–";

    /// <summary>Days inventory outstanding.</summary>
    [ObservableProperty]
    private string _dio = "–";

    /// <summary>The cash conversion cycle.</summary>
    [ObservableProperty]
    private string _ccc = "–";

    /// <summary>Receivables balance.</summary>
    [ObservableProperty]
    private string _receivables = "–";

    /// <summary>Payables balance.</summary>
    [ObservableProperty]
    private string _payables = "–";

    /// <summary>Inventory balance.</summary>
    [ObservableProperty]
    private string _inventory = "–";

    /// <summary>Total cash across the bank accounts.</summary>
    [ObservableProperty]
    private string _totalCash = "–";

    /// <summary>Total estimated open pipeline.</summary>
    [ObservableProperty]
    private string _pipelineEstimated = "–";

    /// <summary>Total weighted open pipeline (the forecast).</summary>
    [ObservableProperty]
    private string _pipelineWeighted = "–";

    /// <summary>Opportunities won / lost / win rate.</summary>
    [ObservableProperty]
    private string _conversion = "–";

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while refreshing.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Loads (or reloads) every figure on the dashboard.</summary>
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
            _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
            if (_companyId == 0)
            {
                StatusMessage = "No company is set up.";
                return;
            }

            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            DateOnly from = new(today.Year, 1, 1);
            PeriodLabel = $"YTD {from:yyyy-MM-dd} to {today:yyyy-MM-dd}";

            Result<WorkingCapitalMetrics> wc = await sp.GetRequiredService<IWorkingCapitalService>().ComputeAsync(_companyId, from, today, ct).ConfigureAwait(true);
            if (wc.IsSuccess)
            {
                WorkingCapitalMetrics m = wc.Value;
                Dso = Days(m.DaysSalesOutstanding);
                Dpo = Days(m.DaysPayableOutstanding);
                Dio = Days(m.DaysInventoryOutstanding);
                Ccc = Days(m.CashConversionCycle);
                Receivables = Money(m.ReceivablesBalance);
                Payables = Money(m.PayablesBalance);
                Inventory = Money(m.InventoryBalance);
            }

            Result<CashPositionReport> cash = await sp.GetRequiredService<IBankReconciliationService>().CashPositionAsync(_companyId, today, ct).ConfigureAwait(true);
            CashRows.Clear();
            if (cash.IsSuccess)
            {
                foreach (CashPositionRow row in cash.Value.Rows)
                {
                    CashRows.Add(new CashRowVm($"{row.Code} — {row.Name}", Money(row.Balance), row.UnreconciledLines));
                }

                TotalCash = Money(cash.Value.TotalBalance);
            }

            IOpportunityService crm = sp.GetRequiredService<IOpportunityService>();
            Result<PipelineForecast> pipeline = await crm.PipelineForecastAsync(_companyId, ct).ConfigureAwait(true);
            PipelineRows.Clear();
            if (pipeline.IsSuccess)
            {
                foreach (PipelineStageSummary stage in pipeline.Value.Stages)
                {
                    PipelineRows.Add(new PipelineRowVm(stage.Stage.ToString(), stage.Count, Money(stage.EstimatedValue), Money(stage.WeightedValue)));
                }

                PipelineEstimated = Money(pipeline.Value.TotalEstimated);
                PipelineWeighted = Money(pipeline.Value.TotalWeighted);
            }

            Result<OpportunityConversion> conversion = await crm.ConversionMetricsAsync(_companyId, ct).ConfigureAwait(true);
            if (conversion.IsSuccess)
            {
                OpportunityConversion c = conversion.Value;
                Conversion = $"Won {c.Won}  •  Lost {c.Lost}  •  Win rate {c.WinRatePercent.ToString("N1", CultureInfo.CurrentCulture)}%";
            }

            StatusMessage = $"Refreshed {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load analytics: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    private static string Days(decimal value) => value.ToString("N1", CultureInfo.CurrentCulture) + " d";
}
