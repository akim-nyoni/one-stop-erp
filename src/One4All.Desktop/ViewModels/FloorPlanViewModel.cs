using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A table drawn on the waiter's floor screen.</summary>
/// <param name="Number">The table number.</param>
/// <param name="Left">Its left edge in pixels.</param>
/// <param name="Top">Its top edge in pixels.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="CornerRadius">Its corner radius (round tables are circles).</param>
/// <param name="Fill">Its colour: free, occupied, or warned.</param>
/// <param name="Line1">The number and seats.</param>
/// <param name="Line2">The tab total and minutes open, when occupied.</param>
/// <param name="Detail">The full description for the side panel.</param>
public sealed record FloorTableVm(string Number, double Left, double Top, double Width, double Height, double CornerRadius, string Fill, string Line1, string Line2, string Detail);

/// <summary>A menu item the waiter taps to add to the table's order.</summary>
/// <param name="Id">The menu item.</param>
/// <param name="Name">Its name.</param>
/// <param name="Price">Its price.</param>
public sealed record FloorMenuTileVm(long Id, string Name, decimal Price)
{
    /// <summary>Gets the price as shown on the tile.</summary>
    public string PriceText => Price.ToString("N2", CultureInfo.CurrentCulture);
}

/// <summary>A line of the table's open order.</summary>
/// <param name="Text">The quantity and description.</param>
/// <param name="Amount">The line value.</param>
/// <param name="State">Sent to the kitchen, voided or comped.</param>
public sealed record FloorOrderLineVm(string Text, string Amount, string State);

/// <summary>
/// Backs the waiter's floor screen (brief §4.1 restaurant "graphical floor / table plan"): every table at its place on the plan, coloured by
/// whether it is free, occupied or past its tab warning, with the open tab's total and age; refreshed on a timer so the floor stays current. Tapping
/// a table opens its order beside the plan: a free table is seated (an order opened for the party), menu items are tapped onto the order, the order
/// is sent to the kitchen, and the table is billed - closing the order and freeing the table.
/// </summary>
public sealed partial class FloorPlanViewModel : ObservableObject
{
    /// <summary>Pixels per plan grid cell.</summary>
    public const double Cell = 24;

    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application services.</param>
    public FloorPlanViewModel(IServiceProvider services) => _services = services;

    /// <summary>Gets the tables to draw.</summary>
    public ObservableCollection<FloorTableVm> Tables { get; } = [];

    /// <summary>Gets or sets the table picked on the plan.</summary>
    [ObservableProperty]
    private FloorTableVm? _selected;

    /// <summary>Gets or sets the summary line.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Gets or sets the canvas width needed for the plan.</summary>
    [ObservableProperty]
    private double _canvasWidth = 800;

    /// <summary>Gets or sets the canvas height needed for the plan.</summary>
    [ObservableProperty]
    private double _canvasHeight = 600;

    /// <summary>Gets the menu to tap onto the table's order.</summary>
    public ObservableCollection<FloorMenuTileVm> Menu { get; } = [];

    /// <summary>Gets the lines of the selected table's open order.</summary>
    public ObservableCollection<FloorOrderLineVm> OrderLines { get; } = [];

    /// <summary>Gets or sets the selected table's open order number (null when the table is free).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrder), nameof(CanSeat))]
    private string? _orderNumber;

    /// <summary>Gets or sets the order's total.</summary>
    [ObservableProperty]
    private string _orderTotal = string.Empty;

    /// <summary>Gets or sets the party size for seating a free table.</summary>
    [ObservableProperty]
    private int _partySize = 2;

    /// <summary>Gets or sets the account the bill is paid into (cash or card clearing).</summary>
    [ObservableProperty]
    private string _payAccount = string.Empty;

    /// <summary>Gets or sets the output tax account for the bill.</summary>
    [ObservableProperty]
    private string _taxAccount = string.Empty;

    /// <summary>Gets or sets the warehouse the recipe ingredients are issued from.</summary>
    [ObservableProperty]
    private string _warehouse = string.Empty;

    /// <summary>Gets or sets the result of the last action.</summary>
    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>Gets whether the selected table has an open order.</summary>
    public bool HasOrder => OrderNumber is not null;

    /// <summary>Gets whether the selected table can be seated (it is free).</summary>
    public bool CanSeat => Selected is not null && OrderNumber is null;

    /// <summary>Reloads the plan.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        if (_companyId == 0)
        {
            _companyId = scope.ServiceProvider.GetService<Application.Abstractions.Security.ICurrentUser>()?.CompanyId
                ?? await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        }

        IReadOnlyList<FloorTable> plan = await scope.ServiceProvider.GetRequiredService<IFloorPlanService>().PlanAsync(_companyId, DateTimeOffset.UtcNow, ct).ConfigureAwait(true);
        string? keep = Selected?.Number;
        Tables.Clear();
        int unplaced = 0, maxRow = plan.Where(t => t.Y is not null).Select(t => t.Y!.Value).DefaultIfEmpty(0).Max();
        foreach (FloorTable table in plan.OrderBy(t => t.TableNumber, StringComparer.OrdinalIgnoreCase))
        {
            // Tables not yet placed line up along the bottom so nothing is hidden.
            (int x, int y) = table.X is int px && table.Y is int py ? (px, py) : (unplaced++ * 4, maxRow + 4);
            (double w, double h) = table.Shape switch
            {
                TableShape.Booth => (Cell * 4, Cell * 2),
                TableShape.BarSeat => (Cell * 1.5, Cell * 1.5),
                _ => (Cell * 3, Cell * 3),
            };
            string fill = table.Warning is not null ? "#C0392B" : table.Status == TableStatus.Occupied ? "#D68910" : "#239B56";
            string line2 = table.Status == TableStatus.Occupied ? $"{table.TabTotal.ToString("N2", CultureInfo.InvariantCulture)} · {table.MinutesOpen} min" : "free";
            string detail = $"Table {table.TableNumber} ({table.Seats} seats, {table.Shape})" + Environment.NewLine
                + (table.Status == TableStatus.Occupied ? $"Order {table.OrderNumber}: {table.TabTotal.ToString("N2", CultureInfo.InvariantCulture)}, open {table.MinutesOpen} min" : "Free")
                + (table.Warning is null ? string.Empty : Environment.NewLine + "⚠ " + table.Warning);
            Tables.Add(new FloorTableVm(table.TableNumber, x * Cell, y * Cell, w, h, table.Shape == TableShape.Round ? w / 2 : 6, fill, $"{table.TableNumber} ({table.Seats})", line2, detail));
        }

        CanvasWidth = Math.Max(800, Tables.Select(t => t.Left + t.Width).DefaultIfEmpty(0).Max() + Cell);
        CanvasHeight = Math.Max(600, Tables.Select(t => t.Top + t.Height).DefaultIfEmpty(0).Max() + Cell);
        int occupied = plan.Count(t => t.Status == TableStatus.Occupied), warned = plan.Count(t => t.Warning is not null);
        Summary = $"{plan.Count} table(s): {occupied} occupied, {plan.Count - occupied} free, {warned} warned · {plan.Where(t => t.Status == TableStatus.Occupied).Sum(t => t.TabTotal).ToString("N2", CultureInfo.InvariantCulture)} open on tabs · {DateTime.Now:HH:mm}";
        Selected = Tables.FirstOrDefault(t => t.Number == keep);
    }

    /// <summary>Picks a table and opens its order beside the plan.</summary>
    /// <param name="table">The table clicked.</param>
    /// <returns>A task.</returns>
    [RelayCommand]
    private async Task PickAsync(FloorTableVm? table)
    {
        Selected = table;
        Message = string.Empty;
        await LoadOrderAsync().ConfigureAwait(true);
    }

    partial void OnSelectedChanged(FloorTableVm? value) => OnPropertyChanged(nameof(CanSeat));

    /// <summary>Seats the party at the selected free table: an order is opened for it.</summary>
    /// <returns>A task.</returns>
    [RelayCommand]
    private async Task SeatAsync()
    {
        if (Selected is not FloorTableVm table)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long tableId = await db.Set<RestaurantTable>().Where(t => t.CompanyId == _companyId && t.TableNumber == table.Number).Select(t => t.Id).FirstOrDefaultAsync().ConfigureAwait(true);
        string number = $"T{table.Number}-{DateTime.Now:yyMMddHHmmss}";
        Core.Common.Result<long> opened = await scope.ServiceProvider.GetRequiredService<IRestaurantService>()
            .OpenOrderAsync(_companyId, tableId, number, scope.ServiceProvider.GetRequiredService<Application.Abstractions.Security.ICurrentUser>().UserId ?? 0, DateOnly.FromDateTime(DateTime.Today), Math.Max(1, PartySize)).ConfigureAwait(true);
        Message = opened.IsSuccess ? $"Table {table.Number} seated: order {number}." : opened.Error.Message;
        await RefreshAsync().ConfigureAwait(true);
        await LoadOrderAsync().ConfigureAwait(true);
    }

    /// <summary>Adds one of the tapped menu item to the table's order.</summary>
    /// <param name="item">The menu item.</param>
    /// <returns>A task.</returns>
    [RelayCommand]
    private async Task AddItemAsync(FloorMenuTileVm? item)
    {
        if (item is null || await OrderIdAsync().ConfigureAwait(true) is not long order)
        {
            Message = "Seat the table first.";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Core.Common.Result<decimal> added = await scope.ServiceProvider.GetRequiredService<IRestaurantService>().AddItemAsync(_companyId, order, item.Id, 1m).ConfigureAwait(true);
        Message = added.IsSuccess ? $"{item.Name} added." : added.Error.Message;
        await LoadOrderAsync().ConfigureAwait(true);
    }

    /// <summary>Sends what has not yet gone to the kitchen.</summary>
    /// <returns>A task.</returns>
    [RelayCommand]
    private async Task FireAsync()
    {
        if (await OrderIdAsync().ConfigureAwait(true) is not long order)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        string ticket = $"K-{OrderNumber}-{DateTime.Now:HHmmss}";
        Core.Common.Result<long> fired = await scope.ServiceProvider.GetRequiredService<IRestaurantService>().FireOrderAsync(_companyId, order, ticket, DateOnly.FromDateTime(DateTime.Today))
            .ConfigureAwait(true);
        if (fired.IsSuccess && scope.ServiceProvider.GetService<IKitchenPrintService>() is IKitchenPrintService printer)
        {
            _ = await printer.PrintAsync(_companyId, ticket).ConfigureAwait(true);
        }

        Message = fired.IsSuccess ? $"Sent to the kitchen ({ticket})." : fired.Error.Message;
        await LoadOrderAsync().ConfigureAwait(true);
    }

    /// <summary>Bills the table in full into the pay account, issuing the recipe ingredients; the order closes and the table is free.</summary>
    /// <returns>A task.</returns>
    [RelayCommand]
    private async Task BillAsync()
    {
        if (await OrderIdAsync().ConfigureAwait(true) is not long order)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(PayAccount) || string.IsNullOrWhiteSpace(Warehouse))
        {
            Message = "Give the pay account and the warehouse first.";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        string code = Warehouse.Trim().ToUpperInvariant();
        long warehouse = await db.Warehouses.Where(w => w.CompanyId == _companyId && w.Code.ToUpper() == code).Select(w => w.Id).FirstOrDefaultAsync().ConfigureAwait(true);

        // The cash goes into the signed-in user's open till session, so it is counted at that till's cash-up (billing refuses without one).
        long? me = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Security.ICurrentUser>().UserId;
        long? till = me is null ? null : await db.TillSessions.AsNoTracking()
            .Where(s => s.CompanyId == _companyId && s.CashierUserId == me && s.Status == Core.Pos.TillSessionStatus.Open).OrderByDescending(s => s.Id).Select(s => (long?)s.Id)
            .FirstOrDefaultAsync().ConfigureAwait(true);
        Core.Common.Result<BillResult> bill = await scope.ServiceProvider.GetRequiredService<IRestaurantService>().BillOrderAsync(_companyId, order, warehouse, DateOnly.FromDateTime(DateTime.Today),
            PayAccount.Trim(), string.IsNullOrWhiteSpace(TaxAccount) ? null : TaxAccount.Trim(), tillSessionId: till).ConfigureAwait(true);
        Message = bill.IsSuccess ? $"Billed {bill.Value.Gross.ToString("N2", CultureInfo.CurrentCulture)}{(bill.Value.FullyBilled ? "; the table is free." : ".")}" : bill.Error.Message;
        await RefreshAsync().ConfigureAwait(true);
        await LoadOrderAsync().ConfigureAwait(true);
    }

    private async Task<long?> OrderIdAsync()
    {
        if (OrderNumber is not string number)
        {
            return null;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Set<TableOrder>().Where(o => o.CompanyId == _companyId && o.OrderNumber == number).Select(o => (long?)o.Id)
            .FirstOrDefaultAsync().ConfigureAwait(true);
    }

    // The selected table's open order, its lines, and (once) the menu and the default accounts.
    private async Task LoadOrderAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        if (Menu.Count == 0)
        {
            foreach (var item in await db.Set<MenuItem>().AsNoTracking().Where(m => m.CompanyId == _companyId && m.IsAvailable).OrderBy(m => m.Category).ThenBy(m => m.Name)
                .Select(m => new { m.Id, m.Name, m.Price }).ToListAsync().ConfigureAwait(true))
            {
                Menu.Add(new FloorMenuTileVm(item.Id, item.Name, item.Price));
            }

            var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == _companyId && a.IsActive).Select(a => new { a.AccountCode, a.Name }).ToListAsync().ConfigureAwait(true);
            string? Find(params string[] words) => accounts.FirstOrDefault(a => words.Any(w => a.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))?.AccountCode;
            PayAccount = PayAccount.Length > 0 ? PayAccount : Find("cash", "till") ?? string.Empty;
            TaxAccount = TaxAccount.Length > 0 ? TaxAccount : Find("vat", "output tax") ?? string.Empty;
            Warehouse = Warehouse.Length > 0 ? Warehouse
                : await db.Warehouses.AsNoTracking().Where(w => w.CompanyId == _companyId).OrderBy(w => w.Code).Select(w => w.Code).FirstOrDefaultAsync().ConfigureAwait(true) ?? string.Empty;
        }

        OrderLines.Clear();
        OrderNumber = null;
        OrderTotal = string.Empty;
        if (Selected is not FloorTableVm table)
        {
            return;
        }

        long tableId = await db.Set<RestaurantTable>().Where(t => t.CompanyId == _companyId && t.TableNumber == table.Number).Select(t => t.Id).FirstOrDefaultAsync().ConfigureAwait(true);
        TableOrder? open = await db.Set<TableOrder>().AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == _companyId && o.TableId == tableId && o.Status == TableOrderStatus.Open).OrderByDescending(o => o.Id).FirstOrDefaultAsync().ConfigureAwait(true);
        if (open is null)
        {
            return;
        }

        OrderNumber = open.OrderNumber;
        foreach (TableOrderLine line in open.Lines.OrderBy(l => l.Id))
        {
            string state = line.IsVoided ? "void" : line.IsComped ? "comp" : line.Fired ? "sent" : "new";
            OrderLines.Add(new FloorOrderLineVm($"{line.Quantity:0.##} x {line.Description}", line.LineNet.ToString("N2", CultureInfo.CurrentCulture), state));
        }

        OrderTotal = open.Lines.Where(l => !l.IsVoided && !l.IsComped).Sum(l => l.LineNet).ToString("N2", CultureInfo.CurrentCulture);
    }
}
