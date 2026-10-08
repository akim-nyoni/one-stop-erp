using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.JobCosting;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the WB-numbered weighbridge reports (brief §14 L): every data command whose name starts with <c>Wbr</c>.</summary>
public sealed class WeighbridgeReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public WeighbridgeReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Wbr", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? DateOnly.FromDateTime(DateTime.Today);
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "WBRTICKETS" => await TicketsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "WBRDAILY" => await DailyAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "WBRVEHICLES" => await VehiclesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "WBRDELIVERIES" => await DeliveriesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "WBRMANUAL" => await ManualAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "WBRSTOCKPILES" => await StockpilesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "WBRBATCHES" => await BatchesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "WBRHAULAGE" => await HaulageAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private Task<List<WeighbridgeTicket>> TicketListAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => _db.WeighbridgeTickets.AsNoTracking().Where(t => t.CompanyId == companyId && t.Date >= fromDate && t.Date <= toDate).OrderBy(t => t.Date).ThenBy(t => t.TicketNumber).ToListAsync(ct);

    private async Task<Dictionary<long, string>> CustomersAsync(long companyId, CancellationToken ct)
        => await _db.Customers.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);

    private async Task<Dictionary<long, string>> SuppliersAsync(long companyId, CancellationToken ct)
        => await _db.Suppliers.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.Name, ct).ConfigureAwait(false);

    private Task<Dictionary<long, string>> UsersAsync(CancellationToken ct) => _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TicketsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<WeighbridgeTicket> tickets = await TicketListAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        Dictionary<long, string> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false), suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        return [.. tickets.Select(t => Row(("Ticket", t.TicketNumber), ("Date", Date(t.Date)), ("Vehicle", t.VehicleRegistration), ("Driver", t.DriverName ?? string.Empty),
            ("Party", t.CustomerId is long c ? customers.GetValueOrDefault(c, "?") : t.SupplierId is long s ? suppliers.GetValueOrDefault(s, "?") : string.Empty), ("Product", t.Product),
            ("In", Time(t.TimeInUtc)), ("Out", Time(t.TimeOutUtc)), ("Gross", Qty(t.GrossWeightKg)), ("Tare", Qty(t.TareWeightKg)), ("Deduction", Qty(t.DeductionKg)),
            ("Cubic", t.IsComplete ? Qty(t.NetCubicMetres) : "open"), ("Net", t.NetTonnes)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DailyAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<WeighbridgeTicket> tickets = [.. (await TicketListAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(t => t.IsComplete)];
        Dictionary<long, string> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        return [.. tickets.GroupBy(t => (t.Date, t.Product, Customer: t.CustomerId is long c ? customers.GetValueOrDefault(c, "?") : "—", t.VehicleRegistration, Driver: t.DriverName ?? "—"))
            .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Product, StringComparer.Ordinal).ThenBy(g => g.Key.Customer, StringComparer.Ordinal).ThenBy(g => g.Key.VehicleRegistration, StringComparer.Ordinal)
            .Select(g => Row(("Date", Date(g.Key.Date)), ("Product", g.Key.Product), ("Customer", g.Key.Customer), ("Vehicle", g.Key.VehicleRegistration), ("Driver", g.Key.Driver), ("Loads", g.Count()),
                ("Cubic", Qty(g.Sum(t => t.NetCubicMetres))), ("Tonnes", g.Sum(t => t.NetTonnes))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VehiclesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<WeighbridgeTicket> tickets = await TicketListAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        Dictionary<string, WeighbridgeVehicle> register = await _db.WeighbridgeVehicles.AsNoTracking().Where(v => v.CompanyId == companyId).ToDictionaryAsync(v => v.Registration, ct).ConfigureAwait(false);
        return [.. tickets.GroupBy(t => t.VehicleRegistration).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            WeighbridgeVehicle? vehicle = register.GetValueOrDefault(g.Key);
            List<int> minutes = [.. g.Select(t => WeighbridgeRules.TurnaroundMinutes(t.TimeInUtc, t.TimeOutUtc)).Where(m => m.HasValue).Select(m => m!.Value)];
            return Row(("Vehicle", g.Key), ("Haulier", vehicle?.Haulier ?? "—"), ("Loads", g.Count(t => t.IsComplete)), ("Average", minutes.Count == 0 ? "—" : Math.Round(minutes.Average()).ToString(CultureInfo.InvariantCulture)),
                ("Longest", minutes.Count == 0 ? "—" : minutes.Max().ToString(CultureInfo.InvariantCulture)), ("Open", g.Count(t => !t.IsComplete)),
                ("Overloads", vehicle?.MaxGrossKg is decimal max ? g.Count(t => t.IsComplete && t.GrossWeightKg > max) : 0), ("Tonnes", g.Sum(t => t.NetTonnes)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DeliveriesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<WeighbridgeTicket> tickets = [.. (await TicketListAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(t => t.IsComplete && t.Direction == WeighDirection.Outbound)];
        Dictionary<long, string> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        return [.. tickets.GroupBy(t => (Customer: t.CustomerId is long c ? customers.GetValueOrDefault(c, "?") : "Cash / walk-in", Site: t.SiteName ?? "—"))
            .OrderBy(g => g.Key.Customer, StringComparer.Ordinal).ThenBy(g => g.Key.Site, StringComparer.Ordinal)
            .Select(g => Row(("Customer", g.Key.Customer), ("Site", g.Key.Site), ("Loads", g.Count()), ("Tonnes", Qty(g.Sum(t => t.NetTonnes))), ("Cubic", Qty(g.Sum(t => t.NetCubicMetres))),
                ("Haulage", g.Sum(t => t.HaulageCharge))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ManualAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<WeighbridgeTicket> tickets = [.. (await TicketListAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(t => t.IsManualWeight)];
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        string User(long? id) => id is long u ? users.GetValueOrDefault(u, $"user {u}") : "—";
        return [.. tickets.Select(t => Row(("Ticket", t.TicketNumber), ("Date", Date(t.Date)), ("Vehicle", t.VehicleRegistration), ("Operator", User(t.OperatorUserId)), ("AuthorisedBy", User(t.ManualAuthorisedByUserId)),
            ("Reason", t.ManualReason ?? string.Empty), ("Net", t.NetTonnes)))];
    }

    // Movements classified by what made them: assembly receipts (ASM) are production, other receipts and transfers in are received,
    // issues on weighbridge tickets are sold, and everything else (other issues, transfers out, adjustments) is "adjusted / other".
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StockpilesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var piles = await _db.WeighbridgeTickets.AsNoTracking().Where(t => t.CompanyId == companyId && t.WarehouseId != null && t.ItemId != null)
            .Select(t => new { ItemId = t.ItemId!.Value, WarehouseId = t.WarehouseId!.Value }).Distinct().ToListAsync(ct).ConfigureAwait(false);
        List<string> ticketNumbers = await _db.WeighbridgeTickets.AsNoTracking().Where(t => t.CompanyId == companyId).Select(t => t.TicketNumber).ToListAsync(ct).ConfigureAwait(false);
        HashSet<string> tickets = [.. ticketNumbers];
        Dictionary<long, string> items = await _db.Items.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.Description, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await _db.Warehouses.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Id, w => w.Code, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (var pile in piles.OrderBy(p => items.GetValueOrDefault(p.ItemId), StringComparer.Ordinal).ThenBy(p => warehouses.GetValueOrDefault(p.WarehouseId), StringComparer.Ordinal))
        {
            List<StockMovement> moves = await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.ItemId == pile.ItemId && m.WarehouseId == pile.WarehouseId && m.Date <= toDate)
                .ToListAsync(ct).ConfigureAwait(false);
            decimal opening = moves.Where(m => m.Date < fromDate).Sum(m => m.Quantity);
            List<StockMovement> inPeriod = [.. moves.Where(m => m.Date >= fromDate)];
            decimal produced = inPeriod.Where(m => m.Type == StockMovementType.Receipt && (m.Reference ?? string.Empty).StartsWith("ASM", StringComparison.Ordinal)).Sum(m => m.Quantity);
            decimal received = inPeriod.Where(m => (m.Type == StockMovementType.Receipt && !(m.Reference ?? string.Empty).StartsWith("ASM", StringComparison.Ordinal))
                || (m.Type == StockMovementType.Transfer && m.Quantity > 0m)).Sum(m => m.Quantity);
            decimal sold = -inPeriod.Where(m => m.Type == StockMovementType.Issue && m.Reference != null && tickets.Contains(m.Reference)).Sum(m => m.Quantity);
            decimal closing = opening + inPeriod.Sum(m => m.Quantity);
            rows.Add(Row(("Product", items.GetValueOrDefault(pile.ItemId, "?")), ("Stockpile", warehouses.GetValueOrDefault(pile.WarehouseId, "?")), ("Opening", Qty(opening)), ("Produced", Qty(produced)),
                ("Received", Qty(received)), ("Sold", Qty(sold)), ("Adjusted", Qty(closing - opening - produced - received + sold)), ("Closing", closing)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BatchesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<AssemblyRun> runs = await _db.AssemblyRuns.AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId && r.Date >= fromDate && r.Date <= toDate)
            .OrderBy(r => r.Date).ThenBy(r => r.RunNumber).ToListAsync(ct).ConfigureAwait(false);
        List<string> numbers = [.. runs.Select(r => r.RunNumber)];
        var delivered = await _db.WeighbridgeTickets.AsNoTracking().Where(t => t.CompanyId == companyId && t.BatchRunNumber != null && numbers.Contains(t.BatchRunNumber) && t.IsComplete)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> boms = await _db.BillsOfMaterials.AsNoTracking().Where(b => b.CompanyId == companyId).ToDictionaryAsync(b => b.Id, b => b.Code, ct).ConfigureAwait(false);
        return [.. runs.Where(r => delivered.Any(t => t.BatchRunNumber == r.RunNumber)).Select(r =>
        {
            List<WeighbridgeTicket> loads = [.. delivered.Where(t => t.BatchRunNumber == r.RunNumber)];
            decimal standard = r.Lines.Sum(l => l.ActualQuantity == 0m ? 0m : Math.Round(l.StandardQuantity * l.Cost / l.ActualQuantity, 2));
            return Row(("Run", r.RunNumber), ("Date", Date(r.Date)), ("Mix", boms.GetValueOrDefault(r.BillOfMaterialsId, "?")), ("Produced", Qty(r.ActualOutput)),
                ("Delivered", Qty(loads.Sum(t => t.NetCubicMetres))), ("Loads", loads.Count), ("Cost", Money(r.TotalCost)), ("Variance", Math.Round(r.TotalCost, 2) - standard));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HaulageAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<WeighbridgeTicket> tickets = [.. (await TicketListAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(t => t.IsComplete && t.HaulageZoneCode != null)];
        Dictionary<string, string> zones = await _db.HaulageZones.AsNoTracking().Where(z => z.CompanyId == companyId).ToDictionaryAsync(z => z.Code, z => z.Name, ct).ConfigureAwait(false);
        return [.. tickets.GroupBy(t => t.HaulageZoneCode!).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            decimal tonneKm = g.Sum(t => t.NetTonnes * (t.DistanceKm ?? 0m)), charge = g.Sum(t => t.HaulageCharge);
            return Row(("Zone", g.Key), ("Name", zones.GetValueOrDefault(g.Key, "?")), ("Loads", g.Count()), ("Tonnes", Qty(g.Sum(t => t.NetTonnes))), ("Km", Qty(g.Sum(t => t.DistanceKm ?? 0m))),
                ("PerTonneKm", tonneKm == 0m ? "—" : Math.Round(charge / tonneKm, 4).ToString("0.####", CultureInfo.InvariantCulture)), ("Charge", charge));
        })];
    }

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Time(DateTimeOffset? at) => at is { } t ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : "—";

    private static string Qty(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }
}
