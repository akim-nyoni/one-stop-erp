using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Receipting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop;

/// <summary>
/// The teller's screen (brief §RCP-3): find the payer as you type (account, stand or student), see what they owe, pick the currency they hand
/// over with one click (each button shows its rate and the balance in it; F9 cycles), type or take the full amount, and press the tender —
/// the receipt posts against the teller's open session. Tick "Split payment" to take it in parts — e.g. part cash, part card — each tender press
/// adding a part, then "Take split payment" posts one receipt for the total.
/// </summary>
public sealed class TellerWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly long _userId;
    private readonly TextBox _search = new() { FontSize = 18, Padding = new Thickness(6, 4, 6, 4) };
    private readonly ListBox _hits = new() { Height = 170, FontSize = 14 };
    private readonly TextBlock _payer = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _due = new() { FontSize = 15, Margin = new Thickness(0, 0, 0, 6) };
    private readonly WrapPanel _currencies = new() { Margin = new Thickness(0, 4, 0, 6) };
    private readonly TextBox _amount = new() { FontSize = 22, Width = 180, Padding = new Thickness(6, 2, 6, 2), HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly TextBox _reference = new() { Width = 220, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _cashAccount = new() { Width = 70, Text = "1000", Margin = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly List<TillCurrency> _rates = [];
    private readonly CheckBox _split = new() { Content = "Split payment (part cash, part card…)", Margin = new Thickness(0, 10, 0, 0), FontSize = 14 };
    private readonly ListBox _parts = new() { Height = 90, FontSize = 14, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _splitButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _takeSplit = new() { Content = "Take split payment", Height = 44, Padding = new Thickness(16, 4, 16, 4), FontSize = 16, FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x61, 0x8D)) };
    private readonly List<ReceiptTenderRequest> _taken = [];
    private CurrencyCode _base = CurrencyCode.Parse("USD");
    private TillCurrency? _currency;
    private PayerHit? _selected;
    private long _companyId;
    private CancellationTokenSource? _typing;

    /// <summary>Creates the window.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in teller.</param>
    public TellerWindow(IServiceProvider services, long userId)
    {
        _services = services;
        _userId = userId;
        Title = "Teller — take a payment";
        Width = 760;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        StackPanel form = new() { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = "Find the payer — account code or name, stand number, student name or admission number", Foreground = Brushes.Gray });
        form.Children.Add(_search);
        form.Children.Add(_hits);
        form.Children.Add(_payer);
        form.Children.Add(_due);
        form.Children.Add(new TextBlock { Text = "Currency handed over — click one, or press F9", Foreground = Brushes.Gray });
        form.Children.Add(_currencies);

        Button full = new() { Content = "Full balance", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        full.Click += (_, _) => FillBalance();
        StackPanel amountRow = new() { Orientation = Orientation.Horizontal };
        amountRow.Children.Add(new TextBlock { Text = "Amount", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 15 });
        amountRow.Children.Add(_amount);
        amountRow.Children.Add(full);
        amountRow.Children.Add(new TextBlock { Text = "Reference", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Foreground = Brushes.Gray });
        amountRow.Children.Add(_reference);
        form.Children.Add(amountRow);

        UniformGrid tenders = new() { Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
        foreach ((TenderType type, string label, string colour) in new[]
        {
            (TenderType.Cash, "CASH", "#27AE60"), (TenderType.Card, "CARD", "#2874A6"), (TenderType.MobileMoney, "MOBILE", "#117A65"), (TenderType.Cheque, "CHEQUE", "#7D3C98"),
        })
        {
            Button tender = new() { Content = label, Height = 60, Margin = new Thickness(4), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                Background = (Brush)new BrushConverter().ConvertFromString(colour)! };
            tender.Click += async (_, _) => await TakeAsync(type).ConfigureAwait(true);
            tenders.Children.Add(tender);
        }

        form.Children.Add(tenders);
        form.Children.Add(_split);
        form.Children.Add(_parts);
        Button clear = new() { Content = "Clear parts", Height = 44, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        _takeSplit.Click += async (_, _) => await TakeSplitAsync().ConfigureAwait(true);
        clear.Click += (_, _) =>
        {
            _taken.Clear();
            ShowParts();
        };
        _splitButtons.Children.Add(_takeSplit);
        _splitButtons.Children.Add(clear);
        form.Children.Add(_splitButtons);
        _split.Checked += (_, _) => ShowParts();
        _split.Unchecked += (_, _) =>
        {
            _taken.Clear();
            ShowParts();
        };
        StackPanel cashRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        cashRow.Children.Add(new TextBlock { Text = "Cash account", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        cashRow.Children.Add(_cashAccount);
        form.Children.Add(cashRow);
        form.Children.Add(_status);
        Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _search.TextChanged += async (_, _) => await SearchAsync().ConfigureAwait(true);
        _hits.SelectionChanged += (_, _) => Select(_hits.SelectedItem is ListBoxItem { Tag: PayerHit hit } ? hit : null);
        InputBindings.Add(new KeyBinding(new Relay(NextCurrency), Key.F9, ModifierKeys.None));
        Loaded += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        Application.Security.DataScope reach = await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Security.IDataScopeService>().ForUserAsync(_userId).ConfigureAwait(true);
        var companies = await db.Companies.OrderBy(c => c.Id).Select(c => new { c.Id, c.Code, c.BaseCurrency }).ToListAsync().ConfigureAwait(true);
        var company = companies.FirstOrDefault(c => reach.Allows(Core.Security.ScopeDimension.Company, c.Code, c.Id.ToString(CultureInfo.InvariantCulture)));
        if (company is null)
        {
            _status.Text = "You have no company to take payments for.";
            return;
        }

        (_companyId, _base) = (company.Id, company.BaseCurrency);
        _rates.AddRange(await scope.ServiceProvider.GetRequiredService<IPosService>().TenderCurrenciesAsync(_companyId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true));
        if (_rates.Count == 0)
        {
            _rates.Add(new TillCurrency(_base, 1m));
        }

        _currency = _rates[0];
        bool open = await db.CashierSessions.AnyAsync(s => s.CompanyId == _companyId && s.CashierUserId == _userId && s.ClosedOn == null).ConfigureAwait(true);
        _status.Text = open ? "Ready." : "You have no open cashier session: open one (Receipting › Open a session) before taking payments.";
        Refresh();
        _search.Focus();
    }

    // Searches as the teller types, a moment after they stop.
    private async Task SearchAsync()
    {
        _typing?.Cancel();
        _typing = new CancellationTokenSource();
        CancellationToken token = _typing.Token;
        try
        {
            await Task.Delay(250, token).ConfigureAwait(true);
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IReadOnlyList<PayerHit> hits = await scope.ServiceProvider.GetRequiredService<IPayerSearchService>().SearchAsync(_companyId, _search.Text, 20, token).ConfigureAwait(true);
            _hits.Items.Clear();
            foreach (PayerHit hit in hits)
            {
                _hits.Items.Add(new ListBoxItem
                {
                    Tag = hit, Content = $"{hit.Code,-12} {hit.Name,-28} {hit.Matched,-34} owes {hit.Balance.ToString("N2", CultureInfo.CurrentCulture)}",
                    FontFamily = new FontFamily("Consolas"),
                });
            }

            if (hits.Count == 1)
            {
                _hits.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke took over.
        }
    }

    private void Select(PayerHit? hit)
    {
        _selected = hit;
        Refresh();
    }

    private void NextCurrency()
    {
        if (_rates.Count > 1)
        {
            _currency = _rates[(_currency is null ? 0 : _rates.IndexOf(_currency) + 1) % _rates.Count];
            Refresh();
        }
    }

    private decimal DueIn(TillCurrency currency) => _selected is null || _selected.Balance <= 0m || currency.Rate <= 0m ? 0m
        : currency.Code == _base ? _selected.Balance : Math.Ceiling(_selected.Balance / currency.Rate * 100m) / 100m;

    // The payer, what they owe in base and in the chosen currency, and a button per currency with the chosen one lit.
    private void Refresh()
    {
        _payer.Text = _selected is null ? "No payer chosen." : $"{_selected.Name} ({_selected.Code}) — {_selected.Matched}";
        _due.Text = _selected is null ? string.Empty
            : $"Owes {_selected.Balance.ToString("N2", CultureInfo.CurrentCulture)} {_base.Value}"
              + (_currency is { } c && c.Code != _base ? $"  =  {DueIn(c).ToString("N2", CultureInfo.CurrentCulture)} {c.Code.Value} at {c.Rate.ToString("0.######", CultureInfo.CurrentCulture)}" : string.Empty);
        _currencies.Children.Clear();
        foreach (TillCurrency currency in _rates)
        {
            bool chosen = currency == _currency;
            Button button = new()
            {
                Content = currency.Code.Value, MinWidth = 72, Height = 44, Margin = new Thickness(3), FontSize = 16, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                Background = chosen ? new SolidColorBrush(Color.FromRgb(0x1F, 0x61, 0x8D)) : new SolidColorBrush(Color.FromRgb(0xD5, 0xD8, 0xDC)),
                Foreground = chosen ? Brushes.White : Brushes.Black, BorderThickness = new Thickness(chosen ? 2 : 1),
                BorderBrush = chosen ? new SolidColorBrush(Color.FromRgb(0xF4, 0xD0, 0x3F)) : Brushes.Gray,
                ToolTip = currency.Code == _base ? $"{currency.Code.Value} (base)" + (_selected is null ? string.Empty : $": {DueIn(currency).ToString("N2", CultureInfo.CurrentCulture)} due")
                    : $"{currency.Code.Value} at {currency.Rate.ToString("0.######", CultureInfo.CurrentCulture)}" + (_selected is null ? string.Empty : $": {DueIn(currency).ToString("N2", CultureInfo.CurrentCulture)} due"),
            };
            button.Click += (_, _) =>
            {
                _currency = currency;
                Refresh();
            };
            _currencies.Children.Add(button);
        }
    }

    private void FillBalance()
    {
        if (_currency is not null)
        {
            _amount.Text = DueIn(_currency).ToString("0.00", CultureInfo.CurrentCulture);
        }
    }

    private async Task TakeAsync(TenderType tender)
    {
        if (_selected is null || _currency is null)
        {
            _status.Text = "Find and choose the payer first.";
            return;
        }

        if (!decimal.TryParse(_amount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal amount) || amount <= 0m)
        {
            _status.Text = _split.IsChecked == true ? "Type this part's amount, then press its tender." : "Type the amount handed over (or press Full balance).";
            return;
        }

        if (tender != TenderType.Cash && string.IsNullOrWhiteSpace(_reference.Text))
        {
            _status.Text = "Give the card, mobile money or cheque reference.";
            return;
        }

        ReceiptTenderRequest part = new(tender, amount, string.IsNullOrWhiteSpace(_reference.Text) ? null : _reference.Text.Trim());
        if (_split.IsChecked == true)
        {
            // A split: the press adds a part; the receipt is taken with "Take split payment".
            _taken.Add(part);
            _amount.Text = string.Empty;
            _reference.Text = string.Empty;
            ShowParts();
            _amount.Focus();
            return;
        }

        await PostAsync([part]).ConfigureAwait(true);
    }

    private async Task TakeSplitAsync()
    {
        if (_selected is null || _currency is null)
        {
            _status.Text = "Find and choose the payer first.";
            return;
        }

        if (Application.Receipting.TenderSplit.Problem(_taken) is string problem)
        {
            _status.Text = problem;
            return;
        }

        await PostAsync(Application.Receipting.TenderSplit.Merge(_taken)).ConfigureAwait(true);
    }

    // The parts taken so far, their total and what is left of the balance.
    private void ShowParts()
    {
        bool on = _split.IsChecked == true;
        _parts.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _splitButtons.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _parts.Items.Clear();
        foreach (ReceiptTenderRequest part in _taken)
        {
            _parts.Items.Add($"{part.Type,-12} {part.Amount.ToString("N2", CultureInfo.CurrentCulture),12}  {part.Reference}");
        }

        decimal total = Application.Receipting.TenderSplit.Total(_taken);
        string code = _currency?.Code.Value ?? _base.Value;
        decimal left = _currency is null ? 0m : DueIn(_currency) - total;
        _takeSplit.Content = _taken.Count == 0 ? "Take split payment" : $"Take split payment — {total.ToString("N2", CultureInfo.CurrentCulture)} {code}";
        if (on)
        {
            _status.Text = _taken.Count == 0 ? "Type each part's amount and press its tender; then Take split payment."
                : $"{_taken.Count} part(s), {total.ToString("N2", CultureInfo.CurrentCulture)} {code}" + (_selected is null ? "." : left > 0m
                    ? $"; {left.ToString("N2", CultureInfo.CurrentCulture)} {code} of the balance left."
                    : left < 0m ? $"; {(-left).ToString("N2", CultureInfo.CurrentCulture)} {code} over the balance (credit on account)." : "; the balance is covered.");
        }
    }

    private async Task PostAsync(IReadOnlyList<ReceiptTenderRequest> tenders)
    {
        if (_selected is null || _currency is null)
        {
            return;
        }

        decimal amount = Application.Receipting.TenderSplit.Total(tenders);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long? session = await db.CashierSessions.AsNoTracking().Where(s => s.CompanyId == _companyId && s.CashierUserId == _userId && s.ClosedOn == null).OrderByDescending(s => s.Id)
            .Select(s => (long?)s.Id).FirstOrDefaultAsync().ConfigureAwait(true);
        if (session is null)
        {
            _status.Text = "You have no open cashier session: open one (Receipting › Open a session) first.";
            return;
        }

        Result<ReceiptResult> posted = await scope.ServiceProvider.GetRequiredService<IReceiptingService>().PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = _companyId, SessionId = session.Value, PayerKind = PayerKind.ArCustomer, PayerAccountId = _selected.CustomerId, Date = DateOnly.FromDateTime(DateTime.Today),
            CashAccountCode = string.IsNullOrWhiteSpace(_cashAccount.Text) ? "1000" : _cashAccount.Text.Trim(), AllocationStrategy = ReceiptAllocationStrategy.OldestFirst,
            Currency = _currency.Code == _base ? null : _currency.Code,
            Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, amount)],
            Tenders = tenders,
        }).ConfigureAwait(true);
        if (posted.IsFailure)
        {
            _status.Text = posted.Error.Message;
            return;
        }

        ReceiptResult receipt = posted.Value;
        string how = string.Join(" + ", tenders.Select(t => tenders.Count == 1 ? t.Type.ToString() : $"{t.Type} {t.Amount.ToString("N2", CultureInfo.CurrentCulture)}"));
        _status.Text = $"Receipt {receipt.ReceiptNumber}: {amount.ToString("N2", CultureInfo.CurrentCulture)} {_currency.Code.Value} ({how}) from {_selected.Name} — "
            + $"{receipt.AllocatedToDebt.ToString("N2", CultureInfo.CurrentCulture)} to the account, {receipt.CreditOnAccount.ToString("N2", CultureInfo.CurrentCulture)} credit on account.";
        _amount.Text = string.Empty;
        _reference.Text = string.Empty;
        _taken.Clear();
        _parts.Items.Clear();
        _takeSplit.Content = "Take split payment";
        _search.Text = string.Empty;
        _hits.Items.Clear();
        Select(null);
        _search.Focus();
    }

    private sealed class Relay(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => run();
    }
}
