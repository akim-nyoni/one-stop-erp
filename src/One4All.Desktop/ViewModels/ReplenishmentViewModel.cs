using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A supplier choice for the order.</summary>
/// <param name="Id">The supplier id.</param>
/// <param name="Label">Code and name.</param>
public sealed record SupplierChoiceVm(long Id, string Label);

/// <summary>A replenishment suggestion row; the order quantity is editable and the row can be left out.</summary>
public sealed partial class ReplenishmentRowVm : ObservableObject
{
    /// <summary>Creates the row from a suggestion.</summary>
    /// <param name="suggestion">The suggestion.</param>
    public ReplenishmentRowVm(ReplenishmentSuggestion suggestion)
    {
        ItemId = suggestion.ItemId;
        ItemCode = suggestion.ItemCode;
        Description = suggestion.Description;
        OnHand = suggestion.OnHand;
        OnOrder = suggestion.OnOrder;
        ReorderLevel = suggestion.ReorderLevel;
        LastUnitPrice = suggestion.LastUnitPrice;
        LastSupplierId = suggestion.LastSupplierId;
        _orderQuantity = suggestion.SuggestedQuantity;
        _include = true;
    }

    /// <summary>The item.</summary>
    public long ItemId { get; }

    /// <summary>The item code.</summary>
    public string ItemCode { get; }

    /// <summary>The description.</summary>
    public string Description { get; }

    /// <summary>On hand.</summary>
    public decimal OnHand { get; }

    /// <summary>On open purchase orders.</summary>
    public decimal OnOrder { get; }

    /// <summary>The reorder level.</summary>
    public decimal ReorderLevel { get; }

    /// <summary>The last purchase price (0 when never bought).</summary>
    public decimal LastUnitPrice { get; }

    /// <summary>The last supplier, when known.</summary>
    public long? LastSupplierId { get; }

    /// <summary>Whether to include the row on the order.</summary>
    [ObservableProperty]
    private bool _include;

    /// <summary>The quantity to order (editable; starts at the suggestion).</summary>
    [ObservableProperty]
    private decimal _orderQuantity;
}

/// <summary>
/// Backs the replenishment screen: the items at or below their reorder level (net of stock already on
/// order), each with an editable order quantity, and a Raise Order action that places one purchase order
/// on the chosen supplier at last purchase prices. The netting and pricing rules live in the tested
/// <see cref="IReplenishmentService"/>; this class lists and forwards.
/// </summary>
public sealed partial class ReplenishmentViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public ReplenishmentViewModel(IServiceProvider services) => _services = services;

    /// <summary>The suggestions.</summary>
    public ObservableCollection<ReplenishmentRowVm> Rows { get; } = [];

    /// <summary>The suppliers an order can be placed on.</summary>
    public ObservableCollection<SupplierChoiceVm> Suppliers { get; } = [];

    /// <summary>The selected row (selecting one defaults the supplier to its last supplier).</summary>
    [ObservableProperty]
    private ReplenishmentRowVm? _selectedRow;

    /// <summary>The supplier to order from.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RaiseOrderCommand))]
    private SupplierChoiceVm? _selectedSupplier;

    /// <summary>The order date.</summary>
    [ObservableProperty]
    private DateTime _orderDate = DateTime.Today;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while loading or raising.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RaiseOrderCommand))]
    private bool _isBusy;

    private bool CanRaise => SelectedSupplier is not null && !IsBusy;

    partial void OnSelectedRowChanged(ReplenishmentRowVm? value)
    {
        if (value?.LastSupplierId is { } last && Suppliers.FirstOrDefault(s => s.Id == last) is { } match)
        {
            SelectedSupplier = match;
        }
    }

    /// <summary>Loads (or reloads) the suggestions and suppliers.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IServiceProvider sp = scope.ServiceProvider;
            One4AllDbContext db = sp.GetRequiredService<One4AllDbContext>();

            if (_companyId == 0)
            {
                _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
                if (_companyId == 0)
                {
                    StatusMessage = "No company is set up.";
                    return;
                }
            }

            long? keepSupplier = SelectedSupplier?.Id;
            Suppliers.Clear();
            foreach (var s in await db.Suppliers.Where(s => s.CompanyId == _companyId && s.IsActive && !s.OnHold).OrderBy(s => s.Code).Select(s => new { s.Id, s.Code, s.Name }).ToListAsync(ct).ConfigureAwait(true))
            {
                Suppliers.Add(new SupplierChoiceVm(s.Id, $"{s.Code} — {s.Name}"));
            }

            SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == keepSupplier);

            IReadOnlyList<ReplenishmentSuggestion> suggestions = await sp.GetRequiredService<IReplenishmentService>().SuggestAsync(_companyId, null, ct).ConfigureAwait(true);
            Rows.Clear();
            foreach (ReplenishmentSuggestion s in suggestions)
            {
                Rows.Add(new ReplenishmentRowVm(s));
            }

            SelectedRow = null;
            StatusMessage = Rows.Count == 0
                ? "Nothing is at or below its reorder level."
                : $"{Rows.Count} item(s) need replenishing — refreshed {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load suggestions: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Raises one purchase order on the selected supplier for every included row.</summary>
    [RelayCommand(CanExecute = nameof(CanRaise))]
    public async Task RaiseOrderAsync()
    {
        SupplierChoiceVm? supplier = SelectedSupplier;
        if (supplier is null || IsBusy)
        {
            return;
        }

        List<ReplenishmentLine> lines = Rows.Where(r => r.Include && r.OrderQuantity > 0m).Select(r => new ReplenishmentLine(r.ItemId, r.OrderQuantity)).ToList();
        if (lines.Count == 0)
        {
            StatusMessage = "Tick at least one item with a positive quantity.";
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IReplenishmentService replenish = scope.ServiceProvider.GetRequiredService<IReplenishmentService>();
            Result<long> raised = await replenish.RaiseOrderAsync(_companyId, supplier.Id, string.Empty, DateOnly.FromDateTime(OrderDate), lines).ConfigureAwait(true);
            if (raised.IsFailure)
            {
                StatusMessage = $"Order not raised: {raised.Error.Message}";
                return;
            }

            StatusMessage = $"Purchase order {raised.Value} raised on {supplier.Label} for {lines.Count} line(s), {Qty(lines.Sum(l => l.Quantity))} units.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Order not raised: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private static string Qty(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);
}
