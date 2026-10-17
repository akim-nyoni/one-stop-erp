using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Offline;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Offline;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Offline;
using One4All.Core.Pos;
using One4All.Core.Restaurant;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A product button on the terminal grid (a priced, sellable menu item).</summary>
public sealed class PosProductVm
{
    /// <summary>The stock item the tile sells (stock and cost of sales follow it).</summary>
    public long ItemId { get; init; }

    /// <summary>The tile as the catalogue gave it, for searching.</summary>
    public PosTile? Tile { get; init; }

    /// <summary>The menu name shown on the button.</summary>
    public required string Name { get; init; }

    /// <summary>The category (for filtering).</summary>
    public required string Category { get; init; }

    /// <summary>The tile colour (a favourite's own, else the default).</summary>
    public System.Windows.Media.Brush TileBrush { get; init; } = DefaultTileBrush;

    /// <summary>The default tile colour.</summary>
    public static System.Windows.Media.Brush DefaultTileBrush { get; } = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x61, 0x8D));

    /// <summary>The tax-exclusive unit price.</summary>
    public required decimal Price { get; init; }

    /// <summary>The revenue account credited.</summary>
    public required string RevenueAccountCode { get; init; }

    /// <summary>The tax code, if any.</summary>
    public string? TaxCode { get; init; }

    /// <summary>The tax rate as a fraction (resolved from <see cref="TaxCode"/>).</summary>
    public decimal TaxRate { get; init; }

    /// <summary>The price label for the button.</summary>
    public string PriceText => Price.ToString("N2", CultureInfo.CurrentCulture);
}

/// <summary>A cart row on the terminal, wrapping a <see cref="PosTicketLine"/>.</summary>
public sealed class PosCartLineVm(PosTicketLine line)
{
    /// <summary>The underlying ticket line.</summary>
    public PosTicketLine Line { get; } = line;

    /// <summary>The description.</summary>
    public string Description => Line.Description;

    /// <summary>The quantity.</summary>
    public decimal Quantity => Line.Quantity;

    /// <summary>The gross line value.</summary>
    public string LineGrossText => Line.LineGross.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>Gets the adjustments on the line (price override, discount).</summary>
    public string Detail => string.Join("  ", new[]
    {
        Line.IsOverridden ? $"price {Line.ListUnitPriceNet.ToString("N2", CultureInfo.CurrentCulture)} → {Line.UnitPriceNet.ToString("N2", CultureInfo.CurrentCulture)}" : null,
        Line.Discount > 0m ? $"less {Line.Discount.ToString("N2", CultureInfo.CurrentCulture)}{(Line.DiscountPercent is decimal p ? $" ({p:0.##}%)" : string.Empty)}" : null,
        Line.PromotionDiscount > 0m ? $"{Line.PromotionCode} −{Line.PromotionDiscount.ToString("N2", CultureInfo.CurrentCulture)}" : null,
    }.Where(t => t is not null));

    /// <summary>Gets or sets a value indicating whether this row is selected for an adjustment.</summary>
    public bool IsSelected { get; init; }
}

/// <summary>
/// Backs the full-screen touch POS terminal. It is a thin shell over the tested <see cref="PosTicket"/>
/// engine: it loads the priced menu as product buttons, opens/closes a till session through
/// <see cref="IPosService"/>, builds up a ticket, takes tenders, and completes the sale (which posts the
/// revenue/tax/tender journal). The cart and tender maths live in <see cref="PosTicket"/> so they are unit
/// tested; this class only marshals state to and from the screen.
/// </summary>
public sealed partial class PosTerminalViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _cashierUserId;
    private readonly PosTicket _ticket = new();
    private readonly Dictionary<string, decimal> _taxRates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PosProductVm> _allProducts = [];

    private long _companyId;
    private long _terminalId;
    private long _warehouseId;
    private CurrencyCode _currency = CurrencyCode.Parse("USD");
    private int _saleSequence;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    /// <param name="cashierUserId">The signed-in cashier.</param>
    /// <param name="cashierName">The cashier's display name.</param>
    public PosTerminalViewModel(IServiceProvider services, long cashierUserId, string cashierName)
    {
        _services = services;
        _cashierUserId = cashierUserId;
        _cashierName = cashierName;
        Cart.CollectionChanged += (_, _) => RefreshGuide();
        Products.CollectionChanged += (_, _) => RefreshGuide();
    }

    /// <summary>Gets what to do next, in one line: open the till, add products, take the payment.</summary>
    public string GuideText => _companyId == 0 ? "Loading the till…"
        : _allProducts.Count == 0 ? "No products are priced for the till yet. Add selling prices first (Stock & Products › Prices › Price lists), then reopen the till."
        : !IsSessionOpen ? "Step 1 of 3 — type the cash you are starting with (the float) and press Open Till."
        : Cart.Count == 0 ? "Step 2 of 3 — tap a product tile, or scan / type its barcode in the box and press Add. Each tap adds one."
        : "Step 3 of 3 — type the amount handed over on the keypad, press CASH (or CARD / MOBILE), then PAY / COMPLETE. New Sale starts the next customer.";

    /// <summary>Gets the guide strip's colour: amber until the till is open, green while trading.</summary>
    public System.Windows.Media.Brush GuideBrush => _allProducts.Count == 0 ? System.Windows.Media.Brushes.Firebrick
        : !IsSessionOpen ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x54, 0x00))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x84, 0x49));

    /// <summary>Gets a value indicating whether the till still has to be opened.</summary>
    public bool IsTillClosed => !IsSessionOpen && _companyId != 0 && _allProducts.Count > 0;

    private void RefreshGuide()
    {
        OnPropertyChanged(nameof(GuideText));
        OnPropertyChanged(nameof(GuideBrush));
        OnPropertyChanged(nameof(IsTillClosed));
    }

    partial void OnIsSessionOpenChanged(bool value) => RefreshGuide();

    /// <summary>The product buttons for the selected category.</summary>
    public ObservableCollection<PosProductVm> Products { get; } = [];

    /// <summary>The categories offered (plus "All").</summary>
    public ObservableCollection<string> Categories { get; } = [];

    /// <summary>The current cart rows.</summary>
    public ObservableCollection<PosCartLineVm> Cart { get; } = [];

    /// <summary>The cashier's display name.</summary>
    [ObservableProperty]
    private string _cashierName = string.Empty;

    /// <summary>
    /// Hands the till to the next cashier (they enter their name and PIN in the supervisor boxes): the drawer count closes this cashier's
    /// session with its cash-up, and a session opens in the incoming cashier's name with the counted cash as float.
    /// </summary>
    /// <returns>A task.</returns>
    [RelayCommand]
    private async Task HandOverAsync()
    {
        if (!IsSessionOpen || string.IsNullOrWhiteSpace(CountedCash) || string.IsNullOrWhiteSpace(CashAccountCode) || string.IsNullOrWhiteSpace(OverShortAccountCode))
        {
            StatusMessage = "To hand over, count the drawer and give the cash and over/short accounts, and the next cashier enters their name and PIN.";
            return;
        }

        if (!CashCounts.TryParse(CountedCash, _currency.Value, out IReadOnlyList<CashCount> counts))
        {
            StatusMessage = "Type the count (120, note by note 50x10+20x5, or 120; ZAR 500), and the next cashier their name and PIN (or swipe their card).";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        AuthenticationResult incoming = await IdentifyPersonAsync(scope).ConfigureAwait(true);
        if (!incoming.Succeeded)
        {
            StatusMessage = incoming.Message;
            return;
        }

        long next = incoming.UserId;

        Result<CashierHandover> handed = await scope.ServiceProvider.GetRequiredService<IPosService>().HandOverAsync(new CashierHandoverRequest
        {
            TillSessionId = TillSessionId, IncomingCashierUserId = next, Counts = counts, CountedByUserId = _cashierUserId, Date = DateOnly.FromDateTime(DateTime.Today),
            CashAccountCode = CashAccountCode.Trim(), OverShortAccountCode = OverShortAccountCode.Trim(),
        }).ConfigureAwait(true);
        if (handed.IsFailure)
        {
            StatusMessage = handed.Error.Message;
            return;
        }

        CashUpResult r = handed.Value.OutgoingCashUp;
        string outgoing = CashierName;
        _cashierUserId = next;
        CashierName = incoming.DisplayName;
        TillSessionId = handed.Value.IncomingSessionId;
        CountedCash = string.Empty;
        StatusMessage = $"{outgoing} handed over: expected {r.ExpectedCash:N2}, counted {r.CountedCash:N2}. {CashierName} now has the till (session {TillSessionId}).";
    }

    /// <summary>
    /// The quick switch: the next cashier enters their name and PIN (or swipes their card) and takes the till as it is — the sale on the screen
    /// stays, nothing is counted; the drawer is shared until a hand-over or the Z-read. Needs the connection.
    /// </summary>
    /// <returns>A task.</returns>
    [RelayCommand]
    private async Task SwitchCashierAsync()
    {
        if (!IsSessionOpen)
        {
            StatusMessage = "Open the till first.";
            return;
        }

        if (!_online || TillSessionId == 0)
        {
            StatusMessage = "Offline: a cashier switch needs the connection (hand over with a count when it returns).";
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            AuthenticationResult incoming = await IdentifyPersonAsync(scope).ConfigureAwait(true);
            if (!incoming.Succeeded)
            {
                StatusMessage = incoming.Message;
                return;
            }

            Result switched = await scope.ServiceProvider.GetRequiredService<IPosService>().SwitchCashierAsync(TillSessionId, incoming.UserId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true);
            if (switched.IsFailure)
            {
                StatusMessage = switched.Error.Message;
                return;
            }

            string outgoing = CashierName;
            _cashierUserId = incoming.UserId;
            CashierName = incoming.DisplayName;
            SupervisorName = string.Empty;
            StatusMessage = $"{CashierName} has taken the till from {outgoing} (no cash-up; the sale on the screen stays).";
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            StatusMessage = "Offline: a cashier switch needs the connection.";
        }
    }

    // Who is at the till: a name and PIN; a card swiped or scanned into the name box (no PIN); or, with nothing typed, the fingerprint reader.
    private async Task<AuthenticationResult> IdentifyPersonAsync(AsyncServiceScope scope)
    {
        IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        string terminal = $"POS-{_terminalId}";
        string name = SupervisorName?.Trim() ?? string.Empty;
        string pin = SupervisorPin ?? string.Empty;
        SupervisorPin = string.Empty;
        SupervisorPinCleared?.Invoke();
        if (name.Length > 0 && pin.Length > 0)
        {
            return await auth.AuthenticateByPinAsync(name, pin, terminal).ConfigureAwait(true);
        }

        if (name.Length > 0)
        {
            SupervisorName = string.Empty;
            return await auth.AuthenticateByCardAsync(name, terminal).ConfigureAwait(true);
        }

        if (scope.ServiceProvider.GetRequiredService<IDeviceProvider>().FingerprintReader is IFingerprintReader reader
            && await reader.IdentifyAsync().ConfigureAwait(true) is string enrolment)
        {
            return await auth.AuthenticateByBiometricAsync(enrolment, terminal).ConfigureAwait(true);
        }

        return AuthenticationResult.Failure(AuthenticationOutcome.InvalidCredentials, "Enter a name and PIN, swipe a card, or use the fingerprint reader.");
    }

    /// <summary>The GL account cash (and change) posts to.</summary>
    [ObservableProperty]
    private string _cashAccountCode = string.Empty;

    /// <summary>A coupon code typed at the till.</summary>
    [ObservableProperty]
    private string _couponEntry = string.Empty;

    private readonly List<string> _coupons = [];
    private bool _repricing;
    private long? _branchId;

    /// <summary>The GL account card tenders post to.</summary>
    [ObservableProperty]
    private string _cardAccountCode = string.Empty;

    /// <summary>The GL account mobile-money tenders post to.</summary>
    [ObservableProperty]
    private string _mobileMoneyAccountCode = string.Empty;

    /// <summary>The gift card liability account a voucher tender posts to.</summary>
    [ObservableProperty]
    private string _giftCardAccountCode = string.Empty;

    /// <summary>The tips-payable account a tip added at the till is credited to.</summary>
    [ObservableProperty]
    private string _tipsAccountCode = string.Empty;

    /// <summary>Adds the amount typed in the scan box to the ticket as the customer's tip (0 removes it); the tenders then cover it.</summary>
    [RelayCommand]
    private void AddTip()
    {
        if (!decimal.TryParse((ScanEntry ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal tip))
        {
            StatusMessage = "Type the tip amount in the scan box first.";
            return;
        }

        Result set = _ticket.SetTip(tip);
        StatusMessage = set.IsSuccess ? (tip > 0m ? $"Tip {tip:N2} added." : "Tip removed.") : set.Error.Message;
        ScanEntry = string.Empty;
        RefreshTotals();
    }

    /// <summary>The gift card number for a voucher tender (typed or scanned).</summary>
    [ObservableProperty]
    private string _voucherNumber = string.Empty;

    /// <summary>The currencies the till takes today (base first) and the one the next cash, card or mobile tender is in.</summary>
    public ObservableCollection<TillCurrency> TenderCurrencies { get; } = [];

    /// <summary>The currency of the next cash, card or mobile tender.</summary>
    [ObservableProperty]
    private TillCurrency? _selectedTenderCurrency;

    /// <summary>The customer code an on-account sale is charged to.</summary>
    [ObservableProperty]
    private string _accountCustomer = string.Empty;

    partial void OnSelectedTenderCurrencyChanged(TillCurrency? value) => RefreshTotals();

    /// <summary>
    /// The currency buttons on the till: one per currency taken today, the selected one highlighted, each one's tooltip giving its rate and
    /// the balance due in it — one click (or F9 to cycle) switches the currency the next tender is in.
    /// </summary>
    public IReadOnlyList<CurrencyButton> CurrencyButtons => [.. TenderCurrencies.Select(c => new CurrencyButton(c, c.Code.Value, c == SelectedTenderCurrency,
        c.Code == _currency ? $"{c.Code.Value} (base): {_ticket.BalanceDue.ToString("N2", CultureInfo.CurrentCulture)} due"
            : $"{c.Code.Value} at {c.Rate.ToString("0.######", CultureInfo.CurrentCulture)}: {_ticket.BalanceDueIn(c.Rate).ToString("N2", CultureInfo.CurrentCulture)} {c.Code.Value} due"))];

    /// <summary>The currency the next tender is in, as shown on the till.</summary>
    public string PayingInText => SelectedTenderCurrency is { } c ? $"Paying in {c.Code.Value}" : "Paying in base";

    /// <summary>Takes the next tender in a currency (a currency button).</summary>
    /// <param name="code">The currency.</param>
    [RelayCommand]
    private void PayIn(string? code)
    {
        if (TenderCurrencies.FirstOrDefault(c => c.Code.Value == code) is { } currency)
        {
            SelectedTenderCurrency = currency;
        }
    }

    /// <summary>Moves to the next currency the till takes (F9).</summary>
    [RelayCommand]
    private void NextCurrency()
    {
        if (TenderCurrencies.Count > 1)
        {
            int at = SelectedTenderCurrency is null ? -1 : TenderCurrencies.IndexOf(SelectedTenderCurrency);
            SelectedTenderCurrency = TenderCurrencies[(at + 1) % TenderCurrencies.Count];
        }
    }

    /// <summary>The balance due in the selected tender currency (blank in base).</summary>
    public string BalanceInTenderCurrencyText => SelectedTenderCurrency is { } currency && currency.Code != _currency
        ? $"{_ticket.BalanceDueIn(currency.Rate).ToString("N2", CultureInfo.CurrentCulture)} {currency.Code.Value} @ {currency.Rate.ToString("0.######", CultureInfo.CurrentCulture)}"
        : string.Empty;

    /// <summary>The GL account manual discounts post to.</summary>
    [ObservableProperty]
    private string _discountAccountCode = string.Empty;

    /// <summary>The most a cashier may discount without a supervisor (percent).</summary>
    [ObservableProperty]
    private decimal _unsupervisedDiscountLimit = 10m;

    /// <summary>The cart row selected for a discount or price override.</summary>
    [ObservableProperty]
    private PosCartLineVm? _selectedCartLine;

    /// <summary>The supervisor authorising an override (user name; the PIN comes from the PIN box).</summary>
    [ObservableProperty]
    private string _supervisorName = string.Empty;

    /// <summary>Gets or sets the supervisor's PIN (set from the PIN box, never shown).</summary>
    public string SupervisorPin { get; set; } = string.Empty;

    /// <summary>What was scanned or typed: a barcode (plain or with an embedded weight / price), a PLU or an item code.</summary>
    [ObservableProperty]
    private string _scanEntry = string.Empty;

    /// <summary>The loyalty member earning points on this sale.</summary>
    [ObservableProperty]
    private string _loyaltyMember = string.Empty;

    /// <summary>The name a parked sale is filed under.</summary>
    [ObservableProperty]
    private string _parkTag = string.Empty;

    /// <summary>The parked sale chosen to resume.</summary>
    [ObservableProperty]
    private ParkedSaleSummary? _selectedParkedSale;

    /// <summary>Gets the parked sales waiting.</summary>
    public ObservableCollection<ParkedSaleSummary> ParkedSales { get; } = [];

    /// <summary>The output-tax account.</summary>
    [ObservableProperty]
    private string _taxAccountCode = string.Empty;

    /// <summary>Whether the cashier has checked the buyer is old enough for age-restricted items.</summary>
    [ObservableProperty]
    private bool _ageVerified;

    /// <summary>The prescription prescription-only items are sold against.</summary>
    [ObservableProperty]
    private string _prescriptionReference = string.Empty;

    /// <summary>The cash-over/short account the Z cash-up variance posts to.</summary>
    [ObservableProperty]
    private string _overShortAccountCode = string.Empty;

    /// <summary>The cash counted for the Z cash-up (blank closes without a cash-up).</summary>
    [ObservableProperty]
    private string _countedCash = string.Empty;

    /// <summary>The opening float entered when opening the till.</summary>
    [ObservableProperty]
    private string _openingFloat = "0.00";

    /// <summary>True while a till session is open and trading.</summary>
    [ObservableProperty]
    private bool _isSessionOpen;

    /// <summary>The category filter.</summary>
    [ObservableProperty]
    private string _selectedCategory = "All";

    /// <summary>The number being typed on the tender pad.</summary>
    [ObservableProperty]
    private string _tenderEntry = string.Empty;

    /// <summary>A status/message line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>The number of sales captured locally while offline and awaiting sync.</summary>
    [ObservableProperty]
    private int _pendingOfflineCount;

    /// <summary>The plain-text receipt for the last posted sale (empty until a sale posts).</summary>
    [ObservableProperty]
    private string _lastReceiptText = string.Empty;

    /// <summary>The net subtotal.</summary>
    public string SubtotalText => _ticket.SubtotalNet.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>The tax total.</summary>
    public string TaxText => _ticket.TaxTotal.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>The gross total.</summary>
    public string GrossText => _ticket.GrossTotal.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>The amount tendered so far.</summary>
    public string TenderedText => _ticket.Tendered.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>The outstanding balance.</summary>
    public string BalanceText => _ticket.BalanceDue.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>The change owed.</summary>
    public string ChangeText => _ticket.ChangeDue.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>Loads the company, terminal, menu and default accounts.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            await LoadOnlineAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            await LoadOfflineAsync(ct).ConfigureAwait(true);
        }
    }

    // The database is unreachable: the terminal sells from its sealed local snapshot (brief §6B), and its sales queue locally.
    private async Task LoadOfflineAsync(CancellationToken ct)
    {
        PendingOfflineCount = await LocalQueue.CountAsync(ct).ConfigureAwait(true);
        PosOfflineSnapshot? snapshot = await CatalogueStore.LoadAsync(ct).ConfigureAwait(true);
        if (snapshot is null)
        {
            StatusMessage = "Offline, and this terminal has no saved catalogue yet — it needs the connection once before it can sell offline.";
            return;
        }

        _snapshot = snapshot;
        _companyId = snapshot.CompanyId;
        _currency = CurrencyCode.Parse(snapshot.BaseCurrency);
        _warehouseId = snapshot.WarehouseId;
        _terminalId = snapshot.TerminalId;
        _taxRates.Clear();
        foreach ((string code, decimal rate) in snapshot.TaxRates)
        {
            _taxRates[code] = rate;
        }

        ShowProducts(snapshot.Tiles);
        string Account(string role, string current) => snapshot.Accounts.TryGetValue(role, out string? code) && code.Length > 0 ? code : current;
        CashAccountCode = Account("Cash", CashAccountCode);
        CardAccountCode = Account("Card", CardAccountCode);
        TaxAccountCode = Account("Tax", TaxAccountCode);
        OverShortAccountCode = Account("OverShort", OverShortAccountCode);
        DiscountAccountCode = Account("Discount", DiscountAccountCode);
        MobileMoneyAccountCode = Account("MobileMoney", MobileMoneyAccountCode);
        GiftCardAccountCode = Account("GiftCard", GiftCardAccountCode);
        TipsAccountCode = Account("Tips", TipsAccountCode);
        TenderCurrencies.Clear();
        foreach (OfflineTenderCurrency currency in snapshot.TenderCurrencies)
        {
            TenderCurrencies.Add(new TillCurrency(CurrencyCode.Parse(currency.Code), currency.Rate));
        }

        SelectedTenderCurrency = TenderCurrencies.FirstOrDefault();
        string taken = snapshot.TakenUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        if (snapshot.OpenSessionId is long session)
        {
            TillSessionId = session;
            IsSessionOpen = true;
            StatusMessage = $"Offline — selling from the catalogue saved {taken} in till session {session}; sales queue here ({PendingOfflineCount} so far) and sync when the connection returns.";
        }
        else
        {
            StatusMessage = $"Offline — the catalogue saved {taken} is shown, but a till can only be opened with the connection.";
        }
    }

    private async Task LoadOnlineAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

        Company? company = await db.Companies.OrderBy(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        if (company is null)
        {
            StatusMessage = "No company is set up — create one before using the terminal.";
            return;
        }

        _companyId = company.Id;
        _currency = company.BaseCurrency;

        _warehouseId = await db.Set<Warehouse>()
            .Where(w => w.CompanyId == _companyId)
            .OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);

        _terminalId = await ResolveTerminalAsync(db, ct).ConfigureAwait(true);

        await LoadTaxRatesAsync(db, ct).ConfigureAwait(true);
        await LoadProductsAsync(scope.ServiceProvider.GetRequiredService<IPosCatalogueService>(), db, ct).ConfigureAwait(true);
        await PrefillAccountsAsync(db, ct).ConfigureAwait(true);
        TenderCurrencies.Clear();
        foreach (TillCurrency currency in await scope.ServiceProvider.GetRequiredService<IPosService>().TenderCurrenciesAsync(_companyId, DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(true))
        {
            TenderCurrencies.Add(currency);
        }

        SelectedTenderCurrency = TenderCurrencies.FirstOrDefault();
        _customers = await db.Customers.AsNoTracking().Where(c => c.CompanyId == _companyId).OrderBy(c => c.Code).Take(5000)
            .Select(c => new OfflineCustomer(c.Id, c.Code, c.Name)).ToListAsync(ct).ConfigureAwait(true);
        await SaveSnapshotAsync(IsSessionOpen ? TillSessionId : null).ConfigureAwait(true);
        await RefreshParkedAsync().ConfigureAwait(true);

        PendingOfflineCount = await LocalQueue.CountAsync(ct).ConfigureAwait(true);

        StatusMessage = _allProducts.Count == 0
            ? "No products are priced for the till — add selling prices (Stock & Products › Prices) and reopen the till."
            : PendingOfflineCount > 0
                ? $"{PendingOfflineCount} offline sale(s) queued. Open the till to sync and trade."
                : "Enter an opening float and press Open Till to start trading.";
        RefreshGuide();
    }

    private ILocalOfflineQueue LocalQueue => _services.GetRequiredService<ILocalOfflineQueue>();

    private ILocalCatalogueStore CatalogueStore => _services.GetRequiredService<ILocalCatalogueStore>();

    private PosOfflineSnapshot? _snapshot;
    private IReadOnlyList<PosTile> _tiles = [];

    private const string FavouritesTab = "★ Favourites";

    // The till's favourites in order, each category's parent (to show sub-categories with their parent), and whether the server can be asked.
    private List<FavouriteTile> _favourites = [];
    private TerminalProfile _profile;

    /// <summary>The till's profile as shown in the header.</summary>
    public string ProfileText => _profile == TerminalProfile.General ? string.Empty : _profile.ToString();

    /// <summary>Whether the till weighs goods (not at a bar or restaurant till).</summary>
    public bool IsScaleVisible => _profile is not (TerminalProfile.Bar or TerminalProfile.Restaurant);

    private IReadOnlyDictionary<string, string> _functionKeys = TillFunctionKeys.Defaults(TerminalProfile.General);

    /// <summary>Gets the function keys for the help line.</summary>
    public string FunctionKeysText => TillFunctionKeys.Describe(_functionKeys);

    /// <summary>Runs the till action mapped to a function key.</summary>
    /// <param name="key">The key ("F2").</param>
    /// <returns>True when the key is mapped (and its action was run if it could be).</returns>
    public bool RunFunctionKey(string key)
    {
        if (!_functionKeys.TryGetValue(key, out string? action)
            || GetType().GetProperty(action + "Command")?.GetValue(this) is not System.Windows.Input.ICommand command)
        {
            return false;
        }

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        return true;
    }
    private Dictionary<string, string?> _categoryParents = new(StringComparer.OrdinalIgnoreCase);

    // The category tree by code (parent → children) for drilling down, and the category whose children the chips show (null: the top).
    private Dictionary<string, List<string>> _categoryChildren = new(StringComparer.OrdinalIgnoreCase);
    private string? _categoryLevel;

    /// <summary>The chip that goes back up a level.</summary>
    public const string BackChip = "◀ Back";

    /// <summary>Gets where in the category tree the chips are (All › Drinks › Soft drinks).</summary>
    public string CategoryPath
    {
        get
        {
            List<string> path = [];
            for (string? level = _categoryLevel; level is not null; level = _categoryParents.GetValueOrDefault(level))
            {
                path.Insert(0, level);
            }

            return string.Join(" › ", ["All", .. path]);
        }
    }
    private bool _online;
    private CancellationTokenSource? _searching;
    private List<OfflineCustomer> _customers = [];

    // Seals what the terminal needs to sell offline; best effort — a terminal that cannot write its snapshot still sells online.
    private async Task SaveSnapshotAsync(long? openSessionId)
    {
        if (_companyId == 0)
        {
            return;
        }

        IReadOnlyList<One4All.Application.Pos.PromotionDefinition> promotions = _snapshot?.Promotions ?? [];
        IReadOnlyList<OfflineSupervisor> supervisors = _snapshot?.Supervisors ?? [];
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            promotions = await scope.ServiceProvider.GetRequiredService<IPromotionService>().DefinitionsAsync(_companyId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true);
            long terminal = _terminalId;
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            _branchId ??= await db.PosTerminals.AsNoTracking().Where(t => t.Id == terminal).Select(t => (long?)t.BranchId).FirstOrDefaultAsync().ConfigureAwait(true);

            // Who may authorise a void or refund while offline: every active user with a PIN and the permission, their PIN hash sealed with the rest.
            IPermissionService permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
            List<OfflineSupervisor> found = [];
            foreach (var user in await db.Users.AsNoTracking().Where(u => u.IsActive && u.PinHash != null).Select(u => new { u.Id, u.Username, u.PinHash }).ToListAsync().ConfigureAwait(true))
            {
                EffectivePermissions permissions = await permissionService.ResolveAsync(user.Id).ConfigureAwait(true);
                bool canVoid = permissions.Has("Pos.Void", OperationFlags.VoidReverse), canRefund = permissions.Has("Pos.Refund", OperationFlags.AuthoriseLevel1);
                if (canVoid || canRefund)
                {
                    found.Add(new OfflineSupervisor(user.Id, user.Username, user.PinHash!, canVoid, canRefund));
                }
            }

            supervisors = found;
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            // Keep the promotions saved last time.
        }

        _snapshot = new PosOfflineSnapshot
        {
            Promotions = promotions, BranchId = _branchId,
            TakenUtc = DateTimeOffset.UtcNow, CompanyId = _companyId, BaseCurrency = _currency.Value, WarehouseId = _warehouseId, TerminalId = _terminalId,
            OpenSessionId = openSessionId, TaxRates = new Dictionary<string, decimal>(_taxRates, StringComparer.OrdinalIgnoreCase), Tiles = _tiles,
            Accounts = new Dictionary<string, string>
            {
                ["Cash"] = CashAccountCode, ["Card"] = CardAccountCode, ["Tax"] = TaxAccountCode, ["OverShort"] = OverShortAccountCode, ["Discount"] = DiscountAccountCode,
                ["MobileMoney"] = MobileMoneyAccountCode, ["GiftCard"] = GiftCardAccountCode, ["Tips"] = TipsAccountCode,
            },
            TenderCurrencies = [.. TenderCurrencies.Select(t => new OfflineTenderCurrency(t.Code.Value, t.Rate))],
            Customers = _customers.Count > 0 ? _customers : _snapshot?.Customers ?? [],
            Supervisors = supervisors,
        };
        try
        {
            await CatalogueStore.SaveAsync(_snapshot).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            // The snapshot is a fallback; failing to write it never stops an online sale.
        }
    }

    private async Task<long> ResolveTerminalAsync(One4AllDbContext db, CancellationToken ct)
    {
        PosTerminal? terminal = await db.Set<PosTerminal>()
            .Where(t => t.CompanyId == _companyId && t.IsActive)
            .OrderBy(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);

        if (terminal is not null)
        {
            return terminal.Id;
        }

        // Self-provision a default terminal so the shell is usable out of the box.
        PosTerminal created = new(_companyId, branchId: 0, "POS1", "Terminal 1");
        db.Set<PosTerminal>().Add(created);
        await db.SaveChangesAsync(ct).ConfigureAwait(true);
        return created.Id;
    }

    private async Task LoadTaxRatesAsync(One4AllDbContext db, CancellationToken ct)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        List<TaxCode> codes = await db.Set<TaxCode>()
            .Where(t => t.CompanyId == _companyId)
            .ToListAsync(ct).ConfigureAwait(true);

        _taxRates.Clear();
        foreach (TaxCode code in codes)
        {
            _taxRates[code.Code] = code.RateOn(today) / 100m; // stored as a percent
        }
    }

    // Tiles come from the item master (not the restaurant menu), so a tapped sale issues stock and posts its cost like a scanned one.
    private async Task LoadProductsAsync(IPosCatalogueService catalogue, One4AllDbContext db, CancellationToken ct)
    {
        _favourites = [.. await catalogue.FavouriteTilesAsync(_companyId, _terminalId == 0 ? null : _terminalId, _cashierUserId == 0 ? null : _cashierUserId, ct).ConfigureAwait(true)];
        long terminal = _terminalId;
        var till = await db.PosTerminals.AsNoTracking().Where(t => t.Id == terminal).Select(t => new { t.Profile, t.FunctionKeys }).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        _profile = till?.Profile ?? TerminalProfile.General;
        _functionKeys = TillFunctionKeys.For(_profile, till?.FunctionKeys);
        OnPropertyChanged(nameof(ProfileText));
        OnPropertyChanged(nameof(FunctionKeysText));
        OnPropertyChanged(nameof(IsScaleVisible));
        _categoryParents = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        _categoryChildren = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemCategory category in await db.ItemCategories.AsNoTracking().Where(c => c.CompanyId == _companyId).ToListAsync(ct).ConfigureAwait(true))
        {
            _categoryParents[category.Code] = category.ParentCode;
            _categoryParents.TryAdd(category.Name, category.ParentCode);
            PosCategoryNames.Map[category.Code] = category.Name;
            if (category.ParentCode is string parent)
            {
                (_categoryChildren.TryGetValue(parent, out List<string>? children) ? children : _categoryChildren[parent] = []).Add(category.Code);
            }
        }

        _online = true;
        ShowProducts(await catalogue.TilesAsync(_companyId, ct).ConfigureAwait(true));
    }

    private void ShowProducts(IReadOnlyList<PosTile> tiles)
    {
        _tiles = tiles;
        _allProducts.Clear();
        foreach (PosTile tile in tiles)
        {
            _allProducts.Add(Product(tile));
        }

        _categoryLevel = null;
        ShowCategoryLevel();
        SelectedCategory = "All";
        ApplyCategoryFilter();
    }

    // The chips for the current level: at the top All, the favourites pages and the top categories that have something to sell under them;
    // inside a category, Back, the category itself (everything under it) and its children with products.
    private void ShowCategoryLevel()
    {
        Categories.Clear();
        if (_categoryLevel is null)
        {
            Categories.Add("All");
            // One favourites tab per page (a bar or restaurant till opens on its favourites).
            foreach (int page in _favourites.Select(f => f.Page).Distinct().Order())
            {
                Categories.Add(FavouritesPage(page));
            }
        }
        else
        {
            Categories.Add(BackChip);
            Categories.Add(_categoryLevel);
        }

        IEnumerable<string> level = _categoryLevel is null
            ? _allProducts.Select(p => TopOf(p.Category)).Distinct(StringComparer.OrdinalIgnoreCase)
            : _categoryChildren.GetValueOrDefault(_categoryLevel) ?? [];
        foreach (string category in level.Where(c => _allProducts.Any(p => InCategory(p, c))).OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        {
            Categories.Add(category);
        }

        OnPropertyChanged(nameof(CategoryPath));
    }

    // The top of a category's tree (a category with no parent, or one the tree does not know).
    private string TopOf(string category)
    {
        string top = category;
        for (int guard = 0; guard < 20 && _categoryParents.GetValueOrDefault(top) is string parent; guard++)
        {
            top = parent;
        }

        return top;
    }

    private static string FavouritesPage(int page) => page <= 1 ? FavouritesTab : $"{FavouritesTab} {page}";

    // A favourite's tile in its own colour (an unreadable colour keeps the default).
    private static PosProductVm Coloured(PosProductVm product, string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour))
        {
            return product;
        }

        try
        {
            return new PosProductVm
            {
                ItemId = product.ItemId, Tile = product.Tile, Name = product.Name, Category = product.Category, Price = product.Price, RevenueAccountCode = product.RevenueAccountCode,
                TaxCode = product.TaxCode, TaxRate = product.TaxRate,
                TileBrush = (System.Windows.Media.Brush?)new System.Windows.Media.BrushConverter().ConvertFromString(colour) ?? PosProductVm.DefaultTileBrush,
            };
        }
        catch (FormatException)
        {
            return product;
        }
    }

    private PosProductVm Product(PosTile tile) => new()
    {
        ItemId = tile.ItemId,
        Tile = tile,
        Name = tile.Name,
        Category = tile.Category,
        Price = tile.UnitPrice,
        RevenueAccountCode = tile.RevenueAccountCode,
        TaxCode = tile.TaxCode,
        TaxRate = tile.TaxCode is not null && _taxRates.TryGetValue(tile.TaxCode, out decimal r) ? r : 0m,
    };

    // Whether a tile's category is the one chosen or sits (at any depth) under it.
    private bool InCategory(PosProductVm product, string chosen)
    {
        for (string? category = product.Category; category is not null; category = _categoryParents.GetValueOrDefault(category))
        {
            if (string.Equals(category, chosen, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!_categoryParents.ContainsKey(category))
            {
                break;
            }
        }

        return false;
    }

    /// <summary>The finder's brand filter (blank: any).</summary>
    [ObservableProperty]
    private string _finderBrand = string.Empty;

    /// <summary>The finder's supplier filter (blank: any).</summary>
    [ObservableProperty]
    private string _finderSupplier = string.Empty;

    /// <summary>The finder's lowest price (blank: none).</summary>
    [ObservableProperty]
    private string _finderMinPrice = string.Empty;

    /// <summary>The finder's highest price (blank: none).</summary>
    [ObservableProperty]
    private string _finderMaxPrice = string.Empty;

    /// <summary>Only items with stock on hand.</summary>
    [ObservableProperty]
    private bool _finderInStock;

    /// <summary>Only items on a promotion.</summary>
    [ObservableProperty]
    private bool _finderPromotion;

    /// <summary>Only items sold in the last week.</summary>
    [ObservableProperty]
    private bool _finderRecent;

    /// <summary>Whether the finder's filter bar is open.</summary>
    [ObservableProperty]
    private bool _isFinderOpen;

    /// <summary>The brands to choose from.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> FinderBrands { get; } = [];

    /// <summary>The suppliers to choose from.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> FinderSuppliers { get; } = [];

    /// <summary>Opens or closes the filter bar (loading the brands and suppliers the first time).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ToggleFinderAsync()
    {
        IsFinderOpen = !IsFinderOpen;
        if (!IsFinderOpen || FinderBrands.Count + FinderSuppliers.Count > 0 || !_online)
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            (IReadOnlyList<string> brands, IReadOnlyList<string> suppliers) = await scope.ServiceProvider.GetRequiredService<IPosCatalogueService>().FinderChoicesAsync(_companyId)
                .ConfigureAwait(true);
            foreach (string brand in brands)
            {
                FinderBrands.Add(brand);
            }

            foreach (string supplier in suppliers)
            {
                FinderSuppliers.Add(supplier);
            }
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            _online = false;
        }
    }

    /// <summary>Shows the items that pass the finder's filters (within the chosen category).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ApplyFinderAsync()
    {
        static decimal? Price(string text) => decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out decimal value) ? value : null;
        PosFinderFilter filter = new(SelectedCategory == "All" ? null : SelectedCategory, NullIfBlank(FinderBrand), NullIfBlank(FinderSupplier), Price(FinderMinPrice), Price(FinderMaxPrice),
            FinderInStock, FinderPromotion, FinderRecent ? 7 : null);
        if (!_online)
        {
            // Offline: the price band applies to the tiles on hand; the other filters need the server.
            Products.Clear();
            foreach (PosProductVm product in _allProducts.Where(p => (filter.Category is null || InCategory(p, filter.Category))
                && (filter.MinPrice is not decimal min || p.Price >= min) && (filter.MaxPrice is not decimal max || p.Price <= max)))
            {
                Products.Add(product);
            }

            StatusMessage = "Offline: only the category and price band filter the tiles.";
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IReadOnlyList<PosTile> hits = await scope.ServiceProvider.GetRequiredService<IPosCatalogueService>()
                .FindAsync(_companyId, filter, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true);
            Products.Clear();
            foreach (PosTile hit in hits)
            {
                Products.Add(_allProducts.FirstOrDefault(p => p.ItemId == hit.ItemId) ?? Product(hit));
            }

            StatusMessage = hits.Count == 0 ? "No item passes those filters." : $"{hits.Count} item(s) found.";
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            _online = false;
            StatusMessage = "The server cannot be reached; the finder works on the tiles on hand.";
        }
    }

    /// <summary>Clears the finder's filters and shows the chosen category again.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ClearFinder()
    {
        FinderBrand = FinderSupplier = FinderMinPrice = FinderMaxPrice = string.Empty;
        FinderInStock = FinderPromotion = FinderRecent = false;
        ApplyCategoryFilter();
    }

    // What was typed matches no tile here: ask the server, which also knows alias and pack barcodes and synonyms.
    private async Task SearchServerAsync(string text)
    {
        _searching?.Cancel();
        CancellationTokenSource cts = new();
        _searching = cts;
        try
        {
            await Task.Delay(300, cts.Token).ConfigureAwait(true);
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IReadOnlyList<PosTile> hits = await scope.ServiceProvider.GetRequiredService<IPosCatalogueService>().SearchAsync(_companyId, text, 30, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || ScanEntry != text)
            {
                return;
            }

            Products.Clear();
            foreach (PosTile hit in hits)
            {
                Products.Add(_allProducts.FirstOrDefault(p => p.ItemId == hit.ItemId) ?? Product(hit));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke took over.
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            _online = false;
        }
    }

    private async Task PrefillAccountsAsync(One4AllDbContext db, CancellationToken ct)
    {
        List<LedgerAccount> accounts = await db.Set<LedgerAccount>()
            .Where(a => a.CompanyId == _companyId && a.IsActive)
            .OrderBy(a => a.AccountCode)
            .ToListAsync(ct).ConfigureAwait(true);

        CashAccountCode = FindByName(accounts, "cash", "till", "bank") ?? CashAccountCode;
        CardAccountCode = FindByName(accounts, "card", "clearing") ?? CashAccountCode;
        TaxAccountCode = FindByName(accounts, "vat", "output tax", "tax") ?? TaxAccountCode;
        OverShortAccountCode = FindByName(accounts, "over", "short", "variance") ?? OverShortAccountCode;
        DiscountAccountCode = FindByName(accounts, "discount allowed", "discounts allowed", "sales discount") ?? DiscountAccountCode;
        MobileMoneyAccountCode = FindByName(accounts, "mobile money", "ecocash", "wallet") ?? MobileMoneyAccountCode;
        GiftCardAccountCode = FindByName(accounts, "gift card", "voucher") ?? GiftCardAccountCode;
    }

    private static string? FindByName(IEnumerable<LedgerAccount> accounts, params string[] keywords) =>
        accounts.FirstOrDefault(a => keywords.Any(k => a.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))?.AccountCode;

    partial void OnSelectedCategoryChanged(string value)
    {
        if (value is null)
        {
            return; // the chips are being rebuilt
        }

        if (value == BackChip)
        {
            _categoryLevel = _categoryLevel is null ? null : _categoryParents.GetValueOrDefault(_categoryLevel);
            ShowCategoryLevel();
            SelectedCategory = _categoryLevel ?? "All";
            return;
        }

        // A category with sub-categories opens them (its own chip then shows everything under it).
        if (!string.Equals(value, _categoryLevel, StringComparison.OrdinalIgnoreCase) && _categoryChildren.TryGetValue(value, out List<string>? children)
            && children.Any(c => _allProducts.Any(p => InCategory(p, c))))
        {
            _categoryLevel = value;
            ShowCategoryLevel();
            SelectedCategory = value;
            return;
        }

        ApplyCategoryFilter();
    }

    // Typing in the scan box narrows the tiles as you type (code, name, barcode or PLU); Enter still adds the exact match.
    partial void OnScanEntryChanged(string value) => ApplyCategoryFilter();

    /// <summary>Moves a favourite tile to where another one is (dragged onto it) and saves the cashier's arrangement of the page.</summary>
    /// <param name="moving">The tile dragged.</param>
    /// <param name="onto">The tile it was dropped on.</param>
    /// <returns>A task.</returns>
    public async Task MoveFavouriteAsync(PosProductVm moving, PosProductVm onto)
    {
        if (SelectedCategory is not string tab || !tab.StartsWith(FavouritesTab, StringComparison.Ordinal) || _cashierUserId == 0 || !_online)
        {
            return;
        }

        int page = _favourites.FirstOrDefault(f => f.Tile.ItemId == moving.ItemId)?.Page ?? 1;
        IReadOnlyList<long> order = FavouriteOrder.Move([.. Products.Select(p => p.ItemId)], moving.ItemId, onto.ItemId);
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IPosCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<IPosCatalogueService>();
            Result saved = await catalogue.ArrangeFavouritesAsync(_companyId, _terminalId == 0 ? null : _terminalId, _cashierUserId, page, order).ConfigureAwait(true);
            if (saved.IsFailure)
            {
                StatusMessage = saved.Error.Message;
                return;
            }

            _favourites = [.. await catalogue.FavouriteTilesAsync(_companyId, _terminalId == 0 ? null : _terminalId, _cashierUserId).ConfigureAwait(true)];
            ApplyCategoryFilter();
            StatusMessage = $"{moving.Name} moved.";
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            StatusMessage = "Offline: favourites can be rearranged when the connection returns.";
        }
    }

    private void ApplyCategoryFilter()
    {
        Products.Clear();
        IEnumerable<PosProductVm> chosen = SelectedCategory switch
        {
            "All" => _allProducts,
            string tab when tab.StartsWith(FavouritesTab, StringComparison.Ordinal) => _favourites.Where(f => FavouritesPage(f.Page) == tab)
                .Select(f => _allProducts.FirstOrDefault(p => p.ItemId == f.Tile.ItemId) is PosProductVm p ? Coloured(p, f.Colour) : null).OfType<PosProductVm>(),
            _ => _allProducts.Where(p => InCategory(p, SelectedCategory)),
        };
        foreach (PosProductVm product in chosen.Where(p => p.Tile?.Matches(ScanEntry) ?? true))
        {
            Products.Add(product);
        }

        if (Products.Count == 0 && _online && ScanEntry is { Length: >= 3 } typed)
        {
            _ = SearchServerAsync(typed);
        }
    }

    /// <summary>Opens a till session with the entered float.</summary>
    [RelayCommand]
    private async Task OpenTillAsync()
    {
        if (IsSessionOpen || _companyId == 0)
        {
            return;
        }

        decimal floatAmount = decimal.TryParse(OpeningFloat, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal f) ? f : 0m;

        Result<long> result;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            result = await pos.OpenTillAsync(_companyId, _terminalId, _cashierUserId, floatAmount).ConfigureAwait(true);
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex) && _snapshot is not null)
        {
            // Offline: open the till here under a key; it opens centrally when the connection returns, before the sales rung up in it.
            string offlineKey = $"OFF-{_terminalId}-{Guid.NewGuid():N}";
            _offlineSessionKey = offlineKey.Length > 40 ? offlineKey[..40] : offlineKey;
            PosOpenTillEnvelope open = new(_companyId, _terminalId, _cashierUserId, floatAmount, DateTimeOffset.UtcNow, _offlineSessionKey);
            await LocalQueue.EnqueueAsync(new LocalOfflineEntry(Guid.NewGuid(), _companyId, PosOpenTillEnvelope.Kind, JsonSerializer.Serialize(open), open.OpenedUtc)).ConfigureAwait(true);
            PendingOfflineCount = await LocalQueue.CountAsync().ConfigureAwait(true);
            TillSessionId = 0;
            IsSessionOpen = true;
            UpdateSyncState();
            StatusMessage = "Offline: till opened here. Sales queue on this terminal and the till opens centrally when the connection returns.";
            return;
        }

        if (result.IsFailure)
        {
            StatusMessage = result.Error.Message;
            return;
        }

        TillSessionId = result.Value;
        IsSessionOpen = true;
        StatusMessage = $"Till open (session {TillSessionId}). Ready to trade.";
        await SaveSnapshotAsync(TillSessionId).ConfigureAwait(true);

        // The database is reachable (the till just opened) — flush anything captured offline earlier.
        if (PendingOfflineCount > 0)
        {
            await FlushOfflineAsync().ConfigureAwait(true);
        }
    }

    /// <summary>The open till session id (0 when closed).</summary>
    [ObservableProperty]
    private long _tillSessionId;

    // A bottle or crate deposit rides with the item that carries it: the deposit item's own price, untaxed, on its liability account.
    private void AddDepositFor(long? depositItemId, decimal quantity)
    {
        if (depositItemId is long id && _tiles.FirstOrDefault(t => t.ItemId == id) is PosTile deposit)
        {
            _ticket.AddLine(deposit.ItemId, deposit.Name, deposit.UnitPrice, 0m, deposit.RevenueAccountCode, null, quantity);
        }
    }

    /// <summary>Adds a product to the ticket.</summary>
    /// <param name="product">The product tapped.</param>
    [RelayCommand]
    private void AddProduct(PosProductVm? product)
    {
        if (product is null || !IsSessionOpen)
        {
            return;
        }

        _ticket.AddLine(product.ItemId, product.Name, product.Price, product.TaxRate, product.RevenueAccountCode, product.TaxCode);
        AddDepositFor(product.Tile?.DepositItemId, 1m);
        ScanEntry = string.Empty;
        RebuildCart();
    }

    /// <summary>Adds one to a cart line's quantity.</summary>
    /// <param name="row">The cart row.</param>
    [RelayCommand]
    private void IncrementLine(PosCartLineVm? row)
    {
        if (row is null)
        {
            return;
        }

        row.Line.Quantity += 1m;
        RebuildCart();
    }

    /// <summary>Removes one from a cart line's quantity (removing the line at zero).</summary>
    /// <param name="row">The cart row.</param>
    [RelayCommand]
    private void DecrementLine(PosCartLineVm? row)
    {
        if (row is null)
        {
            return;
        }

        row.Line.Quantity -= 1m;
        if (row.Line.Quantity <= 0m)
        {
            _ticket.RemoveLine(row.Line);
        }

        RebuildCart();
    }

    /// <summary>Voids a cart line entirely.</summary>
    /// <param name="row">The cart row.</param>
    [RelayCommand]
    private void VoidLine(PosCartLineVm? row)
    {
        if (row is null)
        {
            return;
        }

        _ticket.RemoveLine(row.Line);
        RebuildCart();
    }

    /// <summary>Appends a digit (or decimal point) to the tender entry.</summary>
    /// <param name="digit">The key pressed.</param>
    [RelayCommand]
    private void AppendDigit(string digit)
    {
        if (digit == "." && TenderEntry.Contains('.', StringComparison.Ordinal))
        {
            return;
        }

        TenderEntry += digit;
    }

    /// <summary>Clears the tender entry.</summary>
    [RelayCommand]
    private void ClearEntry() => TenderEntry = string.Empty;

    /// <summary>Takes a cash tender for the entered amount (or the exact balance when blank).</summary>
    [RelayCommand]
    private void TenderCash() => AddTender(TenderType.Cash, allowOverTender: true);

    /// <summary>Takes a card tender for the entered amount (or the exact balance when blank), through the card terminal when one is connected.</summary>
    [RelayCommand]
    private async Task TenderCardAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService<IDeviceProvider>().CardTerminal is not ICardTerminal terminal || !IsSessionOpen || _ticket.Lines.Count == 0)
        {
            AddTender(TenderType.Card, allowOverTender: false);
            return;
        }

        decimal amount = string.IsNullOrWhiteSpace(TenderEntry) ? _ticket.BalanceDue : TryEntry(out decimal typed) ? Math.Min(typed, _ticket.BalanceDue) : 0m;
        if (amount <= 0m)
        {
            StatusMessage = "Enter a valid amount.";
            return;
        }

        StatusMessage = $"Present the card for {amount:N2}…";
        CardAuthorisation answer = await terminal.ChargeAsync(amount, $"POS{_terminalId}-{DateTime.Now:HHmmss}").ConfigureAwait(true);
        if (!answer.Approved)
        {
            StatusMessage = $"Card declined: {answer.AuthorisationCode}";
            return;
        }

        TenderEntry = amount.ToString(CultureInfo.CurrentCulture);
        AddTender(TenderType.Card, allowOverTender: false);
        StatusMessage = $"Card {answer.MaskedCard} approved ({answer.AuthorisationCode}). " + StatusMessage;
    }

    private void AddTender(TenderType type, bool allowOverTender)
    {
        if (!IsSessionOpen || _ticket.Lines.Count == 0)
        {
            return;
        }

        // A tender in another currency: the amount typed is in that currency (blank: the balance in it), turned into base at today's rate.
        if (SelectedTenderCurrency is { } foreign && foreign.Code != _currency)
        {
            decimal due = _ticket.BalanceDueIn(foreign.Rate);
            decimal given = string.IsNullOrWhiteSpace(TenderEntry) ? due : decimal.TryParse(TenderEntry, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal typed) ? typed : 0m;
            if (!allowOverTender && given > due)
            {
                given = due;
            }

            Result foreignResult = _ticket.AddForeignTender(type, foreign.Code, foreign.Rate, given);
            if (foreignResult.IsFailure)
            {
                StatusMessage = foreignResult.Error.Message;
                return;
            }

            TenderEntry = string.Empty;
            RefreshTotals();
            StatusMessage = (_ticket.IsFullyTendered ? $"Paid. Change {ChangeText} {_currency.Value}. Press Pay to complete." : $"Balance {BalanceText} due.")
                + $" Took {given:N2} {foreign.Code.Value}.";
            return;
        }

        decimal amount;
        if (string.IsNullOrWhiteSpace(TenderEntry))
        {
            amount = _ticket.BalanceDue; // exact remaining balance
        }
        else if (!decimal.TryParse(TenderEntry, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
        {
            StatusMessage = "Enter a valid amount.";
            return;
        }

        // A non-cash tender cannot exceed the balance (only cash gives change).
        if (!allowOverTender && amount > _ticket.BalanceDue)
        {
            amount = _ticket.BalanceDue;
        }

        Result result = _ticket.AddTender(type, amount);
        if (result.IsFailure)
        {
            StatusMessage = result.Error.Message;
            return;
        }

        TenderEntry = string.Empty;
        RefreshTotals();
        StatusMessage = _ticket.IsFullyTendered
            ? $"Paid. Change {ChangeText}. Press Pay to complete."
            : $"Balance {BalanceText} due.";
    }

    /// <summary>Selects a cart line for a discount or price override (tapping it again clears the selection).</summary>
    /// <param name="row">The row tapped.</param>
    [RelayCommand]
    private void SelectLine(PosCartLineVm? row)
    {
        SelectedCartLine = row is null || row.IsSelected ? null : row;
        PosTicketLine? keep = SelectedCartLine?.Line;
        Cart.Clear();
        foreach (PosTicketLine line in _ticket.Lines)
        {
            PosCartLineVm rebuilt = new(line) { IsSelected = ReferenceEquals(line, keep) };
            Cart.Add(rebuilt);
            if (rebuilt.IsSelected)
            {
                SelectedCartLine = rebuilt;
            }
        }
    }

    /// <summary>
    /// Adds a scanned or typed stock item: an embedded weight barcode sells that weight and an embedded price barcode that price; otherwise the
    /// code is looked up (barcode, PLU, pack barcode, item code) and priced from the price list for the quantity on the pad (default 1).
    /// </summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        string code = ScanEntry.Trim();
        ScanEntry = string.Empty;
        if (!IsSessionOpen || code.Length == 0)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IDeviceProvider devices = scope.ServiceProvider.GetRequiredService<IDeviceProvider>();
        EmbeddedBarcode? embedded = Barcodes.TryParseEmbedded(code, devices.BarcodeRules);
        Result<ItemLookup> found;
        try
        {
            found = await scope.ServiceProvider.GetRequiredService<IItemCatalogueService>().LookupAsync(_companyId, embedded?.ItemCode ?? code).ConfigureAwait(true);
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            await ScanOfflineAsync(devices, code, embedded).ConfigureAwait(true);
            return;
        }

        if (found.IsFailure)
        {
            StatusMessage = $"Nothing found for {code}.";
            return;
        }

        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        Item item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == found.Value.ItemId).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(item.RevenueAccountCode))
        {
            StatusMessage = $"{item.Description} has no revenue account; it cannot be sold here.";
            return;
        }

        decimal rate = item.TaxCode is not null && _taxRates.TryGetValue(item.TaxCode, out decimal r) ? r : 0m;
        decimal quantity = embedded?.Kind == EmbeddedValueKind.Weight ? embedded.Value : (TryEntry(out decimal typed) && typed > 0m ? typed : 1m) * found.Value.Factor;
        decimal unitPrice;
        if (embedded?.Kind == EmbeddedValueKind.Price)
        {
            // The label's price is what the customer pays (tax included); sold as one pack.
            unitPrice = PosMoney.Round(embedded.Value / (1m + rate));
            quantity = 1m;
        }
        else
        {
            // The account customer's own price list when one is set on the sale, else the default list.
            long? customerId = await AccountCustomerIdAsync().ConfigureAwait(true);
            Result<ResolvedPrice> price = await scope.ServiceProvider.GetRequiredService<IPricingService>().PriceAsync(_companyId, customerId, item.Id, quantity, DateOnly.FromDateTime(DateTime.Today))
                .ConfigureAwait(true);
            if (price.IsFailure)
            {
                StatusMessage = $"{item.Description}: {price.Error.Message}";
                return;
            }

            unitPrice = price.Value.UnitPrice;
        }

        PosTicketLine line = _ticket.AddLine(item.Id, item.Description, unitPrice, rate, item.RevenueAccountCode, item.TaxCode, quantity);
        AddDepositFor(item.DepositItemId, quantity);
        TenderEntry = string.Empty;
        RebuildCart();
        StatusMessage = $"{item.Description} × {quantity:0.###}";
        await ShowOnDisplayAsync(devices, line.Description, line.LineGross).ConfigureAwait(true);
    }

    // Offline: the code is found in the saved catalogue (barcode, PLU, item code) and sold at its saved price.
    private async Task ScanOfflineAsync(IDeviceProvider devices, string code, EmbeddedBarcode? embedded)
    {
        PosTile? tile = (_snapshot ?? new PosOfflineSnapshot { Tiles = _tiles }).Find(embedded?.ItemCode ?? code);
        if (tile is null)
        {
            StatusMessage = $"Offline: {code} is not in the saved catalogue.";
            return;
        }

        decimal rate = tile.TaxCode is not null && _taxRates.TryGetValue(tile.TaxCode, out decimal r) ? r : 0m;
        decimal quantity = embedded?.Kind == EmbeddedValueKind.Weight ? embedded.Value : TryEntry(out decimal typed) && typed > 0m ? typed : 1m;
        decimal unitPrice = tile.UnitPrice;
        if (embedded?.Kind == EmbeddedValueKind.Price)
        {
            unitPrice = PosMoney.Round(embedded.Value / (1m + rate));
            quantity = 1m;
        }

        PosTicketLine line = _ticket.AddLine(tile.ItemId, tile.Name, unitPrice, rate, tile.RevenueAccountCode, tile.TaxCode, quantity);
        AddDepositFor(tile.DepositItemId, quantity);
        TenderEntry = string.Empty;
        RebuildCart();
        StatusMessage = $"{tile.Name} × {quantity:0.###} (offline price)";
        await ShowOnDisplayAsync(devices, line.Description, line.LineGross).ConfigureAwait(true);
    }

    /// <summary>
    /// Sells or tops up a gift card on the ticket (brief §4.8.1: issued at POS, a liability): the card number in the gift card box and the value on the
    /// number pad become a line on the GIFTCARD item posting to the gift card liability; printed stock activates and an active card tops up when the sale posts.
    /// </summary>
    [RelayCommand]
    private void SellGiftCard()
    {
        string number = (VoucherNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0)
        {
            StatusMessage = "Type or scan the gift card number in the gift card box first.";
            return;
        }

        if (!TryEntry(out decimal value) || value <= 0m)
        {
            StatusMessage = "Type the card's value on the number pad.";
            return;
        }

        if (string.IsNullOrWhiteSpace(GiftCardAccountCode))
        {
            StatusMessage = "Set the gift card liability account under More › account settings first.";
            return;
        }

        PosProductVm? product = _allProducts.FirstOrDefault(p => string.Equals(p.Tile?.Code, "GIFTCARD", StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            StatusMessage = "Add a non-stock item with code GIFTCARD (priced on the till's price list) to sell gift cards.";
            return;
        }

        _ticket.AddGiftCardLine(product.ItemId, number, value, GiftCardAccountCode.Trim());
        TenderEntry = string.Empty;
        VoucherNumber = string.Empty;
        RebuildCart();
        StatusMessage = $"Gift card {number}: {value:N2} on the ticket (printed stock activates, an active card tops up, when the sale posts).";
    }

    /// <summary>Sets the selected line's quantity to the weight on the scale (a settled weight only).</summary>
    [RelayCommand]
    private async Task WeighAsync()
    {
        if (SelectedCartLine is not PosCartLineVm row)
        {
            StatusMessage = "Select the line to weigh.";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IScale? scale = scope.ServiceProvider.GetRequiredService<IDeviceProvider>().Scale;
        ScaleReading? reading = scale is null ? null : await scale.ReadAsync().ConfigureAwait(true);
        if (reading is null || !reading.Stable || reading.Kilograms <= 0m)
        {
            StatusMessage = scale is null ? "No scale is connected to this till." : reading is null ? "The scale did not answer." : "Wait for the weight to settle.";
            return;
        }

        row.Line.Quantity = reading.Kilograms;
        RebuildCart();
        StatusMessage = $"{row.Line.Description}: {reading.Kilograms:0.000} kg";
    }

    /// <summary>Sends the last receipt to the receipt printer.</summary>
    /// <returns>Null when printed, or why not (no printer: the caller saves it to a file instead).</returns>
    public async Task<string?> PrintLastReceiptAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IReceiptPrinter? printer = scope.ServiceProvider.GetRequiredService<IDeviceProvider>().Printer;
        return printer is null ? "No receipt printer." : await printer.PrintAsync(EscPosBuilder.Receipt(LastReceiptText, openDrawer: false)).ConfigureAwait(true);
    }

    private static async Task ShowOnDisplayAsync(IDeviceProvider devices, string label, decimal amount)
    {
        if (devices.Display is ICustomerDisplay display)
        {
            await display.ShowAsync(label.Length > 20 ? label[..20] : label, Infrastructure.Pos.DisplayText.Amount("", amount)).ConfigureAwait(true);
        }
    }

    /// <summary>Takes a mobile-money tender for the entered amount (or the exact balance when blank).</summary>
    [RelayCommand]
    private void TenderMobileMoney() => AddTender(TenderType.MobileMoney, allowOverTender: false);

    /// <summary>Charges the balance to the customer's account (checked against their credit limit when the sale posts).</summary>
    [RelayCommand]
    private async Task TenderAccountAsync()
    {
        if (!IsSessionOpen || _ticket.Lines.Count == 0)
        {
            return;
        }

        if (await AccountCustomerIdAsync().ConfigureAwait(true) is not long)
        {
            StatusMessage = string.IsNullOrWhiteSpace(AccountCustomer) ? "Type the customer's account code first." : $"No customer {AccountCustomer.Trim().ToUpperInvariant()}.";
            return;
        }

        Result added = _ticket.AddTender(TenderType.Credit, string.IsNullOrWhiteSpace(TenderEntry) ? _ticket.BalanceDue : TryEntry(out decimal typed) ? Math.Min(typed, _ticket.BalanceDue) : 0m);
        if (added.IsFailure)
        {
            StatusMessage = added.Error.Message;
            return;
        }

        TenderEntry = string.Empty;
        RefreshTotals();
        StatusMessage = _ticket.IsFullyTendered ? $"Charged to {AccountCustomer.Trim().ToUpperInvariant()}. Press Pay to complete." : $"Balance {BalanceText} due.";
    }

    private async Task<long?> AccountCustomerIdAsync()
    {
        if (string.IsNullOrWhiteSpace(AccountCustomer))
        {
            return null;
        }

        string code = AccountCustomer.Trim().ToUpperInvariant();
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Customers.AsNoTracking()
            .Where(c => c.CompanyId == _companyId && c.Code.ToUpper() == code).Select(c => (long?)c.Id).FirstOrDefaultAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Pays from a gift card: the entered amount (or the balance due) up to what the card holds; the card is charged only when the sale
    /// completes, and gives no change.
    /// </summary>
    [RelayCommand]
    private async Task TenderVoucherAsync()
    {
        if (!IsSessionOpen || _ticket.Lines.Count == 0)
        {
            return;
        }

        string number = VoucherNumber.Trim().ToUpperInvariant();
        if (number.Length == 0)
        {
            StatusMessage = "Scan or type the gift card number first.";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<Application.Abstractions.Vouchers.GiftCardResult> card = await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Vouchers.IVoucherService>()
            .GetBalanceAsync(_companyId, number).ConfigureAwait(true);
        if (card.IsFailure)
        {
            StatusMessage = card.Error.Message;
            return;
        }

        decimal alreadyTaken = _ticket.Tenders.Where(t => t.VoucherNumber == number).Sum(t => t.Amount);
        decimal available = card.Value.Balance - alreadyTaken;
        decimal wanted = string.IsNullOrWhiteSpace(TenderEntry) ? _ticket.BalanceDue : TryEntry(out decimal typed) ? typed : 0m;
        decimal amount = Math.Min(Math.Min(wanted, _ticket.BalanceDue), available);
        if (amount <= 0m)
        {
            StatusMessage = available <= 0m ? $"Gift card {number} has nothing left to spend." : "Enter a valid amount.";
            return;
        }

        Result added = _ticket.AddTender(TenderType.Voucher, amount, number);
        if (added.IsFailure)
        {
            StatusMessage = added.Error.Message;
            return;
        }

        TenderEntry = string.Empty;
        VoucherNumber = string.Empty;
        RefreshTotals();
        StatusMessage = (_ticket.IsFullyTendered ? "Paid. Press Pay to complete." : $"Balance {BalanceText} due.") + $" Gift card {number}: {available - amount:N2} left after this sale.";
    }

    /// <summary>Discounts the selected line by the percentage on the pad (a supervisor above the cashier's limit).</summary>
    [RelayCommand]
    private async Task DiscountLineAsync()
    {
        if (SelectedCartLine is not PosCartLineVm row || !TryEntry(out decimal percent))
        {
            StatusMessage = "Select a line and type the discount % on the pad.";
            return;
        }

        long? supervisor = percent > UnsupervisedDiscountLimit ? await SupervisorAsync("Pos.Sale", OperationFlags.OverrideDiscount).ConfigureAwait(true) : null;
        if (percent > UnsupervisedDiscountLimit && supervisor is null)
        {
            return;
        }

        decimal before = row.Line.LineNet;
        Result applied = _ticket.DiscountLine(row.Line, percent, UnsupervisedDiscountLimit, supervisor is not null);
        await AfterAdjustmentAsync(applied, PosEventKind.LineDiscount, $"{row.Line.Description}: {percent:0.##}% off", row.Line.LineNet, before, supervisor).ConfigureAwait(true);
    }

    /// <summary>Discounts the whole sale by the percentage on the pad.</summary>
    [RelayCommand]
    private async Task DiscountSaleAsync()
    {
        if (_ticket.Lines.Count == 0 || !TryEntry(out decimal percent))
        {
            StatusMessage = "Type the sale discount % on the pad.";
            return;
        }

        long? supervisor = percent > UnsupervisedDiscountLimit ? await SupervisorAsync("Pos.Sale", OperationFlags.OverrideDiscount).ConfigureAwait(true) : null;
        if (percent > UnsupervisedDiscountLimit && supervisor is null)
        {
            return;
        }

        decimal before = _ticket.SubtotalNet;
        Result applied = _ticket.DiscountSale(percent, UnsupervisedDiscountLimit, supervisor is not null);
        await AfterAdjustmentAsync(applied, PosEventKind.LineDiscount, $"Sale discount {percent:0.##}%", _ticket.SubtotalNet, before, supervisor).ConfigureAwait(true);
    }

    /// <summary>Overrides the selected line's unit price with the price on the pad (always supervised).</summary>
    [RelayCommand]
    private async Task OverridePriceAsync()
    {
        if (SelectedCartLine is not PosCartLineVm row || !TryEntry(out decimal price))
        {
            StatusMessage = "Select a line and type the new price on the pad.";
            return;
        }

        if (await SupervisorAsync("Pos.PriceOverride", OperationFlags.OverridePrice).ConfigureAwait(true) is not long supervisor)
        {
            return;
        }

        decimal before = row.Line.UnitPriceNet;
        Result applied = _ticket.OverridePrice(row.Line, price, supervised: true);
        await AfterAdjustmentAsync(applied, PosEventKind.PriceOverride, $"{row.Line.Description}: price {before:N2} → {price:N2}", price, before, supervisor).ConfigureAwait(true);
    }

    /// <summary>Parks the current sale under a name so another customer can be served.</summary>
    [RelayCommand]
    private async Task ParkAsync()
    {
        if (!IsSessionOpen || _ticket.Lines.Count == 0 || _ticket.Tenders.Count > 0)
        {
            StatusMessage = _ticket.Tenders.Count > 0 ? "Clear the tenders before parking." : "Nothing to park.";
            return;
        }

        string tag = string.IsNullOrWhiteSpace(ParkTag) ? $"Park {DateTime.Now:HH:mm}" : ParkTag.Trim();
        if (!_online)
        {
            // Offline: the tab is sealed on this computer and listed with the others.
            await LocalParked.ParkAsync(new LocalParkedSale(Guid.NewGuid(), tag, DateTimeOffset.Now, _ticket.SaleLines(NullIfBlank(DiscountAccountCode)))).ConfigureAwait(true);
            _ticket.Clear();
            ParkTag = string.Empty;
            RebuildCart();
            await RefreshParkedAsync().ConfigureAwait(true);
            StatusMessage = $"Sale parked on this till as \"{tag}\" (offline).";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<long> parked = await scope.ServiceProvider.GetRequiredService<IParkedSaleService>()
            .ParkAsync(_companyId, _terminalId, tag, _cashierUserId, _ticket.SaleLines(NullIfBlank(DiscountAccountCode))).ConfigureAwait(true);
        if (parked.IsFailure)
        {
            StatusMessage = parked.Error.Message;
            return;
        }

        _ticket.Clear();
        ParkTag = string.Empty;
        RebuildCart();
        await RefreshParkedAsync().ConfigureAwait(true);
        StatusMessage = $"Sale parked as \"{tag}\".";
    }

    /// <summary>Brings the chosen parked sale back onto the (empty) ticket.</summary>
    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (SelectedParkedSale is not ParkedSaleSummary chosen)
        {
            StatusMessage = "Choose a parked sale.";
            return;
        }

        if (!_ticket.IsEmpty)
        {
            StatusMessage = "Finish or park the current sale first.";
            return;
        }

        if (chosen.Id < 0 && _localTabs.TryGetValue(chosen.Id, out Guid localId))
        {
            // A tab parked here offline.
            LocalParkedSale? local = await LocalParked.TakeAsync(localId).ConfigureAwait(true);
            Result restored = local is null ? Result.Failure(Error.NotFound("Pos.ParkedGone", "That tab is no longer on this till."))
                : _ticket.Load(local.Lines, l => l.LineNet == 0m ? 0m : Math.Round(l.TaxAmount / l.LineNet, 4));
            StatusMessage = restored.IsSuccess ? $"Resumed \"{chosen.Tag}\"." : restored.Error.Message;
            RebuildCart();
            await RefreshParkedAsync().ConfigureAwait(true);
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<IReadOnlyList<PosSaleLineRequest>> lines = await scope.ServiceProvider.GetRequiredService<IParkedSaleService>()
            .ResumeAsync(_companyId, chosen.Id, _terminalId, _cashierUserId).ConfigureAwait(true);
        Result loaded = lines.IsSuccess ? _ticket.Load(lines.Value, l => l.LineNet == 0m ? 0m : Math.Round(l.TaxAmount / l.LineNet, 4)) : Result.Failure(lines.Error);
        StatusMessage = loaded.IsSuccess ? $"Resumed \"{chosen.Tag}\"." : loaded.Error.Message;
        RebuildCart();
        await RefreshParkedAsync().ConfigureAwait(true);
    }

    /// <summary>Reloads the parked sales.</summary>
    [RelayCommand]
    private async Task RefreshParkedAsync()
    {
        IReadOnlyList<ParkedSaleSummary> parked = [];
        if (_online)
        {
            try
            {
                await using AsyncServiceScope scope = _services.CreateAsyncScope();
                parked = await scope.ServiceProvider.GetRequiredService<IParkedSaleService>().ParkedAsync(_companyId).ConfigureAwait(true);
            }
            catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
            {
                _online = false;
            }
        }

        // The tabs parked on this till while offline, listed with negative ids.
        _localTabs.Clear();
        List<ParkedSaleSummary> local = [];
        foreach (LocalParkedSale tab in await LocalParked.ListAsync().ConfigureAwait(true))
        {
            long id = -(local.Count + 1);
            _localTabs[id] = tab.Id;
            local.Add(new ParkedSaleSummary(id, $"{tab.Tag} (this till)", _terminalId, tab.Lines.Count, tab.Total, tab.ParkedAt));
        }

        ParkedSales.Clear();
        foreach (ParkedSaleSummary sale in parked.Concat(local))
        {
            ParkedSales.Add(sale);
        }
    }

    private readonly Dictionary<long, Guid> _localTabs = [];

    private ILocalParkedSaleStore LocalParked => _services.GetRequiredService<ILocalParkedSaleStore>();

    // A supervisor's user name and PIN, checked now: they must hold the permission and not be the cashier.
    private async Task<long?> SupervisorAsync(string permission, OperationFlags needed)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        AuthenticationResult supervisor = await IdentifyPersonAsync(scope).ConfigureAwait(true);
        if (!supervisor.Succeeded || supervisor.UserId == _cashierUserId || !supervisor.Permissions.Has(permission, needed))
        {
            StatusMessage = !supervisor.Succeeded ? supervisor.Message ?? "Supervisor not recognised."
                : supervisor.UserId == _cashierUserId ? "A cashier cannot authorise their own override." : "That supervisor may not authorise this.";
            return null;
        }

        return supervisor.UserId;
    }

    /// <summary>Raised when the supervisor PIN has been used and the PIN box should be emptied.</summary>
    public event Action? SupervisorPinCleared;

    // Shows the adjustment and logs it as a till event (who, what, authorised by whom).
    private async Task AfterAdjustmentAsync(Result applied, PosEventKind kind, string description, decimal amount, decimal original, long? supervisor)
    {
        if (applied.IsFailure)
        {
            StatusMessage = applied.Error.Message;
            return;
        }

        TenderEntry = string.Empty;
        RebuildCart();
        StatusMessage = description + (supervisor is null ? "." : " (authorised).");
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IPosEventService>().RecordAsync(new PosEventRequest
            {
                CompanyId = _companyId, TerminalId = _terminalId, TillSessionId = TillSessionId, CashierUserId = _cashierUserId, Kind = kind, Date = DateOnly.FromDateTime(DateTime.Today),
                Description = description, Amount = amount, OriginalAmount = original, AuthorisedByUserId = supervisor,
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            // The adjustment stands; the event log catches up with the next online action.
        }
    }

    private bool TryEntry(out decimal value)
        => decimal.TryParse(TenderEntry, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Clears all tenders taken so far, keeping the ticket lines.</summary>
    [RelayCommand]
    private void ClearTenders()
    {
        _ticket.ClearTenders();
        RefreshTotals();
        StatusMessage = "Tenders cleared.";
    }

    /// <summary>Completes the sale: builds a balanced request and posts it.</summary>
    [RelayCommand]
    private async Task PayAsync()
    {
        if (!IsSessionOpen || !_ticket.IsFullyTendered)
        {
            StatusMessage = _ticket.Lines.Count == 0 ? "Add items first." : $"Balance {BalanceText} still due.";
            return;
        }

        string saleNumber = $"POS{_terminalId}-{DateTime.Now:yyMMddHHmmss}-{++_saleSequence}";
        PosChangeQuote? quote = await ChangeQuoteAsync().ConfigureAwait(true);
        PosSaleContext context = SaleContext(saleNumber) with { CustomerId = await AccountCustomerIdAsync().ConfigureAwait(true), Change = quote };
        Result<CompleteSaleRequest> build = _ticket.BuildSale(context);
        if (build.IsFailure)
        {
            StatusMessage = build.Error.Message;
            return;
        }

        CompleteSaleRequest request = build.Value;
        decimal change = _ticket.ChangeDue;
        await CompleteAsync(request, saleNumber, change, quote is null ? null : $"{quote.Amount.ToString("N2", CultureInfo.CurrentCulture)} {quote.Currency.Value}").ConfigureAwait(true);
    }

    /// <summary>The basket to split among payers, or null (with the reason shown) when there is nothing to split.</summary>
    /// <returns>The basket's lines.</returns>
    public IReadOnlyList<PosSaleLineRequest>? SplitBasket()
    {
        if (!IsSessionOpen || _ticket.Lines.Count == 0)
        {
            StatusMessage = "Add items first.";
            return null;
        }

        Result<CompleteSaleRequest> basket = _ticket.BuildBasket(SaleContext("split"));
        if (basket.IsFailure)
        {
            StatusMessage = basket.Error.Message;
            return null;
        }

        return basket.Value.Lines;
    }

    /// <summary>
    /// Completes a split bill: each payer's share becomes its own sale, paid in full by the payer's tender (cash and card take the exact
    /// amount), all posted together; their receipts follow one another. Needs the connection.
    /// </summary>
    /// <param name="parts">Each payer's shares of the lines.</param>
    /// <param name="payments">How each payer pays.</param>
    /// <returns>A task.</returns>
    public async Task CompleteSplitAsync(IReadOnlyList<IReadOnlyList<SplitShare>> parts, IReadOnlyList<SplitPayment> payments)
    {
        string saleNumber = $"POS{_terminalId}-{DateTime.Now:yyMMddHHmmss}-{++_saleSequence}";
        PosSaleContext context = SaleContext(saleNumber);
        Result<CompleteSaleRequest> basket = _ticket.BuildBasket(context);
        Result<IReadOnlyList<IReadOnlyList<PosSaleLineRequest>>> plan = basket.IsSuccess ? SplitBill.Plan(basket.Value.Lines, parts) : basket.Error;
        if (plan.IsFailure)
        {
            StatusMessage = plan.Error.Message;
            return;
        }

        List<IReadOnlyList<PosTenderRequest>> tenders = [];
        for (int p = 0; p < parts.Count; p++)
        {
            if (!context.TenderAccounts.TryGetValue(payments[p].TenderType, out string? account))
            {
                StatusMessage = $"No GL account is configured for {payments[p].TenderType} tenders.";
                return;
            }

            tenders.Add([new PosTenderRequest(payments[p].TenderType, _currency, SplitBill.Gross(plan.Value[p]), account, IsChange: false, VoucherNumber: payments[p].VoucherNumber)]);
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<IReadOnlyList<CompleteSaleResult>> result = await scope.ServiceProvider.GetRequiredService<IPosService>()
                .SplitBillAsync(new SplitBillRequest { Basket = basket.Value, Parts = parts, Tenders = tenders }).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = $"Split bill not posted: {result.Error.Message}";
                return;
            }

            IPosReceiptService receipts = scope.ServiceProvider.GetRequiredService<IPosReceiptService>();
            List<string> slips = [];
            foreach (CompleteSaleResult sale in result.Value)
            {
                Result<string> receipt = await receipts.RenderReceiptTextAsync(context.CompanyId, sale.SaleId).ConfigureAwait(true);
                if (receipt.IsSuccess)
                {
                    slips.Add(receipt.Value);
                }
            }

            LastReceiptText = string.Join(Environment.NewLine + new string('-', 40) + Environment.NewLine, slips);
            IDeviceProvider devices = scope.ServiceProvider.GetRequiredService<IDeviceProvider>();
            string? printed = null;
            if (devices.Printer is IReceiptPrinter printer)
            {
                foreach (string slip in slips)
                {
                    printed ??= await printer.PrintAsync(EscPosBuilder.Receipt(slip, false)).ConfigureAwait(true);
                }
            }

            _ticket.Clear();
            AgeVerified = false;
            PrescriptionReference = string.Empty;
            RebuildCart();
            StatusMessage = $"Bill split into {result.Value.Count} sales ({string.Join(", ", result.Value.Select(s => s.TotalGross.ToString("N2", CultureInfo.CurrentCulture)))})."
                + (printed is null ? string.Empty : $" Receipts not printed: {printed}");
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            StatusMessage = "A split bill needs the connection; take one payment for the whole bill to sell offline.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Split bill not posted: {ex.Message}";
        }
    }

    // The sale's session, accounts and checks as the till is set up now.
    private PosSaleContext SaleContext(string saleNumber)
    {
        Dictionary<TenderType, string> tenderAccounts = new();
        if (!string.IsNullOrWhiteSpace(CashAccountCode))
        {
            tenderAccounts[TenderType.Cash] = CashAccountCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(CardAccountCode))
        {
            tenderAccounts[TenderType.Card] = CardAccountCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(MobileMoneyAccountCode))
        {
            tenderAccounts[TenderType.MobileMoney] = MobileMoneyAccountCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(GiftCardAccountCode))
        {
            tenderAccounts[TenderType.Voucher] = GiftCardAccountCode.Trim();
        }

        return new(
            _companyId, _terminalId, TillSessionId, _cashierUserId,
            saleNumber, DateOnly.FromDateTime(DateTime.Today), _warehouseId, _currency,
            TaxAccountCode: string.IsNullOrWhiteSpace(TaxAccountCode) ? null : TaxAccountCode.Trim(),
            ChangeAccountCode: CashAccountCode.Trim(),
            TenderAccounts: tenderAccounts,
            AgeVerified: AgeVerified,
            PrescriptionReference: string.IsNullOrWhiteSpace(PrescriptionReference) ? null : PrescriptionReference.Trim(),
            DiscountAccountCode: NullIfBlank(DiscountAccountCode),
            TipsAccountCode: NullIfBlank(TipsAccountCode));
    }

    // The change in the configured change currency (null: give it in base, or none is owed).
    private async Task<PosChangeQuote?> ChangeQuoteAsync()
    {
        if (_ticket.ChangeDue <= 0m)
        {
            return null;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<PosChangeQuote?> quote = await scope.ServiceProvider.GetRequiredService<IPosService>().ChangeQuoteAsync(_companyId, _ticket.ChangeDue, DateOnly.FromDateTime(DateTime.Today))
            .ConfigureAwait(true);
        return quote.IsSuccess ? quote.Value : null;
    }

    private async Task CompleteAsync(CompleteSaleRequest request, string saleNumber, decimal change, string? changeShown = null)
    {
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            Result<CompleteSaleResult> result = await pos.CompleteSaleAsync(request).ConfigureAwait(true);
            if (result.IsFailure)
            {
                // A domain failure (e.g. closed period) would recur on replay, so it is surfaced, not queued.
                StatusMessage = $"Sale not posted: {result.Error.Message}";
                return;
            }

            string loyalty = string.Empty;
            if (NullIfBlank(LoyaltyMember) is string member)
            {
                Result<LoyaltyEarned> earned = await scope.ServiceProvider.GetRequiredService<ILoyaltyService>()
                    .EarnAsync(request.CompanyId, member, result.Value.TotalGross, request.SaleDate, saleNumber).ConfigureAwait(true);
                loyalty = earned.IsSuccess ? $" {member} earned {earned.Value.Points:0.##} point(s), balance {earned.Value.Balance:0.##}." : $" Loyalty not earned: {earned.Error.Message}";
                LoyaltyMember = string.Empty;
            }

            IPosReceiptService receipts = scope.ServiceProvider.GetRequiredService<IPosReceiptService>();
            Result<string> receipt = await receipts.RenderReceiptTextAsync(request.CompanyId, result.Value.SaleId).ConfigureAwait(true);
            LastReceiptText = receipt.IsSuccess ? receipt.Value : string.Empty;
            IDeviceProvider devices = scope.ServiceProvider.GetRequiredService<IDeviceProvider>();
            bool cashTaken = request.Tenders.Any(t => t.TenderType == TenderType.Cash);
            string? printed = devices.Printer is IReceiptPrinter printer && LastReceiptText.Length > 0
                ? await printer.PrintAsync(EscPosBuilder.Receipt(LastReceiptText, cashTaken && devices.Drawer is not null)).ConfigureAwait(true)
                : null;
            await ShowOnDisplayAsync(devices, "Thank you - change", change).ConfigureAwait(true);

            _ticket.Clear();
            _coupons.Clear();
            AgeVerified = false;
            PrescriptionReference = string.Empty;
            AccountCustomer = string.Empty;
            RebuildCart();
            StatusMessage = $"Sale {saleNumber} posted (journal {result.Value.JournalNumber}). Change {changeShown ?? change.ToString("N2", CultureInfo.CurrentCulture)}.{loyalty}{(printed is null ? string.Empty : $" Receipt not printed: {printed}")}";
            if (PendingOfflineCount > 0)
            {
                await FlushOfflineAsync().ConfigureAwait(true); // the connection is back: sales queued while offline sync now
            }
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex) && request.Tenders.Any(t => t.TenderType == TenderType.Voucher))
        {
            // A gift card's balance cannot be checked or charged offline, so a voucher sale is not queued.
            StatusMessage = "Offline: gift cards cannot be taken without the connection. Clear the voucher tender and take another payment.";
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex)
            && Infrastructure.Licensing.LicenceFeatureGate.Check(_services.GetService<Application.Abstractions.Licensing.ILicenceService>(), One4All.Licensing.Core.LicenceFeatures.OfflinePos,
                "Selling offline") is { IsFailure: true } unlicensed)
        {
            StatusMessage = $"Offline, and {unlicensed.Error.Message}";
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            // The central database is unreachable — capture the sale locally and sync on reconnect.
            await CaptureOfflineAsync(request).ConfigureAwait(true);
            _ticket.Clear();
            AgeVerified = false;
            PrescriptionReference = string.Empty;
            RebuildCart();
            StatusMessage = $"Offline: sale {saleNumber} saved locally ({PendingOfflineCount} queued). It will sync when the connection returns.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sale not posted: {ex.Message}";
        }
    }

    private async Task CaptureOfflineAsync(CompleteSaleRequest request)
    {
        PosSaleEnvelope envelope = new(
            request.CompanyId, request.TerminalId, request.TillSessionId, request.CashierUserId,
            request.SaleNumber, request.SaleDate, request.WarehouseId, _currency.Value, request.TaxAccountCode,
            request.Lines.Select(l => new PosSaleLineEnvelope(l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.LineNet, l.TaxAmount, l.RevenueAccountCode, l.GiftCardNumber)).ToList(),
            request.Tenders.Select(t => new PosTenderEnvelope(t.TenderType, t.Currency.Value, t.Amount, t.AccountCode, t.IsChange)).ToList(),
            request.AgeVerified, request.PrescriptionReference, request.TillSessionId == 0 ? _offlineSessionKey : null, request.Tip, request.TipsAccountCode);

        LocalOfflineEntry entry = new(Guid.NewGuid(), request.CompanyId, PosSaleEnvelope.Kind, JsonSerializer.Serialize(envelope), DateTimeOffset.UtcNow);
        await LocalQueue.EnqueueAsync(entry).ConfigureAwait(true);
        PendingOfflineCount = await LocalQueue.CountAsync().ConfigureAwait(true);
        UpdateSyncState();
    }

    // The key of a till opened offline, until it has synced and has a session id.
    private string? _offlineSessionKey;

    // When the terminal last synced its queue with the central database.
    private DateTimeOffset? _lastSyncedUtc;

    /// <summary>Where the terminal's offline queue stands: online or offline, how much is queued and when it last synced.</summary>
    [ObservableProperty]
    private string _syncState = string.Empty;

    private void UpdateSyncState()
    {
        string last = _lastSyncedUtc is DateTimeOffset at ? $"last synced {at.ToLocalTime():HH:mm}" : "not synced yet";
        SyncState = PendingOfflineCount > 0 || _offlineSessionKey is not null
            ? $"OFFLINE • {PendingOfflineCount} queued • {last}" + (_offlineSessionKey is null ? string.Empty : " • till opened offline")
            : _lastSyncedUtc is null ? string.Empty : $"Online • {last}";
    }

    /// <summary>Hands locally-captured sales to the central outbox and drains it (used on reconnect).</summary>
    [RelayCommand]
    private async Task FlushOfflineAsync()
    {
        IReadOnlyList<LocalOfflineEntry> entries = await LocalQueue.ListAsync().ConfigureAwait(true);
        if (entries.Count == 0)
        {
            PendingOfflineCount = 0;
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IOfflineOutbox outbox = scope.ServiceProvider.GetRequiredService<IOfflineOutbox>();
            foreach (LocalOfflineEntry entry in entries)
            {
                // Hand off to the durable central outbox, then drop the local copy (enqueue is idempotent).
                await outbox.EnqueueAsync(new OfflineOperation(entry.CompanyId, entry.OperationId, entry.Kind, entry.PayloadJson, entry.CapturedUtc)).ConfigureAwait(true);
                await LocalQueue.RemoveAsync(entry.OperationId).ConfigureAwait(true);
            }

            IOfflineSyncService sync = scope.ServiceProvider.GetRequiredService<IOfflineSyncService>();
            OfflineSyncReport report = await sync.SyncPendingAsync().ConfigureAwait(true);
            _lastSyncedUtc = DateTimeOffset.UtcNow;
            StatusMessage = $"Synced {report.Synced} offline operation(s)"
                + (report.DeadLettered > 0 ? $", {report.DeadLettered} could not be posted" : string.Empty) + ".";

            // A till opened offline now has its central session: carry on trading in it.
            if (_offlineSessionKey is string key)
            {
                One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
                if (await db.Set<TillSession>().Where(s => s.CompanyId == _companyId && s.OfflineKey == key).Select(s => (long?)s.Id).FirstOrDefaultAsync().ConfigureAwait(true) is long id)
                {
                    TillSessionId = id;
                    _offlineSessionKey = null;
                    await SaveSnapshotAsync(id).ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            StatusMessage = $"Still offline — {PendingOfflineCount} sale(s) remain queued.";
        }
        finally
        {
            PendingOfflineCount = await LocalQueue.CountAsync().ConfigureAwait(true);
            UpdateSyncState();
        }
    }

    /// <summary>Voids a completed sale (its number typed in the scan box) with a supervisor's authority: the sale and its stock are reversed.</summary>
    [RelayCommand]
    private async Task VoidSaleAsync()
    {
        if (!_online)
        {
            await VoidOfflineAsync().ConfigureAwait(true);
            return;
        }

        if (await CompletedSaleAsync().ConfigureAwait(true) is not Sale sale || await SupervisorAsync("Pos.Void", OperationFlags.VoidReverse).ConfigureAwait(true) is not long supervisor)
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<string> voided = await scope.ServiceProvider.GetRequiredService<IPosService>()
                .VoidSaleAsync(_companyId, sale.Id, DateOnly.FromDateTime(DateTime.Today), $"Voided at till {_terminalId}", TaxAccountCode.Length > 0 ? TaxAccountCode : null).ConfigureAwait(true);
            StatusMessage = voided.IsSuccess ? $"Sale {sale.SaleNumber} voided (journal {voided.Value})." : voided.Error.Message;
            ScanEntry = string.Empty;
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            // The connection went while voiding: queue it, like a sale.
            await QueueVoidAsync(sale.SaleNumber, supervisor).ConfigureAwait(true);
        }
    }

    // Offline: only a sale rung up on this terminal while offline (still in its queue) can be voided here; the void follows it to the server.
    private async Task VoidOfflineAsync()
    {
        if (await QueuedSaleAsync().ConfigureAwait(true) is not PosSaleEnvelope sale || await OfflineSupervisorAsync(s => s.CanVoid, "void").ConfigureAwait(true) is not long supervisor)
        {
            return;
        }

        await QueueVoidAsync(sale.SaleNumber, supervisor).ConfigureAwait(true);
    }

    private async Task QueueVoidAsync(string saleNumber, long supervisor)
    {
        PosVoidEnvelope envelope = new(_companyId, saleNumber, DateOnly.FromDateTime(DateTime.Today), $"Voided at till {_terminalId} (offline)", TaxAccountCode.Length > 0 ? TaxAccountCode : null, supervisor);
        await LocalQueue.EnqueueAsync(new LocalOfflineEntry(Guid.NewGuid(), _companyId, PosVoidEnvelope.Kind, JsonSerializer.Serialize(envelope), DateTimeOffset.UtcNow)).ConfigureAwait(true);
        PendingOfflineCount = await LocalQueue.CountAsync().ConfigureAwait(true);
        UpdateSyncState();
        ScanEntry = string.Empty;
        StatusMessage = $"Offline: the void of sale {saleNumber} is queued ({PendingOfflineCount} queued) and posts when the connection returns.";
    }

    // The sale whose number is in the scan box, from this terminal's offline queue.
    private async Task<PosSaleEnvelope?> QueuedSaleAsync()
    {
        string number = (ScanEntry ?? string.Empty).Trim();
        if (number.Length == 0)
        {
            StatusMessage = "Type (or scan) the sale number from the receipt in the scan box first.";
            return null;
        }

        foreach (LocalOfflineEntry entry in await LocalQueue.ListAsync().ConfigureAwait(true))
        {
            if (entry.Kind == PosSaleEnvelope.Kind && JsonSerializer.Deserialize<PosSaleEnvelope>(entry.PayloadJson) is PosSaleEnvelope sale
                && string.Equals(sale.SaleNumber, number, StringComparison.OrdinalIgnoreCase))
            {
                return sale;
            }
        }

        StatusMessage = $"Offline: sale {number} was not rung up on this terminal while offline, so it waits for the connection.";
        return null;
    }

    // A supervisor verified against the sealed snapshot (name and PIN only; cards and fingerprints need the connection).
    private async Task<long?> OfflineSupervisorAsync(Func<OfflineSupervisor, bool> allowed, string what)
    {
        string name = SupervisorName?.Trim() ?? string.Empty;
        string pin = SupervisorPin ?? string.Empty;
        SupervisorPin = string.Empty;
        SupervisorPinCleared?.Invoke();
        if (name.Length == 0 || pin.Length == 0)
        {
            StatusMessage = $"Offline: a supervisor's name and PIN authorise a {what} here.";
            return null;
        }

        IPasswordHasher hasher = _services.GetRequiredService<IPasswordHasher>();
        OfflineSupervisor? supervisor = (_snapshot?.Supervisors ?? []).FirstOrDefault(s => string.Equals(s.Username, name, StringComparison.OrdinalIgnoreCase));
        if (supervisor is null || !hasher.Verify(supervisor.PinHash, pin))
        {
            StatusMessage = "Supervisor not recognised.";
            return null;
        }

        if (supervisor.UserId == _cashierUserId)
        {
            StatusMessage = "A cashier cannot authorise their own override.";
            return null;
        }

        if (!allowed(supervisor))
        {
            StatusMessage = "That supervisor may not authorise this.";
            return null;
        }

        await Task.CompletedTask.ConfigureAwait(true);
        return supervisor.UserId;
    }

    /// <summary>Refunds a completed sale in full (its number typed in the scan box) in cash, with a supervisor's authority.</summary>
    [RelayCommand]
    private async Task RefundSaleAsync()
    {
        if (!_online)
        {
            if (await QueuedSaleAsync().ConfigureAwait(true) is PosSaleEnvelope queued && await OfflineSupervisorAsync(s => s.CanRefund, "refund").ConfigureAwait(true) is long authoriser)
            {
                await QueueRefundAsync(queued.SaleNumber, authoriser).ConfigureAwait(true);
            }

            return;
        }

        if (await CompletedSaleAsync().ConfigureAwait(true) is not Sale sale || await SupervisorAsync("Pos.Refund", OperationFlags.AuthoriseLevel1).ConfigureAwait(true) is not long supervisor)
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<PosRefundResult> refunded = await scope.ServiceProvider.GetRequiredService<IPosEventService>().RefundAsync(new PosRefundRequest
            {
                CompanyId = _companyId, SaleId = sale.Id, TerminalId = _terminalId, CashierUserId = _cashierUserId, AuthorisedByUserId = supervisor, Date = DateOnly.FromDateTime(DateTime.Today),
                RefundAccountCode = CashAccountCode, TaxAccountCode = TaxAccountCode.Length > 0 ? TaxAccountCode : null, Reason = "Refund at the till",
                Lines = [.. sale.Lines.Select(l => new PosRefundLine(l.Id, l.Quantity))],
            }).ConfigureAwait(true);
            StatusMessage = refunded.IsSuccess ? $"Sale {sale.SaleNumber} refunded: {refunded.Value.Gross:N2} paid out in cash." : refunded.Error.Message;
            ScanEntry = string.Empty;
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            await QueueRefundAsync(sale.SaleNumber, supervisor).ConfigureAwait(true);
        }
    }

    private async Task QueueRefundAsync(string saleNumber, long supervisor)
    {
        string reference = $"OFR-{Guid.NewGuid():N}"[..16];
        PosRefundEnvelope envelope = new(_companyId, saleNumber, _terminalId, _cashierUserId, supervisor, DateOnly.FromDateTime(DateTime.Today), CashAccountCode,
            TaxAccountCode.Length > 0 ? TaxAccountCode : null, $"Refund at the till (offline {reference})", TillSessionId, TillSessionId == 0 ? _offlineSessionKey : null);
        await LocalQueue.EnqueueAsync(new LocalOfflineEntry(Guid.NewGuid(), _companyId, PosRefundEnvelope.Kind, JsonSerializer.Serialize(envelope), DateTimeOffset.UtcNow)).ConfigureAwait(true);
        PendingOfflineCount = await LocalQueue.CountAsync().ConfigureAwait(true);
        UpdateSyncState();
        ScanEntry = string.Empty;
        StatusMessage = $"Offline: the cash refund of sale {saleNumber} is queued ({PendingOfflineCount} queued); pay it out and it posts when the connection returns.";
    }

    // The completed sale whose number is in the scan box.
    private async Task<Sale?> CompletedSaleAsync()
    {
        string number = (ScanEntry ?? string.Empty).Trim();
        if (number.Length == 0 || !_online)
        {
            StatusMessage = "Type (or scan) the sale number from the receipt in the scan box first.";
            return null;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Sale? sale = await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Set<Sale>().AsNoTracking().Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.CompanyId == _companyId && s.SaleNumber == number).ConfigureAwait(true);
        if (sale is null)
        {
            StatusMessage = $"No sale {number}.";
        }

        return sale;
    }

    /// <summary>Abandons the current ticket.</summary>
    [RelayCommand]
    private void NewSale()
    {
        _ticket.Clear();
        AgeVerified = false;
        PrescriptionReference = string.Empty;
        TenderEntry = string.Empty;
        RebuildCart();
        StatusMessage = "New sale.";
    }

    /// <summary>Produces an X-read (mid-shift) for the open session.</summary>
    [RelayCommand]
    private async Task XReadAsync()
    {
        if (!IsSessionOpen)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        Result<TillRead> result = await pos.XReadAsync(TillSessionId).ConfigureAwait(true);
        StatusMessage = result.IsFailure
            ? result.Error.Message
            : $"X-read: {result.Value.SaleCount} sale(s), gross {result.Value.TotalGross:N2}.";
    }

    /// <summary>Closes the session with a Z-read.</summary>
    [RelayCommand]
    private async Task CloseTillAsync()
    {
        if (!IsSessionOpen)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();

        // The count ("120" or "120; ZAR 500; EUR 20") is declared before anything expected is shown, which is what a blind till needs.
        bool doCashUp = !string.IsNullOrWhiteSpace(CountedCash)
            && !string.IsNullOrWhiteSpace(CashAccountCode)
            && !string.IsNullOrWhiteSpace(OverShortAccountCode);

        IReadOnlyList<CashCount> counts = [];
        if (doCashUp && !CashCounts.TryParse(CountedCash, _currency.Value, out counts))
        {
            StatusMessage = "Type the count as an amount, or per currency: 120; ZAR 500; EUR 20.";
            return;
        }

        try
        {
            if (!_online)
            {
                throw new TimeoutException("The terminal is offline.");
            }

            if (!await CloseTillOnlineAsync(pos, doCashUp, counts).ConfigureAwait(true))
            {
                return;
            }
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            PosCashUpEnvelope envelope = new(_companyId, TillSessionId, TillSessionId == 0 ? _offlineSessionKey : null, doCashUp ? [.. counts.Select(c => new PosCashCountEnvelope(c.Currency, c.Amount))] : [],
                _cashierUserId, DateOnly.FromDateTime(DateTime.Today), CashAccountCode.Trim(), OverShortAccountCode.Trim());
            await LocalQueue.EnqueueAsync(new LocalOfflineEntry(Guid.NewGuid(), _companyId, PosCashUpEnvelope.Kind, JsonSerializer.Serialize(envelope), DateTimeOffset.UtcNow)).ConfigureAwait(true);
            PendingOfflineCount = await LocalQueue.CountAsync().ConfigureAwait(true);
            _offlineSessionKey = null;
            StatusMessage = doCashUp
                ? $"Offline: the count is declared here; the till cashes up and closes centrally when the connection returns ({PendingOfflineCount} queued)."
                : $"Offline: the till closes centrally with its Z-read when the connection returns ({PendingOfflineCount} queued).";
        }

        _ticket.Clear();
        AgeVerified = false;
        PrescriptionReference = string.Empty;
        RebuildCart();
        IsSessionOpen = false;
        TillSessionId = 0;
        CountedCash = string.Empty;
        UpdateSyncState();
        await SaveSnapshotAsync(null).ConfigureAwait(true);
    }

    // Closes the till centrally; false when it stayed open (the message says why).
    private async Task<bool> CloseTillOnlineAsync(IPosService pos, bool doCashUp, IReadOnlyList<CashCount> counts)
    {
        if (doCashUp)
        {
            Result declared = await pos.DeclareCountAsync(TillSessionId, counts, _cashierUserId).ConfigureAwait(true);
            if (declared.IsFailure && declared.Error.Code != "Pos.CountDeclared")
            {
                StatusMessage = declared.Error.Message;
                return false;
            }

            Result<CashUpResult> cashUp = await pos.CashUpAndCloseAsync(new CashUpRequest
            {
                TillSessionId = TillSessionId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                CashAccountCode = CashAccountCode.Trim(),
                OverShortAccountCode = OverShortAccountCode.Trim(),
            }).ConfigureAwait(true);
            if (cashUp.IsFailure)
            {
                StatusMessage = cashUp.Error.Message;
                return false;
            }

            CashUpResult r = cashUp.Value;
            string variance = r.Variance == 0m ? "balanced"
                : r.Variance > 0m ? $"over {r.Variance:N2}"
                : $"short {Math.Abs(r.Variance):N2}";
            string currencies = r.ByCurrency.Count > 1
                ? " " + string.Join("; ", r.ByCurrency.Select(c => $"{c.Currency} expected {c.Expected:N2} counted {c.Counted:N2}"))
                : string.Empty;
            StatusMessage = $"Z-read #{r.ZReadNumber}: expected {r.ExpectedCash:N2}, counted {r.CountedCash:N2} ({variance}).{currencies} Till closed.";
        }
        else
        {
            Result<TillRead> result = await pos.CloseTillAsync(TillSessionId).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return false;
            }

            StatusMessage = $"Z-read #{result.Value.ZReadNumber}: {result.Value.SaleCount} sale(s), gross {result.Value.TotalGross:N2}. Till closed.";
        }

        return true;
    }

    // Promotions are re-priced whenever the basket changes, until payment starts (a tender fixes the total).
    private void RebuildCart()
    {
        RenderCart();
        if (!_repricing && _ticket.Tenders.Count == 0 && _ticket.Lines.Count > 0)
        {
            _ = RepriceAsync();
        }
    }

    /// <summary>Takes a coupon code for the running promotions.</summary>
    [RelayCommand]
    private async Task ApplyCouponAsync()
    {
        string code = CouponEntry.Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            return;
        }

        if (!_coupons.Contains(code, StringComparer.Ordinal))
        {
            _coupons.Add(code);
        }

        CouponEntry = string.Empty;
        decimal before = _ticket.Lines.Sum(l => l.PromotionDiscount);
        await RepriceAsync().ConfigureAwait(true);
        StatusMessage = _ticket.Lines.Sum(l => l.PromotionDiscount) > before ? $"Coupon {code} applied." : $"Coupon {code} gives nothing on this basket (yet).";
    }

    private async Task RepriceAsync()
    {
        if (_ticket.Tenders.Count > 0)
        {
            return;
        }

        _repricing = true;
        if (!_online && _snapshot is not null)
        {
            // Offline: the promotions saved with the catalogue, priced by the same rules as online.
            try
            {
                IReadOnlyList<BasketItem> offlineBasket = _ticket.PromotionBasket();
                Dictionary<long, string?> categories = _snapshot.Tiles.GroupBy(t => t.ItemId).ToDictionary(g => g.Key, g => (string?)g.First().Category);
                BasketPricing offlinePricing = offlineBasket.Count == 0 ? new BasketPricing([], [])
                    : One4All.Application.Pos.PromotionPricing.Price(_snapshot.Promotions, new BasketRequest(_companyId, _snapshot.BranchId, DateTime.Now,
                        IsMember: !string.IsNullOrWhiteSpace(LoyaltyMember), _coupons, offlineBasket), categories);
                _ticket.ApplyPromotions(offlinePricing);
                RenderCart();
            }
            finally
            {
                _repricing = false;
            }

            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            if (_branchId is null)
            {
                long terminal = _terminalId;
                _branchId = await scope.ServiceProvider.GetRequiredService<One4All.Infrastructure.Persistence.One4AllDbContext>().PosTerminals.AsNoTracking()
                    .Where(t => t.Id == terminal).Select(t => (long?)t.BranchId).FirstOrDefaultAsync().ConfigureAwait(true);
            }

            IReadOnlyList<BasketItem> basket = _ticket.PromotionBasket();

            // A loyalty member on the sale gets members' promotions; an account customer's type picks promotions for that type.
            string? customerType = string.IsNullOrWhiteSpace(AccountCustomer) ? null : await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Customers.AsNoTracking()
                .Where(c => c.CompanyId == _companyId && c.Code.ToUpper() == AccountCustomer.Trim().ToUpperInvariant()).Select(c => c.CustomerType).FirstOrDefaultAsync().ConfigureAwait(true);
            BasketPricing pricing = basket.Count == 0 ? new BasketPricing([], [])
                : await scope.ServiceProvider.GetRequiredService<IPromotionService>().PriceBasketAsync(new BasketRequest(_companyId, _branchId, DateTime.Now,
                    IsMember: !string.IsNullOrWhiteSpace(LoyaltyMember), _coupons, basket, customerType)).ConfigureAwait(true);
            _ticket.ApplyPromotions(pricing);
            RenderCart();
        }
        finally
        {
            _repricing = false;
        }
    }

    private void RenderCart()
    {
        Cart.Clear();
        PosTicketLine? selected = SelectedCartLine?.Line;
        SelectedCartLine = null;
        foreach (PosTicketLine line in _ticket.Lines)
        {
            PosCartLineVm row = new(line) { IsSelected = ReferenceEquals(line, selected) };
            Cart.Add(row);
            if (row.IsSelected)
            {
                SelectedCartLine = row;
            }
        }

        RefreshTotals();
    }

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(CurrencyButtons));
        OnPropertyChanged(nameof(PayingInText));
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(TaxText));
        OnPropertyChanged(nameof(GrossText));
        OnPropertyChanged(nameof(TenderedText));
        OnPropertyChanged(nameof(BalanceText));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(BalanceInTenderCurrencyText));
    }
}

/// <summary>A currency button on the till.</summary>
/// <param name="Currency">The currency.</param>
/// <param name="Code">Its code (the button's label).</param>
/// <param name="IsSelected">Whether the next tender is in it.</param>
/// <param name="Tip">Its rate and the balance due in it.</param>
public sealed record CurrencyButton(TillCurrency Currency, string Code, bool IsSelected, string Tip);
