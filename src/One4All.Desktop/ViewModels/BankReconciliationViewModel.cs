using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A bank account choice.</summary>
/// <param name="Id">The bank account id.</param>
/// <param name="Label">Code and name.</param>
public sealed record BankChoiceVm(long Id, string Label);

/// <summary>A statement line row (pre-formatted for display).</summary>
/// <param name="Id">The statement line.</param>
/// <param name="Date">The statement date.</param>
/// <param name="Description">The bank's description.</param>
/// <param name="Reference">The bank's reference.</param>
/// <param name="Amount">The signed amount.</param>
/// <param name="Status">"Matched" or "Open".</param>
/// <param name="IsMatched">Whether the line is reconciled.</param>
/// <param name="AmountValue">The signed amount as a number (direction decides what the line can be posted as).</param>
public sealed record StatementRowVm(long Id, string Date, string Description, string Reference, string Amount, string Status, bool IsMatched, decimal AmountValue);

/// <summary>A counterparty for posting a statement line: a customer, supplier or nominal account.</summary>
/// <param name="Key">The customer / supplier id, or the account code.</param>
/// <param name="Label">What the picker shows.</param>
public sealed record PostTargetVm(string Key, string Label);

/// <summary>An unpresented GL posting row (pre-formatted for display).</summary>
/// <param name="Id">The journal line.</param>
/// <param name="Date">The posting date.</param>
/// <param name="Journal">The journal number.</param>
/// <param name="Document">The document number.</param>
/// <param name="Amount">The signed amount.</param>
public sealed record GlRowVm(long Id, string Date, string Journal, string Document, string Amount);

/// <summary>
/// Backs the bank reconciliation screen: pick a bank account and date, see the cash-book / statement
/// balances and the residual difference, auto-match within a date window, and match or unmatch lines by
/// hand. Every rule lives in the tested <see cref="IBankReconciliationService"/>; this class lists and forwards.
/// </summary>
public sealed partial class BankReconciliationViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public BankReconciliationViewModel(IServiceProvider services) => _services = services;

    /// <summary>The bank accounts.</summary>
    public ObservableCollection<BankChoiceVm> Banks { get; } = [];

    /// <summary>Statement lines up to the as-at date (matched and open).</summary>
    public ObservableCollection<StatementRowVm> StatementLines { get; } = [];

    /// <summary>GL postings not yet on the statement.</summary>
    public ObservableCollection<GlRowVm> UnpresentedGlLines { get; } = [];

    /// <summary>The selected bank account.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(AutoMatchCommand))]
    private BankChoiceVm? _selectedBank;

    /// <summary>The as-at date.</summary>
    [ObservableProperty]
    private DateTime _asAt = DateTime.Today;

    /// <summary>Days either side of the statement date auto-match may look.</summary>
    [ObservableProperty]
    private int _dateToleranceDays = 7;

    /// <summary>The selected statement line.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MatchCommand), nameof(UnmatchCommand), nameof(PostLineCommand))]
    private StatementRowVm? _selectedStatementLine;

    /// <summary>What an open statement line can be posted as.</summary>
    public IReadOnlyList<string> PostKinds { get; } = [CustomerReceipt, SupplierPayment, ToAccount];

    /// <summary>The counterparties for the chosen kind.</summary>
    public ObservableCollection<PostTargetVm> PostTargets { get; } = [];

    /// <summary>How to post the selected line.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PostLineCommand))]
    private string _postKind = CustomerReceipt;

    /// <summary>The chosen customer / supplier / account.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PostLineCommand))]
    private PostTargetVm? _postTarget;

    /// <summary>An optional document number or narrative for the posting.</summary>
    [ObservableProperty]
    private string _postReference = string.Empty;

    private const string CustomerReceipt = "Customer receipt";
    private const string SupplierPayment = "Supplier payment";
    private const string ToAccount = "Post to account";

    private readonly List<PostTargetVm> _customers = [];
    private readonly List<PostTargetVm> _suppliers = [];
    private readonly List<PostTargetVm> _accounts = [];

    private bool CanPostLine => SelectedStatementLine is { IsMatched: false } && PostTarget is not null && !IsBusy;

    partial void OnPostKindChanged(string value) => RefillTargets();

    partial void OnSelectedStatementLineChanged(StatementRowVm? value)
    {
        // Default the kind from the line's direction; a fee or interest line can still be switched to an account.
        if (value is { IsMatched: false })
        {
            string kind = value.AmountValue > 0m ? CustomerReceipt : SupplierPayment;
            if (PostKind != ToAccount && PostKind != kind)
            {
                PostKind = kind;
            }
        }
    }

    private void RefillTargets()
    {
        PostTargets.Clear();
        foreach (PostTargetVm target in PostKind switch { CustomerReceipt => _customers, SupplierPayment => _suppliers, _ => _accounts })
        {
            PostTargets.Add(target);
        }

        PostTarget = null;
    }

    /// <summary>Posts the selected open statement line as the chosen kind against the chosen counterparty, matching it.</summary>
    [RelayCommand(CanExecute = nameof(CanPostLine))]
    public Task PostLineAsync()
    {
        StatementRowVm? line = SelectedStatementLine;
        PostTargetVm? target = PostTarget;
        if (line is null || target is null)
        {
            return Task.CompletedTask;
        }

        string? reference = string.IsNullOrWhiteSpace(PostReference) ? null : PostReference.Trim();
        string kind = PostKind;
        return ActAsync(async (_, bank) =>
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IBankStatementPostingService posting = scope.ServiceProvider.GetRequiredService<IBankStatementPostingService>();
            Result<StatementLinePosting> posted = kind switch
            {
                CustomerReceipt => await posting.PostCustomerReceiptAsync(_companyId, bank.Id, line.Id, long.Parse(target.Key, CultureInfo.InvariantCulture), reference).ConfigureAwait(true),
                SupplierPayment => await posting.PostSupplierPaymentAsync(_companyId, bank.Id, line.Id, long.Parse(target.Key, CultureInfo.InvariantCulture), reference).ConfigureAwait(true),
                _ => await posting.PostToAccountAsync(_companyId, bank.Id, line.Id, target.Key, reference).ConfigureAwait(true),
            };
            if (posted.IsSuccess)
            {
                PostReference = string.Empty;
            }

            return posted.IsSuccess ? Result.Success() : Result.Failure(posted.Error);
        }, $"Posted {line.Date} {line.Amount} as {kind.ToLowerInvariant()} to {target.Label} and matched it.");
    }

    /// <summary>The selected unpresented GL posting.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MatchCommand))]
    private GlRowVm? _selectedGlLine;

    /// <summary>Cash-book balance.</summary>
    [ObservableProperty]
    private string _glBalance = "–";

    /// <summary>Statement balance.</summary>
    [ObservableProperty]
    private string _statementBalance = "–";

    /// <summary>Residual difference after unreconciled items.</summary>
    [ObservableProperty]
    private string _difference = "–";

    /// <summary>True when the residual difference is zero.</summary>
    [ObservableProperty]
    private bool _isReconciled;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while working.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(AutoMatchCommand), nameof(MatchCommand), nameof(UnmatchCommand), nameof(PostLineCommand))]
    private bool _isBusy;

    private bool CanLoad => SelectedBank is not null && !IsBusy;

    private bool CanMatch => SelectedStatementLine is { IsMatched: false } && SelectedGlLine is not null && !IsBusy;

    private bool CanUnmatch => SelectedStatementLine is { IsMatched: true } && !IsBusy;

    partial void OnSelectedBankChanged(BankChoiceVm? value) => _ = LoadAsync();

    /// <summary>Loads the bank accounts (first open).</summary>
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

            Banks.Clear();
            foreach (var b in await db.Set<BankAccount>().Where(b => b.CompanyId == _companyId).OrderBy(b => b.Code).Select(b => new { b.Id, b.Code, b.Name }).ToListAsync(ct).ConfigureAwait(true))
            {
                Banks.Add(new BankChoiceVm(b.Id, $"{b.Code} — {b.Name}"));
            }

            var customers = await db.Customers.Where(c => c.CompanyId == _companyId).OrderBy(c => c.Code).Select(c => new { c.Id, c.Code, c.Name }).ToListAsync(ct).ConfigureAwait(true);
            var suppliers = await db.Suppliers.Where(s => s.CompanyId == _companyId).OrderBy(s => s.Code).Select(s => new { s.Id, s.Code, s.Name }).ToListAsync(ct).ConfigureAwait(true);
            var accounts = await db.Accounts.Where(a => a.CompanyId == _companyId).OrderBy(a => a.AccountCode).Select(a => new { a.AccountCode, a.Name }).ToListAsync(ct).ConfigureAwait(true);
            _customers.Clear();
            _customers.AddRange(customers.Select(c => new PostTargetVm(c.Id.ToString(CultureInfo.InvariantCulture), $"{c.Code} — {c.Name}")));
            _suppliers.Clear();
            _suppliers.AddRange(suppliers.Select(s => new PostTargetVm(s.Id.ToString(CultureInfo.InvariantCulture), $"{s.Code} — {s.Name}")));
            _accounts.Clear();
            _accounts.AddRange(accounts.Select(a => new PostTargetVm(a.AccountCode, $"{a.AccountCode} — {a.Name}")));
            RefillTargets();

            StatusMessage = Banks.Count == 0 ? "No bank accounts are set up." : "Choose a bank account.";
            SelectedBank = Banks.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load bank accounts: {ex.Message}";
        }
    }

    /// <summary>Loads (or reloads) the balances and work list.</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    public async Task LoadAsync()
    {
        BankChoiceVm? bank = SelectedBank;
        if (bank is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            DateOnly asAt = DateOnly.FromDateTime(AsAt);

            Result<BankReconciliationReport> report = await recon.GetReconciliationAsync(_companyId, bank.Id, asAt).ConfigureAwait(true);
            if (report.IsFailure)
            {
                StatusMessage = report.Error.Message;
                return;
            }

            GlBalance = Money(report.Value.GlBalance);
            StatementBalance = Money(report.Value.StatementBalance);
            Difference = Money(report.Value.Difference);
            IsReconciled = report.Value.Difference == 0m;

            Result<BankWorkList> work = await recon.WorkListAsync(_companyId, bank.Id, asAt).ConfigureAwait(true);
            StatementLines.Clear();
            UnpresentedGlLines.Clear();
            if (work.IsSuccess)
            {
                foreach (StatementWorkItem s in work.Value.StatementLines)
                {
                    StatementLines.Add(new StatementRowVm(s.Id, Date(s.Date), s.Description, s.Reference ?? string.Empty, Money(s.Amount), s.MatchedJournalLineId is null ? "Open" : "Matched", s.MatchedJournalLineId is not null, s.Amount));
                }

                foreach (GlWorkItem g in work.Value.UnpresentedGlLines)
                {
                    UnpresentedGlLines.Add(new GlRowVm(g.Id, Date(g.Date), g.JournalNumber, g.DocumentNumber ?? string.Empty, Money(g.Amount)));
                }
            }

            SelectedStatementLine = null;
            SelectedGlLine = null;
            int open = StatementLines.Count(s => !s.IsMatched);
            StatusMessage = $"{open} open statement line(s), {UnpresentedGlLines.Count} unpresented posting(s) as at {asAt:yyyy-MM-dd}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load the reconciliation: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Auto-matches within the date tolerance, then reloads.</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    public Task AutoMatchAsync() => ActAsync(async (recon, bank) =>
    {
        Result<int> matched = await recon.AutoMatchAsync(_companyId, bank.Id, DateToleranceDays).ConfigureAwait(true);
        return matched.IsSuccess ? Result.Success() : Result.Failure(matched.Error);
    }, "Auto-match complete.");

    /// <summary>Matches the selected statement line to the selected GL posting, then reloads.</summary>
    [RelayCommand(CanExecute = nameof(CanMatch))]
    public Task MatchAsync()
    {
        StatementRowVm? s = SelectedStatementLine;
        GlRowVm? g = SelectedGlLine;
        return s is null || g is null
            ? Task.CompletedTask
            : ActAsync((recon, bank) => recon.MatchLineAsync(_companyId, bank.Id, s.Id, g.Id), $"Matched statement line {s.Date} {s.Amount} to {g.Journal}.");
    }

    /// <summary>Unmatches the selected statement line, then reloads.</summary>
    [RelayCommand(CanExecute = nameof(CanUnmatch))]
    public Task UnmatchAsync()
    {
        StatementRowVm? s = SelectedStatementLine;
        return s is null
            ? Task.CompletedTask
            : ActAsync((recon, _) => recon.UnmatchLineAsync(_companyId, s.Id), $"Unmatched statement line {s.Date} {s.Amount}.");
    }

    private async Task ActAsync(Func<IBankReconciliationService, BankChoiceVm, Task<Result>> action, string done)
    {
        BankChoiceVm? bank = SelectedBank;
        if (bank is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result result = await action(scope.ServiceProvider.GetRequiredService<IBankReconciliationService>(), bank).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            StatusMessage = done;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
