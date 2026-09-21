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
        "HousingStands", "StudentRegister", "OpenPurchaseOrders", "Menu", "TableStatus",
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
            _ => [],
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
