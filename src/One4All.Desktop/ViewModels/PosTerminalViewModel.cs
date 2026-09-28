using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
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

    /// <summary>The output-tax account.</summary>
    [ObservableProperty]
    private string _taxAccountCode = string.Empty;

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

        StatusMessage = _allProducts.Count == 0
            ? "No menu items found — add priced menu items to sell at the terminal."
            : "Enter an opening float and press Open Till to start trading.";
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

    /// <summary>Takes a card tender for the entered amount (or the exact balance when blank).</summary>
    [RelayCommand]
    private void TenderCard() => AddTender(TenderType.Card, allowOverTender: false);

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

        string saleNumber = $"POS{_terminalId}-{DateTime.Now:yyMMddHHmmss}-{++_saleSequence}";
        PosSaleContext context = new(
            _companyId, _terminalId, TillSessionId, _cashierUserId,
            saleNumber, DateOnly.FromDateTime(DateTime.Today), _warehouseId, _currency,
            TaxAccountCode: string.IsNullOrWhiteSpace(TaxAccountCode) ? null : TaxAccountCode.Trim(),
            ChangeAccountCode: CashAccountCode.Trim(),
            TenderAccounts: tenderAccounts);

        Result<CompleteSaleRequest> build = _ticket.BuildSale(context);
        if (build.IsFailure)
        {
            StatusMessage = build.Error.Message;
            return;
        }

        decimal change = _ticket.ChangeDue;

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        Result<CompleteSaleResult> result = await pos.CompleteSaleAsync(build.Value).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = $"Sale not posted: {result.Error.Message}";
            return;
        }

        _ticket.Clear();
        RebuildCart();
        StatusMessage = $"Sale {saleNumber} posted (journal {result.Value.JournalNumber}). Change {change:N2}.";
    }

    /// <summary>Abandons the current ticket.</summary>
    [RelayCommand]
    private void NewSale()
    {
        _ticket.Clear();
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
        Result<TillRead> result = await pos.CloseTillAsync(TillSessionId).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = result.Error.Message;
            return;
        }

        _ticket.Clear();
        RebuildCart();
        IsSessionOpen = false;
        TillSessionId = 0;
        StatusMessage = $"Z-read #{result.Value.ZReadNumber}: {result.Value.SaleCount} sale(s), gross {result.Value.TotalGross:N2}. Till closed.";
    }

    private void RebuildCart()
    {
        Cart.Clear();
        foreach (PosTicketLine line in _ticket.Lines)
        {
            Cart.Add(new PosCartLineVm(line));
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
