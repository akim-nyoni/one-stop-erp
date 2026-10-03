using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A nominal account choice.</summary>
/// <param name="Code">The account code.</param>
/// <param name="Label">Code and name.</param>
public sealed record AccountChoiceVm(string Code, string Label);

/// <summary>An activity row (pre-formatted for display).</summary>
/// <param name="Date">The posting date.</param>
/// <param name="JournalNumber">The journal number (the drill-down key).</param>
/// <param name="Narrative">The narrative.</param>
/// <param name="Debit">Debit, or blank.</param>
/// <param name="Credit">Credit, or blank.</param>
/// <param name="Balance">The running balance.</param>
public sealed record ActivityRowVm(string Date, string JournalNumber, string Narrative, string Debit, string Credit, string Balance);

/// <summary>A journal line in the drill-down (pre-formatted for display).</summary>
/// <param name="Account">Code and name.</param>
/// <param name="Analysis">Cost centre / department, or blank.</param>
/// <param name="Debit">Debit, or blank.</param>
/// <param name="Credit">Credit, or blank.</param>
/// <param name="Narrative">The line narrative.</param>
public sealed record JournalLineRowVm(string Account, string Analysis, string Debit, string Credit, string Narrative);

/// <summary>
/// Backs the account enquiry: pick a nominal account and a date range, see the opening balance, every
/// posting with its running balance and the closing balance, and drill into any posting's journal. Both
/// views come from the tested <see cref="IGeneralLedgerReports"/>; this class only formats them.
/// </summary>
public sealed partial class AccountEnquiryViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public AccountEnquiryViewModel(IServiceProvider services) => _services = services;

    /// <summary>The nominal accounts.</summary>
    public ObservableCollection<AccountChoiceVm> Accounts { get; } = [];

    /// <summary>The postings in the range.</summary>
    public ObservableCollection<ActivityRowVm> Activity { get; } = [];

    /// <summary>The selected posting's journal lines.</summary>
    public ObservableCollection<JournalLineRowVm> JournalLines { get; } = [];

    /// <summary>The selected account.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private AccountChoiceVm? _selectedAccount;

    /// <summary>Range start (defaults to the start of the year).</summary>
    [ObservableProperty]
    private DateTime _fromDate = new(DateTime.Today.Year, 1, 1);

    /// <summary>Range end (defaults to today).</summary>
    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    /// <summary>The selected posting.</summary>
    [ObservableProperty]
    private ActivityRowVm? _selectedActivity;

    /// <summary>Opening balance.</summary>
    [ObservableProperty]
    private string _opening = "–";

    /// <summary>Total debits in the range.</summary>
    [ObservableProperty]
    private string _totalDebit = "–";

    /// <summary>Total credits in the range.</summary>
    [ObservableProperty]
    private string _totalCredit = "–";

    /// <summary>Closing balance.</summary>
    [ObservableProperty]
    private string _closing = "–";

    /// <summary>The drill-down heading (journal number, date, status, document, reversal links).</summary>
    [ObservableProperty]
    private string _journalHeading = "Select a posting to see its journal.";

    /// <summary>The drill-down narrative.</summary>
    [ObservableProperty]
    private string _journalNarrative = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while working.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private bool _isBusy;

    private bool CanLoad => SelectedAccount is not null && !IsBusy;

    partial void OnSelectedActivityChanged(ActivityRowVm? value) => _ = ShowJournalAsync(value);

    /// <summary>Loads the chart of accounts.</summary>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
            if (_companyId == 0)
            {
                StatusMessage = "No company is set up.";
                return;
            }

            Accounts.Clear();
            foreach (var a in await db.Accounts.Where(a => a.CompanyId == _companyId).OrderBy(a => a.AccountCode).Select(a => new { a.AccountCode, a.Name }).ToListAsync(ct).ConfigureAwait(true))
            {
                Accounts.Add(new AccountChoiceVm(a.AccountCode, $"{a.AccountCode} — {a.Name}"));
            }

            StatusMessage = $"{Accounts.Count} account(s). Choose one and press Show.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load the chart of accounts: {ex.Message}";
        }
    }

    /// <summary>Loads the account's activity for the range.</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    public async Task LoadAsync()
    {
        AccountChoiceVm? account = SelectedAccount;
        if (account is null)
        {
            return;
        }

        if (FromDate > ToDate)
        {
            StatusMessage = "The From date is after the To date.";
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            NominalActivityReport report = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>()
                .NominalActivityAsync(_companyId, account.Code, DateOnly.FromDateTime(FromDate), DateOnly.FromDateTime(ToDate)).ConfigureAwait(true);

            Activity.Clear();
            foreach (NominalActivityLine line in report.Lines)
            {
                Activity.Add(new ActivityRowVm(line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), line.JournalNumber, line.Narrative ?? string.Empty,
                    Blank(line.Debit), Blank(line.Credit), Money(line.RunningBalance)));
            }

            Opening = Money(report.OpeningBalance);
            TotalDebit = Money(report.Lines.Sum(l => l.Debit));
            TotalCredit = Money(report.Lines.Sum(l => l.Credit));
            Closing = Money(report.ClosingBalance);
            SelectedActivity = null;
            StatusMessage = $"{report.Lines.Count} posting(s) on {report.AccountCode} {report.AccountName}. Select one to see its journal.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load the account: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShowJournalAsync(ActivityRowVm? row)
    {
        JournalLines.Clear();
        JournalNarrative = string.Empty;
        if (row is null)
        {
            JournalHeading = "Select a posting to see its journal.";
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            JournalDetail? journal = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>()
                .JournalDetailAsync(_companyId, row.JournalNumber).ConfigureAwait(true);
            if (journal is null)
            {
                JournalHeading = $"Journal {row.JournalNumber} was not found.";
                return;
            }

            string document = string.Join(" ", new[] { journal.DocumentType, journal.DocumentNumber }.Where(s => !string.IsNullOrWhiteSpace(s)));
            string links = journal.ReversalOf is not null ? $" · reverses {journal.ReversalOf}" : journal.ReversedBy is not null ? $" · reversed by {journal.ReversedBy}" : string.Empty;
            JournalHeading = $"{journal.JournalNumber} · {journal.PostingDate:yyyy-MM-dd} · {journal.SourceModule} · {journal.Status}"
                + (document.Length > 0 ? $" · {document}" : string.Empty) + links;
            JournalNarrative = journal.Narrative ?? string.Empty;

            foreach (JournalDetailLine line in journal.Lines)
            {
                string analysis = string.Join(" / ", new[] { line.CostCentre, line.Department }.Where(s => !string.IsNullOrWhiteSpace(s)));
                JournalLines.Add(new JournalLineRowVm($"{line.AccountCode} {line.AccountName}", analysis, Blank(line.Debit), Blank(line.Credit), line.Narrative ?? string.Empty));
            }

            JournalLines.Add(new JournalLineRowVm("Total", string.Empty, Money(journal.TotalDebit), Money(journal.TotalCredit), string.Empty));
        }
        catch (Exception ex)
        {
            JournalHeading = $"Could not load journal {row.JournalNumber}: {ex.Message}";
        }
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    private static string Blank(decimal value) => value == 0m ? string.Empty : Money(value);
}
