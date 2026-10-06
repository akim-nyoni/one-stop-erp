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
    /// <summary>The menu name shown on the button.</summary>
    public required string Name { get; init; }

    /// <summary>The category (for filtering).</summary>
    public required string Category { get; init; }

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
    private readonly long _cashierUserId;
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
        CashierName = cashierName;
    }

    /// <summary>The product buttons for the selected category.</summary>
    public ObservableCollection<PosProductVm> Products { get; } = [];

    /// <summary>The categories offered (plus "All").</summary>
    public ObservableCollection<string> Categories { get; } = [];

    /// <summary>The current cart rows.</summary>
    public ObservableCollection<PosCartLineVm> Cart { get; } = [];

    /// <summary>The cashier's display name.</summary>
    public string CashierName { get; }

    /// <summary>The GL account cash (and change) posts to.</summary>
    [ObservableProperty]
    private string _cashAccountCode = string.Empty;

    /// <summary>The GL account card tenders post to.</summary>
    [ObservableProperty]
    private string _cardAccountCode = string.Empty;

    /// <summary>The GL account mobile-money tenders post to.</summary>
    [ObservableProperty]
    private string _mobileMoneyAccountCode = string.Empty;

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
        await LoadProductsAsync(db, ct).ConfigureAwait(true);
        await PrefillAccountsAsync(db, ct).ConfigureAwait(true);
        await RefreshParkedAsync().ConfigureAwait(true);

        PendingOfflineCount = await LocalQueue.CountAsync(ct).ConfigureAwait(true);

        StatusMessage = _allProducts.Count == 0
            ? "No menu items found — add priced menu items to sell at the terminal."
            : PendingOfflineCount > 0
                ? $"{PendingOfflineCount} offline sale(s) queued. Open the till to sync and trade."
                : "Enter an opening float and press Open Till to start trading.";
    }

    private ILocalOfflineQueue LocalQueue => _services.GetRequiredService<ILocalOfflineQueue>();

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

    private async Task LoadProductsAsync(One4AllDbContext db, CancellationToken ct)
    {
        List<MenuItem> items = await db.Set<MenuItem>()
            .Where(m => m.CompanyId == _companyId && m.IsAvailable)
            .OrderBy(m => m.Category).ThenBy(m => m.Name)
            .ToListAsync(ct).ConfigureAwait(true);

        _allProducts.Clear();
        foreach (MenuItem item in items)
        {
            string category = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category!;
            decimal rate = item.TaxCode is not null && _taxRates.TryGetValue(item.TaxCode, out decimal r) ? r : 0m;
            _allProducts.Add(new PosProductVm
            {
                Name = item.Name,
                Category = category,
                Price = item.Price,
                RevenueAccountCode = item.RevenueAccountCode,
                TaxCode = item.TaxCode,
                TaxRate = rate,
            });
        }

        Categories.Clear();
        Categories.Add("All");
        foreach (string category in _allProducts.Select(p => p.Category).Distinct().OrderBy(c => c))
        {
            Categories.Add(category);
        }

        SelectedCategory = "All";
        ApplyCategoryFilter();
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
    }

    private static string? FindByName(IEnumerable<LedgerAccount> accounts, params string[] keywords) =>
        accounts.FirstOrDefault(a => keywords.Any(k => a.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))?.AccountCode;

    partial void OnSelectedCategoryChanged(string value) => ApplyCategoryFilter();

    private void ApplyCategoryFilter()
    {
        Products.Clear();
        IEnumerable<PosProductVm> filtered = SelectedCategory == "All"
            ? _allProducts
            : _allProducts.Where(p => p.Category == SelectedCategory);
        foreach (PosProductVm product in filtered)
        {
            Products.Add(product);
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

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        Result<long> result = await pos.OpenTillAsync(_companyId, _terminalId, _cashierUserId, floatAmount).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = result.Error.Message;
            return;
        }

        TillSessionId = result.Value;
        IsSessionOpen = true;
        StatusMessage = $"Till open (session {TillSessionId}). Ready to trade.";

        // The database is reachable (the till just opened) — flush anything captured offline earlier.
        if (PendingOfflineCount > 0)
        {
            await FlushOfflineAsync().ConfigureAwait(true);
        }
    }

    /// <summary>The open till session id (0 when closed).</summary>
    [ObservableProperty]
    private long _tillSessionId;

    /// <summary>Adds a product to the ticket.</summary>
    /// <param name="product">The product tapped.</param>
    [RelayCommand]
    private void AddProduct(PosProductVm? product)
    {
        if (product is null || !IsSessionOpen)
        {
            return;
        }

        _ticket.AddLine(itemId: 0, product.Name, product.Price, product.TaxRate, product.RevenueAccountCode, product.TaxCode);
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
        Result<ItemLookup> found = await scope.ServiceProvider.GetRequiredService<IItemCatalogueService>().LookupAsync(_companyId, embedded?.ItemCode ?? code).ConfigureAwait(true);
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
            Result<ResolvedPrice> price = await scope.ServiceProvider.GetRequiredService<IPricingService>().PriceAsync(_companyId, null, item.Id, quantity).ConfigureAwait(true);
            if (price.IsFailure)
            {
                StatusMessage = $"{item.Description}: {price.Error.Message}";
                return;
            }

            unitPrice = price.Value.UnitPrice;
        }

        PosTicketLine line = _ticket.AddLine(item.Id, item.Description, unitPrice, rate, item.RevenueAccountCode, item.TaxCode, quantity);
        TenderEntry = string.Empty;
        RebuildCart();
        StatusMessage = $"{item.Description} × {quantity:0.###}";
        await ShowOnDisplayAsync(devices, line.Description, line.LineGross).ConfigureAwait(true);
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
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IReadOnlyList<ParkedSaleSummary> parked = await scope.ServiceProvider.GetRequiredService<IParkedSaleService>().ParkedAsync(_companyId).ConfigureAwait(true);
        ParkedSales.Clear();
        foreach (ParkedSaleSummary sale in parked)
        {
            ParkedSales.Add(sale);
        }
    }

    // A supervisor's user name and PIN, checked now: they must hold the permission and not be the cashier.
    private async Task<long?> SupervisorAsync(string permission, OperationFlags needed)
    {
        if (string.IsNullOrWhiteSpace(SupervisorName) || string.IsNullOrEmpty(SupervisorPin))
        {
            StatusMessage = "A supervisor must enter their name and PIN.";
            return null;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        AuthenticationResult supervisor = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
            .AuthenticateByPinAsync(SupervisorName.Trim(), SupervisorPin, $"POS-{_terminalId}").ConfigureAwait(true);
        SupervisorPin = string.Empty;
        SupervisorPinCleared?.Invoke();
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

        string saleNumber = $"POS{_terminalId}-{DateTime.Now:yyMMddHHmmss}-{++_saleSequence}";
        PosSaleContext context = new(
            _companyId, _terminalId, TillSessionId, _cashierUserId,
            saleNumber, DateOnly.FromDateTime(DateTime.Today), _warehouseId, _currency,
            TaxAccountCode: string.IsNullOrWhiteSpace(TaxAccountCode) ? null : TaxAccountCode.Trim(),
            ChangeAccountCode: CashAccountCode.Trim(),
            TenderAccounts: tenderAccounts,
            AgeVerified: AgeVerified,
            PrescriptionReference: string.IsNullOrWhiteSpace(PrescriptionReference) ? null : PrescriptionReference.Trim(),
            DiscountAccountCode: NullIfBlank(DiscountAccountCode));

        Result<CompleteSaleRequest> build = _ticket.BuildSale(context);
        if (build.IsFailure)
        {
            StatusMessage = build.Error.Message;
            return;
        }

        CompleteSaleRequest request = build.Value;
        decimal change = _ticket.ChangeDue;

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
            AgeVerified = false;
            PrescriptionReference = string.Empty;
            RebuildCart();
            StatusMessage = $"Sale {saleNumber} posted (journal {result.Value.JournalNumber}). Change {change:N2}.{loyalty}{(printed is null ? string.Empty : $" Receipt not printed: {printed}")}";
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
            request.Lines.Select(l => new PosSaleLineEnvelope(l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.LineNet, l.TaxAmount, l.RevenueAccountCode)).ToList(),
            request.Tenders.Select(t => new PosTenderEnvelope(t.TenderType, t.Currency.Value, t.Amount, t.AccountCode, t.IsChange)).ToList(),
            request.AgeVerified, request.PrescriptionReference);

        LocalOfflineEntry entry = new(Guid.NewGuid(), request.CompanyId, PosSaleEnvelope.Kind, JsonSerializer.Serialize(envelope), DateTimeOffset.UtcNow);
        await LocalQueue.EnqueueAsync(entry).ConfigureAwait(true);
        PendingOfflineCount = await LocalQueue.CountAsync().ConfigureAwait(true);
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
            StatusMessage = $"Synced {report.Synced} offline sale(s)"
                + (report.DeadLettered > 0 ? $", {report.DeadLettered} could not be posted" : string.Empty) + ".";
        }
        catch (Exception ex) when (OfflineDetection.IsConnectivityError(ex))
        {
            StatusMessage = $"Still offline — {PendingOfflineCount} sale(s) remain queued.";
        }
        finally
        {
            PendingOfflineCount = await LocalQueue.CountAsync().ConfigureAwait(true);
        }
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

        bool doCashUp = decimal.TryParse(CountedCash, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal counted)
            && !string.IsNullOrWhiteSpace(CashAccountCode)
            && !string.IsNullOrWhiteSpace(OverShortAccountCode);

        if (doCashUp)
        {
            Result<CashUpResult> cashUp = await pos.CashUpAndCloseAsync(new CashUpRequest
            {
                TillSessionId = TillSessionId,
                CountedCash = counted,
                Date = DateOnly.FromDateTime(DateTime.Today),
                CashAccountCode = CashAccountCode.Trim(),
                OverShortAccountCode = OverShortAccountCode.Trim(),
            }).ConfigureAwait(true);
            if (cashUp.IsFailure)
            {
                StatusMessage = cashUp.Error.Message;
                return;
            }

            CashUpResult r = cashUp.Value;
            string variance = r.Variance == 0m ? "balanced"
                : r.Variance > 0m ? $"over {r.Variance:N2}"
                : $"short {Math.Abs(r.Variance):N2}";
            StatusMessage = $"Z-read #{r.ZReadNumber}: expected {r.ExpectedCash:N2}, counted {r.CountedCash:N2} ({variance}). Till closed.";
        }
        else
        {
            Result<TillRead> result = await pos.CloseTillAsync(TillSessionId).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            StatusMessage = $"Z-read #{result.Value.ZReadNumber}: {result.Value.SaleCount} sale(s), gross {result.Value.TotalGross:N2}. Till closed.";
        }

        _ticket.Clear();
        AgeVerified = false;
        PrescriptionReference = string.Empty;
        RebuildCart();
        IsSessionOpen = false;
        TillSessionId = 0;
        CountedCash = string.Empty;
    }

    private void RebuildCart()
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
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(TaxText));
        OnPropertyChanged(nameof(GrossText));
        OnPropertyChanged(nameof(TenderedText));
        OnPropertyChanged(nameof(BalanceText));
        OnPropertyChanged(nameof(ChangeText));
    }
}
