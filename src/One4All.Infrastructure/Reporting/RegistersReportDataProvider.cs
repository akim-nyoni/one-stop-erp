using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Core.Procurement;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Supplies live rows for the register-style reports of the lighter verticals — the housing stand register,
/// the student register and open purchase orders — from their entities.
/// </summary>
public sealed class RegistersReportDataProvider : IReportDataProvider
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "HousingStands", "StudentRegister", "OpenPurchaseOrders", "Menu", "TableStatus", "KitchenTickets",
        "TipAllocations", "MenuEngineering", "Reservations",
    };

    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public RegistersReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => Commands.Contains(command);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        return command.ToUpperInvariant() switch
        {
            "HOUSINGSTANDS" => await HousingStandsAsync(companyId, ct).ConfigureAwait(false),
            "STUDENTREGISTER" => await StudentRegisterAsync(companyId, ct).ConfigureAwait(false),
            "OPENPURCHASEORDERS" => await OpenPurchaseOrdersAsync(companyId, ct).ConfigureAwait(false),
            "MENU" => await MenuAsync(companyId, ct).ConfigureAwait(false),
            "TABLESTATUS" => await TableStatusAsync(companyId, ct).ConfigureAwait(false),
            "KITCHENTICKETS" => await KitchenTicketsAsync(companyId, ct).ConfigureAwait(false),
            "TIPALLOCATIONS" => await TipAllocationsAsync(companyId, ct).ConfigureAwait(false),
            "MENUENGINEERING" => await MenuEngineeringAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RESERVATIONS" => await ReservationsAsync(companyId, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> KitchenTicketsAsync(long companyId, CancellationToken ct)
    {
        List<Core.Restaurant.KitchenTicket> tickets = await _db.Set<Core.Restaurant.KitchenTicket>().Include(t => t.Lines)
            .Where(t => t.CompanyId == companyId && t.Status != Core.Restaurant.KitchenTicketStatus.Served)
            .OrderBy(t => t.SentOn).ThenBy(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return tickets.Select(t => Row(new()
        {
            ["Ticket"] = t.TicketNumber,
            ["Status"] = t.Status.ToString(),
            ["Items"] = t.Lines.Count,
            ["Sent"] = t.SentOn.ToDateTime(TimeOnly.MinValue),
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReservationsAsync(long companyId, CancellationToken ct)
    {
        List<Core.Restaurant.TableReservation> reservations = await _db.Set<Core.Restaurant.TableReservation>()
            .Where(r => r.CompanyId == companyId && r.Status == Core.Restaurant.ReservationStatus.Booked)
            .OrderBy(r => r.ReservedFor)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> tables = await _db.Set<Core.Restaurant.RestaurantTable>()
            .Where(t => t.CompanyId == companyId)
            .ToDictionaryAsync(t => t.Id, t => t.TableNumber, ct).ConfigureAwait(false);
        return reservations.Select(r => Row(new()
        {
            ["When"] = r.ReservedFor,
            ["Table"] = tables.GetValueOrDefault(r.TableId, $"#{r.TableId}"),
            ["Guest"] = r.GuestName,
            ["Party"] = r.PartySize,
            ["Phone"] = r.GuestPhone ?? string.Empty,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TipAllocationsAsync(long companyId, CancellationToken ct)
    {
        List<Core.Restaurant.TipAllocation> allocations = await _db.Set<Core.Restaurant.TipAllocation>()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Date).ThenBy(a => a.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> staff = await _db.Set<Core.Security.User>()
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct).ConfigureAwait(false);
        return allocations.Select(a => Row(new()
        {
            ["Staff"] = staff.GetValueOrDefault(a.StaffUserId, $"#{a.StaffUserId}"),
            ["Date"] = a.Date.ToDateTime(TimeOnly.MinValue),
            ["Status"] = a.Status.ToString(),
            ["Amount"] = a.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MenuEngineeringAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly fromDate = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly toDate = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;

        List<Core.Restaurant.MenuItem> menu = await _db.Set<Core.Restaurant.MenuItem>().Include(m => m.Recipe)
            .Where(m => m.CompanyId == companyId)
            .ToListAsync(ct).ConfigureAwait(false);

        // Standard recipe cost per menu item from the ingredients' current weighted-average cost.
        Dictionary<long, decimal> itemCost = await _db.Set<Core.Inventory.StockBalance>()
            .Where(b => b.CompanyId == companyId)
            .GroupBy(b => b.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QuantityOnHand), Value = g.Sum(x => x.TotalValue) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Qty > 0m ? decimal.Round(x.Value / x.Qty, 4, MidpointRounding.ToEven) : 0m, ct)
            .ConfigureAwait(false);

        Dictionary<long, decimal> recipeCost = menu.ToDictionary(
            m => m.Id,
            m => m.Recipe.Sum(r => r.Quantity * itemCost.GetValueOrDefault(r.IngredientItemId, 0m)));

        // Billed sales quantity per menu item over the period (order reference date = opened-on).
        List<Core.Restaurant.TableOrderLine> lines = await (
            from l in _db.Set<Core.Restaurant.TableOrderLine>()
            join o in _db.Set<Core.Restaurant.TableOrder>() on l.TableOrderId equals o.Id
            where o.CompanyId == companyId && o.OpenedOn >= fromDate && o.OpenedOn <= toDate && l.BilledQuantity > 0m
            select l).ToListAsync(ct).ConfigureAwait(false);

        var sales = lines.GroupBy(l => l.MenuItemId).ToDictionary(
            g => g.Key,
            g => (Qty: g.Sum(l => l.BilledQuantity), Revenue: g.Sum(l => decimal.Round(l.BilledQuantity * l.UnitPrice, 4, MidpointRounding.ToEven))));

        var rows = menu.Select(m =>
        {
            (decimal Qty, decimal Revenue) s = sales.GetValueOrDefault(m.Id, (0m, 0m));
            decimal cost = decimal.Round(s.Qty * recipeCost.GetValueOrDefault(m.Id, 0m), 4, MidpointRounding.ToEven);
            decimal margin = s.Revenue - cost;
            decimal unitMargin = s.Qty > 0m ? margin / s.Qty : 0m;
            return (Item: m, s.Qty, s.Revenue, Cost: cost, Margin: margin, UnitMargin: unitMargin);
        }).ToList();

        // Menu-engineering classification: popularity (qty) × profitability (unit margin) against the averages.
        List<(Core.Restaurant.MenuItem Item, decimal Qty, decimal Revenue, decimal Cost, decimal Margin, decimal UnitMargin)> sold =
            rows.Where(r => r.Qty > 0m).ToList();
        decimal avgQty = sold.Count > 0 ? sold.Average(r => r.Qty) : 0m;
        decimal avgUnitMargin = sold.Count > 0 ? sold.Average(r => r.UnitMargin) : 0m;

        return rows.OrderByDescending(r => r.Margin).ThenBy(r => r.Item.Code).Select(r => Row(new()
        {
            ["Item"] = r.Item.Name,
            ["QtySold"] = r.Qty,
            ["Revenue"] = r.Revenue,
            ["Cost"] = r.Cost,
            ["Margin"] = r.Margin,
            ["Class"] = Classify(r.Qty, r.UnitMargin, avgQty, avgUnitMargin),
        })).ToList();
    }

    private static string Classify(decimal qty, decimal unitMargin, decimal avgQty, decimal avgUnitMargin)
    {
        if (qty <= 0m)
        {
            return "Unsold";
        }

        bool popular = qty >= avgQty;
        bool profitable = unitMargin >= avgUnitMargin;
        return (popular, profitable) switch
        {
            (true, true) => "Star",
            (true, false) => "Plowhorse",
            (false, true) => "Puzzle",
            _ => "Dog",
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MenuAsync(long companyId, CancellationToken ct)
    {
        List<Core.Restaurant.MenuItem> items = await _db.Set<Core.Restaurant.MenuItem>().Include(m => m.Recipe)
            .Where(m => m.CompanyId == companyId)
            .OrderBy(m => m.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        return items.Select(m => Row(new()
        {
            ["Code"] = m.Code,
            ["Name"] = m.Name,
            ["Category"] = m.Category ?? string.Empty,
            ["Ingredients"] = m.Recipe.Count,
            ["Available"] = m.IsAvailable ? "Yes" : "No",
            ["Price"] = m.Price,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TableStatusAsync(long companyId, CancellationToken ct)
    {
        List<Core.Restaurant.RestaurantTable> tables = await _db.Set<Core.Restaurant.RestaurantTable>()
            .Where(t => t.CompanyId == companyId)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return tables.Select(t => Row(new()
        {
            ["TableNumber"] = t.TableNumber,
            ["Seats"] = t.Seats,
            ["Status"] = t.Status.ToString(),
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HousingStandsAsync(long companyId, CancellationToken ct)
    {
        List<HousingStand> stands = await _db.Set<HousingStand>()
            .Where(s => s.CompanyId == companyId)
            .OrderBy(s => s.StandNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> holders = await _db.Set<Customer>()
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);

        return stands.Select(s => Row(new()
        {
            ["StandNumber"] = s.StandNumber,
            ["Status"] = s.Status.ToString(),
            ["Holder"] = s.CurrentHolderCustomerId is { } id ? holders.GetValueOrDefault(id, $"#{id}") : string.Empty,
            ["AllocatedCost"] = s.AllocatedCost,
            ["SellingPrice"] = s.SellingPrice,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StudentRegisterAsync(long companyId, CancellationToken ct)
    {
        List<Student> students = await _db.Set<Student>()
            .Where(s => s.CompanyId == companyId && s.IsEnrolled)
            .OrderBy(s => s.AdmissionNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return students.Select(s => Row(new()
        {
            ["AdmissionNumber"] = s.AdmissionNumber,
            ["FullName"] = s.FullName,
            ["Grade"] = s.Grade,
            ["Boarder"] = s.IsBoarder ? "Yes" : "No",
            ["BursaryValue"] = s.BursaryValue,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OpenPurchaseOrdersAsync(long companyId, CancellationToken ct)
    {
        List<PurchaseOrder> orders = await _db.Set<PurchaseOrder>().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.Status != PurchaseOrderStatus.Closed)
            .OrderBy(o => o.OrderNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> suppliers = await _db.Set<Supplier>()
            .Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct).ConfigureAwait(false);

        return orders.Select(o => Row(new()
        {
            ["OrderNumber"] = o.OrderNumber,
            ["OrderDate"] = o.OrderDate.ToDateTime(TimeOnly.MinValue),
            ["Supplier"] = suppliers.GetValueOrDefault(o.SupplierId, $"#{o.SupplierId}"),
            ["Status"] = o.Status.ToString(),
            ["TotalNet"] = o.TotalNet,
        })).ToList();
    }

    private static IReadOnlyDictionary<string, object?> Row(Dictionary<string, object?> values) => values;
}
