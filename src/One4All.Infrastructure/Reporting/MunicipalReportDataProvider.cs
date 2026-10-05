using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the M-series municipal reports (brief §14 I): every data command whose name starts with <c>Muni</c>.</summary>
public sealed class MunicipalReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public MunicipalReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Muni", StringComparison.OrdinalIgnoreCase) && !command.Equals("Municipal", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly asAt = parameters.TryGetValue("AsAtDate", out object? value) && value is DateOnly date ? date : DateOnly.FromDateTime(DateTime.Today);
        return command.ToUpperInvariant() switch
        {
            "MUNIBILLINGRUNS" => await BillingRunsAsync(companyId, ct).ConfigureAwait(false),
            "MUNIBILLINGEXCEPTIONS" => await BillingExceptionsAsync(companyId, ct).ConfigureAwait(false),
            "MUNIAGEDDEBTORS" => await AgedDebtorsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIVALUATIONROLL" => await ValuationRollAsync(companyId, ct).ConfigureAwait(false),
            "MUNIPROPERTYREGISTER" => await PropertyRegisterAsync(companyId, ct).ConfigureAwait(false),
            "MUNICLEARANCES" => await ClearancesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIREADINGSHEETS" => await ReadingSheetsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "MUNIREADINGEXCEPTIONS" => await ReadingExceptionsAsync(companyId, asAt, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BillingRunsAsync(long companyId, CancellationToken ct)
    {
        List<BillingRun> runs = await _db.Set<BillingRun>().AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId).OrderBy(r => r.BillDate).ThenBy(r => r.RunNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        return [.. runs.Select(r =>
        {
            List<BillingRunLine> included = [.. r.Lines.Where(l => l.Included)];
            return Row(("Run", r.RunNumber), ("Service", r.ServiceType.ToString()), ("Period", r.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture)), ("Ward", r.Ward ?? "All"),
                ("Status", r.Status.ToString()), ("Lines", included.Count), ("Excluded", r.Lines.Count - included.Count), ("Previous", Money(included.Sum(l => l.PreviousCharge ?? 0m))),
                ("Approver", r.ApprovedByUserId is long a ? users.GetValueOrDefault(a, $"user {a}") : "—"), ("Billed", included.Sum(l => l.Charge)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BillingExceptionsAsync(long companyId, CancellationToken ct)
    {
        List<BillingRun> runs = await _db.Set<BillingRun>().AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId).OrderBy(r => r.BillDate).ThenBy(r => r.RunNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. runs.SelectMany(r => r.Lines.Where(l => l.Exceptions != BillingExceptions.None).OrderBy(l => l.StandNumber, StringComparer.Ordinal)
            .Select(l => Row(("Run", r.RunNumber), ("Stand", l.StandNumber), ("Exceptions", l.Exceptions.ToString()), ("Message", l.Message ?? "—"), ("Included", l.Included ? "Yes" : "No"),
                ("Previous", l.PreviousCharge is decimal p ? Money(p) : "—"), ("Charge", l.Charge))))];
    }

    // Each ratepayer's open items as at the date: ward and category from their (first) property, service from the billing run that raised the invoice.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AgedDebtorsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        var properties = await _db.Properties.AsNoTracking().Include(p => p.Services).Where(p => p.CompanyId == companyId).OrderBy(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, ConsumerCategory> tariffCategories = await _db.Set<Tariff>().AsNoTracking().IgnoreQueryFilters().Where(t => t.CompanyId == companyId)
            .ToDictionaryAsync(t => t.Id, t => t.Category, ct).ConfigureAwait(false);
        Dictionary<long, (string Ward, string Category)> ratepayers = properties.GroupBy(p => p.RatepayerCustomerId).ToDictionary(g => g.Key, g =>
        {
            Property first = g.First();
            PropertyServiceLink? link = first.Services.FirstOrDefault(s => s.IsActive);
            return (first.Ward, link is null ? "—" : tariffCategories.GetValueOrDefault(link.TariffId).ToString());
        });
        Dictionary<string, string> services = (await _db.Set<BillingRun>().AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(r => r.Lines.Select(l => (l.InvoiceNumber, Service: r.ServiceType.ToString()))).GroupBy(x => x.InvoiceNumber, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Service, StringComparer.Ordinal);
        List<OpenItemAsAt> items = [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false)).Where(i => ratepayers.ContainsKey(i.Item.TraderId))];
        return [.. items.Select(i => (Key: (ratepayers[i.Item.TraderId].Ward, ratepayers[i.Item.TraderId].Category,
                    Service: i.Item.Sign == LedgerSign.Credit ? "Unallocated credit" : services.GetValueOrDefault(i.Item.DocumentNumber, "Other")), Item: i))
            .GroupBy(x => x.Key).OrderBy(g => g.Key.Ward, StringComparer.Ordinal).ThenBy(g => g.Key.Category, StringComparer.Ordinal).ThenBy(g => g.Key.Service, StringComparer.Ordinal)
            .Select(g =>
            {
                decimal[] bands = new decimal[DebtorAgeing.Bands.Count];
                foreach (var x in g)
                {
                    decimal signed = x.Item.Signed(LedgerSign.Debit);
                    bands[signed < 0m ? 0 : DebtorAgeing.Band(x.Item.Item.DueDate, asAt)] += signed;
                }

                return Row(("Ward", g.Key.Ward), ("Category", g.Key.Category), ("Service", g.Key.Service), ("Accounts", g.Select(x => x.Item.Item.TraderId).Distinct().Count()),
                    ("Current", Money(bands[0])), ("D30", Money(bands[1])), ("D60", Money(bands[2])), ("D90", Money(bands[3])), ("D120", Money(bands[4])), ("Over120", Money(bands[5])),
                    ("Total", bands.Sum()));
            })
            .Where(r => (decimal)r["Total"]! != 0m)];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ValuationRollAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, Property> properties = await _db.Properties.AsNoTracking().Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        Dictionary<long, ValuationObjection> objections = (await _db.Set<ValuationObjection>().AsNoTracking().Where(o => o.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(o => o.ObjectedValuationId).ToDictionary(g => g.Key, g => g.OrderBy(o => o.LodgedOn).Last());
        List<PropertyValuation> valuations = await _db.Set<PropertyValuation>().AsNoTracking().Where(v => v.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        return [.. valuations.Where(v => properties.ContainsKey(v.PropertyId))
            .OrderBy(v => properties[v.PropertyId].StandNumber, StringComparer.Ordinal).ThenBy(v => v.EffectiveFrom).ThenBy(v => v.Id)
            .Select(v => Row(("Stand", properties[v.PropertyId].StandNumber), ("Ward", properties[v.PropertyId].Ward), ("Kind", v.Kind.ToString()), ("Reference", v.Reference),
                ("EffectiveFrom", Date(v.EffectiveFrom)),
                ("Objection", objections.TryGetValue(v.Id, out ValuationObjection? o)
                    ? $"{o.ObjectionNumber} {o.Status}{(o.DecidedValue is decimal d ? $" at {Money(d)}" : string.Empty)}" : "—"),
                ("Value", v.Value)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PropertyRegisterAsync(long companyId, CancellationToken ct)
    {
        List<Property> properties = await _db.Properties.AsNoTracking().Include(p => p.Services).Where(p => p.CompanyId == companyId).OrderBy(p => p.StandNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        Dictionary<long, List<string>> owners = (await _db.Set<PropertyOwner>().AsNoTracking().Where(o => o.CompanyId == companyId && o.OwnedTo == null).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(o => o.PropertyId).ToDictionary(g => g.Key, g => g.Select(o => names.GetValueOrDefault(o.CustomerId, "?")).ToList());
        Dictionary<long, string> tariffs = await _db.Set<Tariff>().AsNoTracking().IgnoreQueryFilters().Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Code, ct).ConfigureAwait(false);
        Dictionary<long, string> meters = await _db.Set<Meter>().AsNoTracking().IgnoreQueryFilters().Where(m => m.CompanyId == companyId).ToDictionaryAsync(m => m.Id, m => m.Serial, ct).ConfigureAwait(false);
        return [.. properties.Select(p =>
        {
            List<PropertyServiceLink> active = [.. p.Services.Where(s => s.IsActive)];
            return Row(("Stand", p.StandNumber), ("Ward", p.Ward), ("Address", p.Address ?? "—"), ("Owners", owners.TryGetValue(p.Id, out List<string>? o) ? string.Join("; ", o) : "—"),
                ("Ratepayer", names.GetValueOrDefault(p.RatepayerCustomerId, "?")),
                ("Services", active.Count == 0 ? "—" : string.Join("; ", active.Select(s => $"{s.ServiceType} ({tariffs.GetValueOrDefault(s.TariffId, "?")})"))),
                ("Meters", string.Join("; ", active.Where(s => s.MeterId != null).Select(s => meters.GetValueOrDefault(s.MeterId!.Value, "?")).DefaultIfEmpty("—"))),
                ("Valuation", p.Valuation));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ClearancesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, string> stands = await _db.Properties.AsNoTracking().IgnoreQueryFilters().Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, p => p.StandNumber, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        List<ClearanceCertificate> certificates = await _db.Set<ClearanceCertificate>().AsNoTracking().Where(c => c.CompanyId == companyId && c.IssuedOn <= asAt)
            .OrderBy(c => c.IssuedOn).ThenBy(c => c.CertificateNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. certificates.Select(c => Row(("Certificate", c.CertificateNumber), ("Stand", stands.GetValueOrDefault(c.PropertyId, "?")), ("Issued", Date(c.IssuedOn)),
            ("ValidUntil", Date(c.ValidUntil)), ("State", c.ValidUntil >= asAt ? "Valid" : "Expired"), ("IssuedBy", users.GetValueOrDefault(c.IssuedByUserId, $"user {c.IssuedByUserId}")),
            ("AdvanceHeld", c.AdvanceHeld)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadingSheetsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string? only = parameters.TryGetValue("RouteCode", out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;
        List<ReadingRoute> routes = await _db.Set<ReadingRoute>().AsNoTracking().Include(r => r.Stops).Where(r => r.CompanyId == companyId && (only == null || r.Code == only))
            .OrderBy(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, Meter> meters = await _db.Set<Meter>().AsNoTracking().Where(m => m.CompanyId == companyId).ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        var placed = await (from s in _db.Set<PropertyServiceLink>().AsNoTracking() join p in _db.Properties.AsNoTracking() on s.PropertyId equals p.Id
                            where p.CompanyId == companyId && s.MeterId != null && s.IsActive select new { MeterId = s.MeterId!.Value, p.StandNumber, p.Address }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, (string Stand, string? Address)> where = placed.GroupBy(x => x.MeterId).ToDictionary(g => g.Key, g => (g.First().StandNumber, g.First().Address));
        return [.. routes.SelectMany(r => r.Stops.OrderBy(s => s.Sequence).Where(s => meters.ContainsKey(s.MeterId)).Select(s =>
        {
            Meter meter = meters[s.MeterId];
            (string stand, string? address) = where.GetValueOrDefault(s.MeterId, ("—", null));
            return Row(("Route", r.Code), ("Reader", r.ReaderName), ("Sequence", s.Sequence), ("Meter", meter.Serial), ("Stand", stand), ("Address", address ?? "—"),
                ("LastReading", meter.LastReading.ToString("0.##", CultureInfo.InvariantCulture)), ("LastDate", meter.LastReadingDate is DateOnly d ? Date(d) : "—"), ("NewReading", string.Empty));
        }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadingExceptionsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, string> serials = await _db.Set<Meter>().AsNoTracking().IgnoreQueryFilters().Where(m => m.CompanyId == companyId).ToDictionaryAsync(m => m.Id, m => m.Serial, ct).ConfigureAwait(false);
        var placed = await (from s in _db.Set<PropertyServiceLink>().AsNoTracking() join p in _db.Properties.AsNoTracking() on s.PropertyId equals p.Id
                            where p.CompanyId == companyId && s.MeterId != null select new { MeterId = s.MeterId!.Value, p.StandNumber }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stands = placed.GroupBy(x => x.MeterId).ToDictionary(g => g.Key, g => g.First().StandNumber);
        List<MeterReading> readings = await _db.Set<MeterReading>().AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.ReadingDate <= asAt && (r.Flags != ReadingFlags.None || r.Kind == MeterReadingKind.Estimated || !r.Accepted))
            .OrderBy(r => r.ReadingDate).ThenBy(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        return [.. readings.Select(r => Row(("Date", Date(r.ReadingDate)), ("Meter", serials.GetValueOrDefault(r.MeterId, "?")), ("Stand", stands.GetValueOrDefault(r.MeterId, "—")),
            ("Kind", r.Kind.ToString()), ("Flags", r.Flags == ReadingFlags.None ? "—" : r.Flags.ToString()), ("Previous", r.PreviousReading.ToString("0.##", CultureInfo.InvariantCulture)),
            ("Reading", r.Reading.ToString("0.##", CultureInfo.InvariantCulture)), ("Accepted", r.Accepted ? "Yes" : "No"), ("Consumption", r.Consumption)))];
    }

    private Task<Dictionary<long, string>> UsersAsync(CancellationToken ct) => _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct);

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
