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

/// <summary>A bank rule row (pre-formatted for display).</summary>
/// <param name="Id">The rule.</param>
/// <param name="Name">The rule name.</param>
/// <param name="Phrase">The description phrase.</param>
/// <param name="Direction">The direction filter, in words.</param>
/// <param name="Range">The amount range, or blank.</param>
/// <param name="Account">The account posted to.</param>
/// <param name="Bank">The bank it applies to, or "All banks".</param>
/// <param name="Priority">The priority.</param>
/// <param name="IsActive">Whether it applies.</param>
public sealed record BankRuleRowVm(long Id, string Name, string Phrase, string Direction, string Range, string Account, string Bank, int Priority, bool IsActive)
{
    /// <summary>"Active" or "Off".</summary>
    public string Status => IsActive ? "Active" : "Off";
}

/// <summary>
/// Backs the bank rules screen: the company's rules in the order they are tried, a form to add one (phrase,
/// direction, optional amount range and bank, account, narrative, priority), and switching a rule on or off.
/// Validation lives in the tested <see cref="IBankRuleService"/>.
/// </summary>
public sealed partial class BankRulesViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;
    private Dictionary<long, string> _bankNames = [];

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public BankRulesViewModel(IServiceProvider services) => _services = services;

    /// <summary>The rules.</summary>
    public ObservableCollection<BankRuleRowVm> Rules { get; } = [];

    /// <summary>Bank accounts for the "applies to" picker (the first entry means all banks).</summary>
    public ObservableCollection<BankChoiceVm> Banks { get; } = [];

    /// <summary>Accounts for the "post to" picker.</summary>
    public ObservableCollection<PostTargetVm> Accounts { get; } = [];

    /// <summary>Direction choices.</summary>
    public IReadOnlyList<BankRuleDirection> Directions { get; } = [BankRuleDirection.Either, BankRuleDirection.MoneyIn, BankRuleDirection.MoneyOut];

    /// <summary>The selected rule.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private BankRuleRowVm? _selectedRule;

    /// <summary>New rule: name.</summary>
    [ObservableProperty]
    private string _newName = string.Empty;

    /// <summary>New rule: phrase.</summary>
    [ObservableProperty]
    private string _newPhrase = string.Empty;

    /// <summary>New rule: direction.</summary>
    [ObservableProperty]
    private BankRuleDirection _newDirection = BankRuleDirection.MoneyOut;

    /// <summary>New rule: minimum amount (blank = none).</summary>
    [ObservableProperty]
    private string _newMin = string.Empty;

    /// <summary>New rule: maximum amount (blank = none).</summary>
    [ObservableProperty]
    private string _newMax = string.Empty;

    /// <summary>New rule: bank (first entry = all banks).</summary>
    [ObservableProperty]
    private BankChoiceVm? _newBank;

    /// <summary>New rule: account to post to.</summary>
    [ObservableProperty]
    private PostTargetVm? _newAccount;

    /// <summary>New rule: narrative (blank keeps the bank's description).</summary>
    [ObservableProperty]
    private string _newNarrative = string.Empty;

    /// <summary>New rule: priority.</summary>
    [ObservableProperty]
    private int _newPriority = 100;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while working.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand), nameof(AddCommand))]
    private bool _isBusy;

    private bool CanToggle => SelectedRule is not null && !IsBusy;

    private bool CanAdd => !IsBusy;

    /// <summary>Loads banks, accounts and rules.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            if (_companyId == 0)
            {
                _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
                if (_companyId == 0)
                {
                    StatusMessage = "No company is set up.";
                    return;
                }

                var banks = await db.Set<BankAccount>().Where(b => b.CompanyId == _companyId).OrderBy(b => b.Code).Select(b => new { b.Id, b.Code, b.Name }).ToListAsync(ct).ConfigureAwait(true);
                _bankNames = banks.ToDictionary(b => b.Id, b => b.Code);
                Banks.Clear();
                Banks.Add(new BankChoiceVm(0, "All bank accounts"));
                foreach (var b in banks)
                {
                    Banks.Add(new BankChoiceVm(b.Id, $"{b.Code} - {b.Name}"));
                }

                NewBank = Banks[0];

                Accounts.Clear();
                foreach (var a in await db.Accounts.Where(a => a.CompanyId == _companyId).OrderBy(a => a.AccountCode).Select(a => new { a.AccountCode, a.Name }).ToListAsync(ct).ConfigureAwait(true))
                {
                    Accounts.Add(new PostTargetVm(a.AccountCode, $"{a.AccountCode} - {a.Name}"));
                }
            }

            IReadOnlyList<BankRule> rules = await scope.ServiceProvider.GetRequiredService<IBankRuleService>().ListAsync(_companyId, ct).ConfigureAwait(true);
            Rules.Clear();
            foreach (BankRule r in rules)
            {
                string range = r.MinAmount is null && r.MaxAmount is null ? string.Empty : $"{Money(r.MinAmount)} – {Money(r.MaxAmount)}";
                Rules.Add(new BankRuleRowVm(r.Id, r.Name, r.DescriptionContains, Describe(r.Direction), range, r.AccountCode,
                    r.BankAccountId is { } id ? _bankNames.GetValueOrDefault(id, id.ToString(CultureInfo.InvariantCulture)) : "All banks", r.Priority, r.IsActive));
            }

            StatusMessage = $"{Rules.Count} rule(s), {Rules.Count(r => r.IsActive)} active. Rules are applied from the Bank Reconciliation screen.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load bank rules: {ex.Message}";
        }
    }

    /// <summary>Adds a rule from the form.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    public async Task AddAsync()
    {
        if (!TryAmount(NewMin, out decimal? min) || !TryAmount(NewMax, out decimal? max))
        {
            StatusMessage = "The minimum and maximum must be numbers (or blank).";
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<long> created = await scope.ServiceProvider.GetRequiredService<IBankRuleService>().CreateRuleAsync(new CreateBankRuleRequest
            {
                CompanyId = _companyId,
                BankAccountId = NewBank is { Id: > 0 } bank ? bank.Id : null,
                Name = NewName,
                DescriptionContains = NewPhrase,
                Direction = NewDirection,
                MinAmount = min,
                MaxAmount = max,
                AccountCode = NewAccount?.Key ?? string.Empty,
                Narrative = NewNarrative,
                Priority = NewPriority,
            }).ConfigureAwait(true);

            if (created.IsFailure)
            {
                StatusMessage = created.Error.Message;
                return;
            }

            NewName = NewPhrase = NewMin = NewMax = NewNarrative = string.Empty;
            StatusMessage = "Rule added.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not add the rule: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Switches the selected rule on or off.</summary>
    [RelayCommand(CanExecute = nameof(CanToggle))]
    public async Task ToggleAsync()
    {
        BankRuleRowVm? rule = SelectedRule;
        if (rule is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result done = await scope.ServiceProvider.GetRequiredService<IBankRuleService>().SetActiveAsync(_companyId, rule.Id, !rule.IsActive).ConfigureAwait(true);
            StatusMessage = done.IsFailure ? done.Error.Message : $"{rule.Name} switched {(rule.IsActive ? "off" : "on")}.";
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private static bool TryAmount(string text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal parsed) && parsed >= 0m)
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static string Describe(BankRuleDirection direction) => direction switch
    {
        BankRuleDirection.MoneyIn => "Money in",
        BankRuleDirection.MoneyOut => "Money out",
        _ => "Either",
    };

    private static string Money(decimal? value) => value is { } v ? v.ToString("N2", CultureInfo.CurrentCulture) : "…";
}
