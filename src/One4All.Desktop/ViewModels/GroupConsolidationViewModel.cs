using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A company that can be included in the consolidation.</summary>
public sealed partial class ConsolidationCompanyVm : ObservableObject
{
    /// <summary>Creates the row.</summary>
    /// <param name="id">The company.</param>
    /// <param name="label">Code, name and base currency.</param>
    public ConsolidationCompanyVm(long id, string label)
    {
        Id = id;
        Label = label;
    }

    /// <summary>The company.</summary>
    public long Id { get; }

    /// <summary>Code, name and base currency.</summary>
    public string Label { get; }

    /// <summary>Whether the company is included.</summary>
    [ObservableProperty]
    private bool _include = true;
}

/// <summary>A consolidated trial balance row (pre-formatted for display).</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="AccountName">Its name.</param>
/// <param name="Debit">The debit balance, or blank.</param>
/// <param name="Credit">The credit balance, or blank.</param>
/// <param name="IsReserve">True for the translation reserve row (drives emphasis).</param>
public sealed record ConsolidationRowVm(string AccountCode, string AccountName, string Debit, string Credit, bool IsReserve);

/// <summary>
/// Backs the Group Consolidation enquiry: choose the companies, an as-at date and a presentation currency, optionally
/// intercompany accounts to eliminate, and see the consolidated trial balance - translated (IAS 21) when the companies'
/// base currencies differ from the presentation currency, with the translation reserve and each company's rate. The
/// rules live in the tested <see cref="IConsolidationService"/>; this class lists and formats.
/// </summary>
public sealed partial class GroupConsolidationViewModel : ObservableObject
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public GroupConsolidationViewModel(IServiceProvider services) => _services = services;

    /// <summary>The companies.</summary>
    public ObservableCollection<ConsolidationCompanyVm> Companies { get; } = [];

    /// <summary>The currencies available to present in.</summary>
    public ObservableCollection<string> Currencies { get; } = [];

    /// <summary>The trial balance rows.</summary>
    public ObservableCollection<ConsolidationRowVm> Rows { get; } = [];

    /// <summary>The as-at date.</summary>
    [ObservableProperty]
    private DateTime _asAt = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1);

    /// <summary>The presentation currency.</summary>
    [ObservableProperty]
    private string? _presentationCurrency;

    /// <summary>Intercompany account codes to eliminate, comma-separated.</summary>
    [ObservableProperty]
    private string _intercompanyAccounts = string.Empty;

    /// <summary>The totals line.</summary>
    [ObservableProperty]
    private string _totals = string.Empty;

    /// <summary>How each company was translated, or the elimination outcome.</summary>
    [ObservableProperty]
    private string _translationNote = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Choose the companies and run the consolidation.";

    /// <summary>True while working.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConsolidateCommand))]
    private bool _isBusy;

    private bool CanConsolidate => !IsBusy;

    /// <summary>Loads the companies and currencies.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            Companies.Clear();
            var companies = await db.Companies.OrderBy(c => c.Code).Select(c => new { c.Id, c.Code, c.Name, c.BaseCurrency }).ToListAsync(ct).ConfigureAwait(true);
            foreach (var c in companies)
            {
                Companies.Add(new ConsolidationCompanyVm(c.Id, $"{c.Code} - {c.Name} ({c.BaseCurrency})"));
            }

            Currencies.Clear();
            foreach (CurrencyCode code in await db.Currencies.OrderBy(c => c.Code).Select(c => c.Code).ToListAsync(ct).ConfigureAwait(true))
            {
                Currencies.Add(code.Value);
            }

            PresentationCurrency = companies.Count > 0 ? companies[0].BaseCurrency.Value : Currencies.FirstOrDefault();
            StatusMessage = Companies.Count == 0 ? "No company is set up." : "Choose the companies and run the consolidation.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load: {ex.Message}";
        }
    }

    /// <summary>Runs the consolidation.</summary>
    [RelayCommand(CanExecute = nameof(CanConsolidate))]
    public async Task ConsolidateAsync()
    {
        List<long> ids = [.. Companies.Where(c => c.Include).Select(c => c.Id)];
        if (ids.Count == 0)
        {
            StatusMessage = "Include at least one company.";
            return;
        }

        if (!CurrencyCode.TryParse(PresentationCurrency, out CurrencyCode currency))
        {
            StatusMessage = "Choose a presentation currency.";
            return;
        }

        IsBusy = true;
        try
        {
            string[] interco = IntercompanyAccounts.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<TranslatedTrialBalance> result = await scope.ServiceProvider.GetRequiredService<IConsolidationService>()
                .TranslatedTrialBalanceAsync(ids, currency, DateOnly.FromDateTime(AsAt), interco).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            TranslatedTrialBalance tb = result.Value;
            Rows.Clear();
            foreach (ConsolidatedTrialBalanceRow row in tb.Rows)
            {
                Rows.Add(new ConsolidationRowVm(row.AccountCode, row.AccountName, Money(row.Debit), Money(row.Credit),
                    row.AccountCode == TranslatedTrialBalance.TranslationReserveCode));
            }

            Totals = $"Debits {tb.TotalDebit.ToString("N2", CultureInfo.CurrentCulture)}   Credits {tb.TotalCredit.ToString("N2", CultureInfo.CurrentCulture)}   {(tb.IsBalanced ? "Balanced" : "NOT BALANCED")}";
            List<string> notes = [.. tb.Companies
                .Where(c => c.ClosingRate != 1m || c.TranslationDifference != 0m)
                .Select(c => $"{c.CompanyCode} ({c.BaseCurrency}) closing rate {c.ClosingRate.ToString("0.######", CultureInfo.CurrentCulture)}, translation difference {c.TranslationDifference.ToString("N2", CultureInfo.CurrentCulture)}")];
            if (interco.Length > 0)
            {
                notes.Add(tb.IntercompanyUnmatched == 0m
                    ? $"Intercompany eliminated: {tb.IntercompanyEliminated.ToString("N2", CultureInfo.CurrentCulture)}."
                    : $"Intercompany does not eliminate: {tb.IntercompanyUnmatched.ToString("N2", CultureInfo.CurrentCulture)} unmatched (rows kept).");
            }

            TranslationNote = notes.Count == 0 ? $"All companies already report in {tb.PresentationCurrency}." : string.Join(Environment.NewLine, notes);
            StatusMessage = $"Consolidated {ids.Count} compan{(ids.Count == 1 ? "y" : "ies")} in {tb.PresentationCurrency} as at {tb.AsAtDate:yyyy-MM-dd}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not consolidate: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Money(decimal amount) => amount == 0m ? string.Empty : amount.ToString("N2", CultureInfo.CurrentCulture);
}
