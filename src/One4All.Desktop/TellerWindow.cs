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
/// The teller's screen (brief §RCP-3), laid out like the till: a guide strip on top (open the session there), the payer on the left (find as you
/// type, see what they owe), the money on the right (currency, amount with a keypad, reference, the tender buttons) — one receipt per tender,
/// or a split across several — posting against the teller's open session.
/// </summary>
public sealed class TellerWindow : Window
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x28, 0x33));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6C, 0x7A, 0x89));
    private static readonly Brush Panel = Brushes.White;
    private static readonly Brush Card = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFA));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x1E, 0x84, 0x49));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xD3, 0x54, 0x00));

    private readonly IServiceProvider _services;
    private readonly long _userId;
    private readonly TextBox _search = new() { FontSize = 18, Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox _hits = new() { FontSize = 14, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock _payer = new() { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _due = new() { FontSize = 15, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _currencies = new();
    private readonly TextBox _amount = new() { FontSize = 28, FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 2, 8, 2), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _reference = new() { FontSize = 14, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _cashAccount = new() { Width = 80, Text = "1000", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly Border _guide = new() { Background = Amber, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _guideText = new() { Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _openRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly ComboBox _station = new() { Width = 200, FontSize = 14, Margin = new Thickness(8, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _float = new() { Width = 90, FontSize = 16, Text = "0", Margin = new Thickness(8, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _split = new() { Content = "Split the payment (part cash, part card…)", FontSize = 13, Foreground = Ink, Margin = new Thickness(4, 8, 0, 0) };
    private readonly ListBox _parts = new() { Height = 72, FontSize = 13, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _splitButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _takeSplit = new() { Content = "Take split payment", Height = 42, Padding = new Thickness(14, 4, 14, 4), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x61, 0x8D)), BorderThickness = new Thickness(0) };
    private readonly List<ReceiptTenderRequest> _taken = [];
    private readonly List<TillCurrency> _rates = [];
    private CurrencyCode _base = CurrencyCode.Parse("USD");
    private TillCurrency? _currency;
    private PayerHit? _selected;
    private DateTimeOffset? _startedAt; // when this receipt began (payer chosen or first digit keyed), for RC10's handling time
    private long _companyId;
    private bool _sessionOpen;
    private CancellationTokenSource? _typing;

    /// <summary>Creates the window.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in teller.</param>
    public TellerWindow(IServiceProvider services, long userId)
    {
        _services = services;
        _userId = userId;
        Title = "Teller — take a payment";
        Width = 1100;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF5));
        FontFamily = new FontFamily("Segoe UI");

        DockPanel root = new() { Margin = new Thickness(12) };

        // Header
        DockPanel header = new() { Margin = new Thickness(2, 0, 2, 8) };
        Button close = Flat("✕  Close", new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)), 40, 110, 14);
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        StackPanel titles = new();
        titles.Children.Add(new TextBlock { Text = "💵  Teller — take a payment", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Ink });
        titles.Children.Add(new TextBlock { Text = "Find the payer, choose the currency, type the amount, press the tender. One receipt per tender, or tick Split for several.", FontSize = 12, Foreground = Muted });
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Guide strip: opens the session right here while there is none
        _openRow.Children.Add(new TextBlock { Text = "Counter", Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        _openRow.Children.Add(_station);
        _openRow.Children.Add(new TextBlock { Text = "Cash float", Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        _openRow.Children.Add(_float);
        Button openSession = Flat("▶  Open my session", Green, 44, 170, 15);
        openSession.Click += async (_, _) => await OpenSessionAsync().ConfigureAwait(true);
        _openRow.Children.Add(openSession);
        DockPanel guide = new();
        DockPanel.SetDock(_openRow, Dock.Right);
        guide.Children.Add(_openRow);
        guide.Children.Add(_guideText);
        _guide.Child = guide;
        DockPanel.SetDock(_guide, Dock.Top);
        root.Children.Add(_guide);

        // Status bar
        Border bar = new() { Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 10, 0, 0), Child = _status };
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);

        Grid body = new();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });

        // LEFT: the payer
        Border left = new() { Background = Panel, CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 10, 0) };
        DockPanel payerPanel = new();
        Border searchBox = new() { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(12, 0, 8, 0), Margin = new Thickness(0, 0, 0, 8) };
        DockPanel searchRow = new();
        searchRow.Children.Add(new TextBlock { Text = "🔍", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = Muted });
        searchRow.Children.Add(_search);
        searchBox.Child = searchRow;
        DockPanel.SetDock(searchBox, Dock.Top);
        payerPanel.Children.Add(searchBox);
        TextBlock hint = new() { Text = "Type an account code or name, a stand number, a student's name or admission number — matches appear as you type.", Foreground = Muted, FontSize = 12, Margin = new Thickness(6, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(hint, Dock.Top);
        payerPanel.Children.Add(hint);

        Border payerCard = new() { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 0) };
        StackPanel payerStack = new();
        payerStack.Children.Add(new TextBlock { Text = "PAYING NOW", Foreground = Muted, FontSize = 11, FontWeight = FontWeights.SemiBold });
        payerStack.Children.Add(_payer);
        payerStack.Children.Add(_due);
        payerCard.Child = payerStack;
        DockPanel.SetDock(payerCard, Dock.Bottom);
        payerPanel.Children.Add(payerCard);

        Border hitsBox = new() { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Child = _hits };
        payerPanel.Children.Add(hitsBox);
        left.Child = payerPanel;
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        // RIGHT: the money
        Border right = new() { Background = Panel, CornerRadius = new CornerRadius(10), Padding = new Thickness(12) };
        StackPanel money = new();
        money.Children.Add(new TextBlock { Text = "Currency handed over  (F9 cycles)", Foreground = Muted, FontSize = 12, Margin = new Thickness(2, 0, 0, 2) });
        money.Children.Add(_currencies);

        money.Children.Add(new TextBlock { Text = "Amount", Foreground = Muted, FontSize = 12, Margin = new Thickness(2, 8, 0, 2) });
        DockPanel amountRow = new();
        Button full = Flat("Full balance", new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)), 44, 110, 13);
        full.Margin = new Thickness(6, 0, 0, 0);
        full.Click += (_, _) => FillBalance();
        DockPanel.SetDock(full, Dock.Right);
        amountRow.Children.Add(full);
        amountRow.Children.Add(_amount);
        money.Children.Add(amountRow);

        UniformGrid pad = new() { Columns = 3, Margin = new Thickness(0, 6, 0, 0) };
        foreach (string key in new[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", ".", "0", "⌫" })
        {
            Button k = Flat(key, new SolidColorBrush(Color.FromRgb(0xE6, 0xEB, 0xF0)), 44, 0, 20);
            k.Foreground = Ink;
            k.Margin = new Thickness(2);
            string digit = key;
            k.Click += (_, _) =>
            {
                _startedAt ??= DateTimeOffset.UtcNow;
                _amount.Text = digit == "⌫" ? (_amount.Text.Length > 0 ? _amount.Text[..^1] : string.Empty) : _amount.Text + digit;
            };
            pad.Children.Add(k);
        }

        money.Children.Add(pad);

        money.Children.Add(new TextBlock { Text = "Reference (card slip, mobile money, cheque number)", Foreground = Muted, FontSize = 12, Margin = new Thickness(2, 8, 0, 2) });
        money.Children.Add(_reference);

        UniformGrid tenders = new() { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        foreach ((TenderType type, string label, string colour) in new[]
        {
            (TenderType.Cash, "CASH", "#2980B9"), (TenderType.Card, "CARD", "#8E44AD"), (TenderType.MobileMoney, "MOBILE MONEY", "#117A65"), (TenderType.Cheque, "CHEQUE", "#7D3C98"),
        })
        {
            Button tender = Flat(label, (Brush)new BrushConverter().ConvertFromString(colour)!, 58, 0, 17);
            tender.Margin = new Thickness(3);
            tender.Click += async (_, _) => await TakeAsync(type).ConfigureAwait(true);
            tenders.Children.Add(tender);
        }

        money.Children.Add(tenders);
        money.Children.Add(_split);
        money.Children.Add(_parts);
        Button clear = Flat("Clear parts", new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)), 42, 110, 13);
        clear.Margin = new Thickness(8, 0, 0, 0);
        _takeSplit.Click += async (_, _) => await TakeSplitAsync().ConfigureAwait(true);
        clear.Click += (_, _) =>
        {
            _taken.Clear();
            ShowParts();
        };
        _splitButtons.Children.Add(_takeSplit);
        _splitButtons.Children.Add(clear);
        money.Children.Add(_splitButtons);
        _split.Checked += (_, _) => ShowParts();
        _split.Unchecked += (_, _) =>
        {
            _taken.Clear();
            ShowParts();
        };

        Expander settings = new() { Header = "Settings (set once by the accountant)", FontSize = 11, Foreground = Muted, Margin = new Thickness(2, 10, 0, 0) };
        StackPanel settingsRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        settingsRow.Children.Add(new TextBlock { Text = "Cash account", Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        settingsRow.Children.Add(_cashAccount);
        settings.Content = settingsRow;
        money.Children.Add(settings);

        right.Child = new ScrollViewer { Content = money, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        root.Children.Add(body);
        Content = root;

        _search.TextChanged += async (_, _) => await SearchAsync().ConfigureAwait(true);
        _hits.SelectionChanged += (_, _) => Select(_hits.SelectedItem is ListBoxItem { Tag: PayerHit hit } ? hit : null);
        InputBindings.Add(new KeyBinding(new Relay(NextCurrency), Key.F9, ModifierKeys.None));
        Loaded += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private static Button Flat(string text, Brush background, double height, double width, double fontSize)
    {
        Button button = new()
        {
            Content = text, Height = height, FontSize = fontSize, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Background = background, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Padding = new Thickness(10, 2, 10, 2),
        };
        if (width > 0)
        {
            button.Width = width;
        }

        return button;
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
            Guide("You have no company to take payments for.", Amber);
            return;
        }

        (_companyId, _base) = (company.Id, company.BaseCurrency);
        _rates.AddRange(await scope.ServiceProvider.GetRequiredService<IPosService>().TenderCurrenciesAsync(_companyId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true));
        if (_rates.Count == 0)
        {
            _rates.Add(new TillCurrency(_base, 1m));
        }

        _currency = _rates[0];
        _sessionOpen = await db.CashierSessions.AnyAsync(s => s.CompanyId == _companyId && s.CashierUserId == _userId && s.ClosedOn == null).ConfigureAwait(true);
        if (!_sessionOpen)
        {
            foreach (var station in await db.CashierStations.AsNoTracking().Where(s => s.CompanyId == _companyId).OrderBy(s => s.Code).Select(s => new { s.Id, s.Code, s.Name }).ToListAsync().ConfigureAwait(true))
            {
                _station.Items.Add(new ComboBoxItem { Content = $"{station.Code} — {station.Name}", Tag = station.Id });
            }

            _station.SelectedIndex = _station.Items.Count > 0 ? 0 : -1;
        }

        RefreshGuide();
        Refresh();
        _search.Focus();
    }

    // The guide strip: step 1 (open the session) in amber with its controls, then the steps of a payment in green.
    private void RefreshGuide()
    {
        if (!_sessionOpen)
        {
            _openRow.Visibility = _station.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Guide(_station.Items.Count == 0 ? "No cashier station is set up yet: add one under Cashiering › Setup, then open your session here."
                : "Step 1 of 3 — choose your counter, type the cash you start with, and press Open my session.", Amber);
            return;
        }

        _openRow.Visibility = Visibility.Collapsed;
        Guide(_selected is null ? "Step 2 of 3 — find the payer: type their account, name or stand number and pick them from the list."
            : "Step 3 of 3 — type the amount (or press Full balance) and press the tender: CASH, CARD, MOBILE MONEY or CHEQUE. The receipt posts at once.", Green);
    }

    private void Guide(string text, Brush brush)
    {
        _guideText.Text = text;
        _guide.Background = brush;
    }

    // Opens the teller's session at the chosen station with the float, then the screen is ready to take payments.
    private async Task OpenSessionAsync()
    {
        if (_station.SelectedItem is not ComboBoxItem { Tag: long stationId })
        {
            _status.Text = "Choose the counter (cashier station) to open at.";
            return;
        }

        if (!decimal.TryParse(_float.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal floatAmount) || floatAmount < 0m)
        {
            _status.Text = "Type the cash float you are starting with (0 if none).";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<long> opened = await scope.ServiceProvider.GetRequiredService<IReceiptingService>()
            .OpenSessionAsync(_companyId, stationId, _userId, DateOnly.FromDateTime(DateTime.Today), [new FloatDeclaration(_base, floatAmount)]).ConfigureAwait(true);
        if (opened.IsFailure)
        {
            _status.Text = opened.Error.Message;
            return;
        }

        _sessionOpen = true;
        _status.Text = $"Session {opened.Value} open.";
        RefreshGuide();
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
                DockPanel row = new() { Margin = new Thickness(4, 3, 4, 3) };
                TextBlock owes = new() { Text = $"owes {hit.Balance.ToString("N2", CultureInfo.CurrentCulture)}", FontWeight = FontWeights.SemiBold, Foreground = hit.Balance > 0m ? Amber : Green, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(owes, Dock.Right);
                row.Children.Add(owes);
                StackPanel text = new();
                text.Children.Add(new TextBlock { Text = $"{hit.Name}  ({hit.Code})", FontSize = 14, Foreground = Ink });
                text.Children.Add(new TextBlock { Text = hit.Matched, FontSize = 11, Foreground = Muted });
                row.Children.Add(text);
                _hits.Items.Add(new ListBoxItem { Tag = hit, Content = row, Padding = new Thickness(4, 2, 4, 2) });
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
        if (hit is null)
        {
            _startedAt = null;
        }
        else
        {
            _startedAt ??= DateTimeOffset.UtcNow;
        }

        Refresh();
        RefreshGuide();
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

    // The payer, what they owe in base and in the chosen currency, and a chip per currency with the chosen one lit.
    private void Refresh()
    {
        _payer.Text = _selected is null ? "No payer chosen yet" : $"{_selected.Name}  ({_selected.Code})";
        _due.Text = _selected is null ? "Find them in the box above."
            : $"{_selected.Matched}\nOwes {_selected.Balance.ToString("N2", CultureInfo.CurrentCulture)} {_base.Value}"
              + (_currency is { } c && c.Code != _base ? $"  =  {DueIn(c).ToString("N2", CultureInfo.CurrentCulture)} {c.Code.Value} at {c.Rate.ToString("0.######", CultureInfo.CurrentCulture)}" : string.Empty);
        _currencies.Children.Clear();
        foreach (TillCurrency currency in _rates)
        {
            bool chosen = currency == _currency;
            Button chip = new()
            {
                Content = currency.Code.Value, MinWidth = 64, Height = 36, Margin = new Thickness(2), FontSize = 14, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                Background = chosen ? new SolidColorBrush(Color.FromRgb(0x1F, 0x61, 0x8D)) : new SolidColorBrush(Color.FromRgb(0xE6, 0xEB, 0xF0)),
                Foreground = chosen ? Brushes.White : Ink, BorderThickness = new Thickness(0),
                ToolTip = currency.Code == _base ? $"{currency.Code.Value} (base)" : $"{currency.Code.Value} at {currency.Rate.ToString("0.######", CultureInfo.CurrentCulture)}",
            };
            chip.Click += (_, _) =>
            {
                _currency = currency;
                Refresh();
            };
            _currencies.Children.Add(chip);
        }

        ShowParts();
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
        if (!_sessionOpen)
        {
            _status.Text = "Open your session first (step 1, at the top).";
            return;
        }

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
            _sessionOpen = false;
            RefreshGuide();
            _status.Text = "Your session is closed: open one at the top first.";
            return;
        }

        Result<ReceiptResult> posted = await scope.ServiceProvider.GetRequiredService<IReceiptingService>().PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = _companyId, SessionId = session.Value, PayerKind = PayerKind.ArCustomer, PayerAccountId = _selected.CustomerId, Date = DateOnly.FromDateTime(DateTime.Today),
            CashAccountCode = string.IsNullOrWhiteSpace(_cashAccount.Text) ? "1000" : _cashAccount.Text.Trim(), AllocationStrategy = ReceiptAllocationStrategy.OldestFirst,
            Currency = _currency.Code == _base ? null : _currency.Code,
            Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, amount)],
            Tenders = tenders,
            StartedAtUtc = _startedAt,
        }).ConfigureAwait(true);
        if (posted.IsFailure)
        {
            _status.Text = posted.Error.Message;
            return;
        }

        ReceiptResult receipt = posted.Value;
        string how = string.Join(" + ", tenders.Select(t => tenders.Count == 1 ? t.Type.ToString() : $"{t.Type} {t.Amount.ToString("N2", CultureInfo.CurrentCulture)}"));
        _status.Text = $"Receipt {receipt.ReceiptNumber}: {amount.ToString("N2", CultureInfo.CurrentCulture)} {_currency.Code.Value} ({how}) from {_selected.Name} — "
            + $"{receipt.AllocatedToDebt.ToString("N2", CultureInfo.CurrentCulture)} to the account, {receipt.CreditOnAccount.ToString("N2", CultureInfo.CurrentCulture)} credit on account. Next payer?";
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
