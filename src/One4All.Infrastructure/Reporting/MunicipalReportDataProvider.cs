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
            "MUNICONSUMPTION" => await ConsumptionAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIMETERS" => await MetersAsync(companyId, ct).ConfigureAwait(false),
            "MUNISERVICEORDERS" => await ServiceOrdersAsync(companyId, ct).ConfigureAwait(false),
            "MUNIARRANGEMENTS" => await ArrangementsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIINDIGENT" => await IndigentAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNILICENCES" => await LicencesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNITICKETSLA" => await TicketSlaAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNITARIFFS" => await TariffsAsync(companyId, ct).ConfigureAwait(false),
            "MUNIARREARS" => await ArrearsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIINTEREST" => await InterestAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIWRITEOFFS" => await WriteOffsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIREVENUE" => await RevenueAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIWARDPACK" => await WardPackAsync(companyId, parameters, asAt, ct).ConfigureAwait(false),
            "MUNISTATUTORYAGEING" => await StatutoryAgeingAsync(companyId, asAt, ct).ConfigureAwait(false),
            "MUNIPREPAID" => await PrepaidAsync(companyId, asAt, ct).ConfigureAwait(false),
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

    // Where each meter sits: stand, ward, service and the consumer category of its tariff.
    private async Task<Dictionary<long, (string Stand, string Ward, string Service, string Category)>> MeterPlacesAsync(long companyId, CancellationToken ct)
    {
        var placed = await (from s in _db.Set<PropertyServiceLink>().AsNoTracking()
                            join p in _db.Properties.AsNoTracking().IgnoreQueryFilters() on s.PropertyId equals p.Id
                            join t in _db.Tariffs.AsNoTracking().IgnoreQueryFilters() on s.TariffId equals t.Id
                            where p.CompanyId == companyId && s.MeterId != null
                            orderby s.IsActive descending
                            select new { MeterId = s.MeterId!.Value, p.StandNumber, p.Ward, s.ServiceType, t.Category }).ToListAsync(ct).ConfigureAwait(false);
        return placed.GroupBy(x => x.MeterId).ToDictionary(g => g.Key, g => (g.First().StandNumber, g.First().Ward, g.First().ServiceType.ToString(), g.First().Category.ToString()));
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ConsumptionAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly from = asAt.AddYears(-1), priorFrom = asAt.AddYears(-2);
        Dictionary<long, (string Stand, string Ward, string Service, string Category)> places = await MeterPlacesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> serials = await _db.Meters.AsNoTracking().IgnoreQueryFilters().Where(m => m.CompanyId == companyId).ToDictionaryAsync(m => m.Id, m => m.Serial, ct).ConfigureAwait(false);
        var readings = await _db.MeterReadings.AsNoTracking().Where(r => r.CompanyId == companyId && r.Accepted && r.ReadingDate > priorFrom && r.ReadingDate <= asAt)
            .Select(r => new { r.MeterId, r.ReadingDate, r.Consumption }).ToListAsync(ct).ConfigureAwait(false);
        return [.. readings.GroupBy(r => r.MeterId).Where(g => g.Any(r => r.ReadingDate > from))
            .OrderBy(g => places.GetValueOrDefault(g.Key).Stand, StringComparer.Ordinal).ThenBy(g => serials.GetValueOrDefault(g.Key), StringComparer.Ordinal)
            .Select(g =>
            {
                decimal current = g.Where(r => r.ReadingDate > from).Sum(r => r.Consumption);
                decimal prior = g.Where(r => r.ReadingDate <= from).Sum(r => r.Consumption);
                (string stand, _, string service, string category) = places.GetValueOrDefault(g.Key, ("—", "—", "—", "—"));
                return Row(("Stand", stand), ("Meter", serials.GetValueOrDefault(g.Key, "?")), ("Service", service), ("Category", category), ("Readings", g.Count(r => r.ReadingDate > from)),
                    ("PriorYear", prior.ToString("0.##", CultureInfo.InvariantCulture)),
                    ("Change", prior == 0m ? "—" : ((current - prior) * 100m / prior).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"), ("Consumption", current));
            })];
    }

    // A removed meter is replaced by the meter installed on the same stand and service on or after its removal date.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MetersAsync(long companyId, CancellationToken ct)
    {
        List<Meter> meters = await _db.Meters.AsNoTracking().IgnoreQueryFilters().Where(m => m.CompanyId == companyId).OrderBy(m => m.Serial).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, (string Stand, string Ward, string Service, string Category)> places = await MeterPlacesAsync(companyId, ct).ConfigureAwait(false);
        string? ReplacedBy(Meter meter)
        {
            if (meter.RemovedOn is not DateOnly removed || !places.TryGetValue(meter.Id, out var place))
            {
                return null;
            }

            return meters.Where(m => m.Id != meter.Id && m.InstalledOn >= removed && places.TryGetValue(m.Id, out var other) && other.Stand == place.Stand && other.Service == place.Service)
                .OrderBy(m => m.InstalledOn).Select(m => m.Serial).FirstOrDefault();
        }

        return [.. meters.Select(m =>
        {
            (string stand, _, string service, _) = places.GetValueOrDefault(m.Id, ("—", "—", "—", "—"));
            return Row(("Meter", m.Serial), ("Stand", stand), ("Service", service), ("Installed", m.InstalledOn is DateOnly i ? Date(i) : "—"), ("Removed", m.RemovedOn is DateOnly r ? Date(r) : "—"),
                ("ReplacedBy", ReplacedBy(m) ?? "—"),
                ("LastActual", m.LastActualDate is DateOnly a ? $"{m.LastActualReading.ToString("0.##", CultureInfo.InvariantCulture)} on {Date(a)}" : "—"),
                ("Status", m.RemovedOn is not null ? "Removed" : m.IsActive ? "Active" : "Inactive"), ("EstimatedSinceActual", m.EstimatedSinceActual));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ServiceOrdersAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, Property> properties = await _db.Properties.AsNoTracking().IgnoreQueryFilters().Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        List<ServiceOrder> orders = await _db.Set<ServiceOrder>().AsNoTracking().Where(o => o.CompanyId == companyId).OrderBy(o => o.RaisedOn).ThenBy(o => o.OrderNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. orders.Select(o => Row(("Order", o.OrderNumber), ("Kind", o.Kind.ToString()), ("Stand", properties.GetValueOrDefault(o.PropertyId)?.StandNumber ?? "?"),
            ("Ward", properties.GetValueOrDefault(o.PropertyId)?.Ward ?? "?"), ("Service", o.ServiceType.ToString()), ("Raised", Date(o.RaisedOn)), ("Status", o.Status.ToString()),
            ("Executed", o.ExecutedOn is DateOnly e ? Date(e) : "—"), ("Fee", o.FeeInvoiceNumber ?? "—"), ("Arrears", o.Arrears)))];
    }

    // Standing measured as the arrangement monitor does, read-only; ward from the debtor's (first) property.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ArrangementsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<Core.Receipting.PaymentArrangement> arrangements = await _db.Set<Core.Receipting.PaymentArrangement>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.StartDate <= asAt).OrderBy(a => a.StartDate).ThenBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        List<long> customers = [.. arrangements.Select(a => a.CustomerId).Distinct()];
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => customers.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        Dictionary<long, string> wards = (await _db.Properties.AsNoTracking().Where(p => p.CompanyId == companyId && customers.Contains(p.RatepayerCustomerId)).OrderBy(p => p.Id)
            .Select(p => new { p.RatepayerCustomerId, p.Ward }).ToListAsync(ct).ConfigureAwait(false)).GroupBy(p => p.RatepayerCustomerId).ToDictionary(g => g.Key, g => g.First().Ward);
        var receipts = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Receipt
            && customers.Contains(i.TraderId) && i.DocumentDate <= asAt).Select(i => new { i.TraderId, i.DocumentDate, i.OriginalBase }).ToListAsync(ct).ConfigureAwait(false);
        return [.. arrangements.Select(a =>
        {
            DateOnly from = new(a.StartDate.Year, a.StartDate.Month, 1);
            decimal paid = receipts.Where(r => r.TraderId == a.CustomerId && r.DocumentDate >= from).Sum(r => r.OriginalBase);
            var standing = Application.Trading.InstalmentSchedule.Assess(Application.Trading.InstalmentSchedule.Build(a.TotalAmount, a.InstalmentAmount, a.StartDate), paid, asAt, 0, int.MaxValue);
            return Row(("Ward", wards.GetValueOrDefault(a.CustomerId, "—")), ("Debtor", names.GetValueOrDefault(a.CustomerId, "?")), ("Started", Date(a.StartDate)), ("Total", Money(a.TotalAmount)),
                ("Instalment", Money(a.InstalmentAmount)), ("Status", a.Status.ToString()), ("DueToDate", Money(standing.DueToDate)), ("Paid", Money(standing.Paid)),
                ("Missed", standing.InstalmentsMissed), ("Shortfall", standing.Shortfall));
        })];
    }

    // Subsidy credits are the IND-{stand}-{yyyyMM} items the subsidy run raises on the ratepayer's account.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> IndigentAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Property> properties = await _db.Properties.AsNoTracking().IgnoreQueryFilters().Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        List<IndigentRegistration> registrations = await _db.Set<IndigentRegistration>().AsNoTracking().Where(r => r.CompanyId == companyId && r.ApprovedFrom <= asAt).ToListAsync(ct).ConfigureAwait(false);
        DateOnly yearStart = new(asAt.Year, 1, 1);
        var credits = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.DocumentNumber.StartsWith("IND-")
            && i.DocumentDate >= yearStart && i.DocumentDate <= asAt).Select(i => new { i.DocumentNumber, i.OriginalBase }).ToListAsync(ct).ConfigureAwait(false);
        return [.. registrations.Where(r => properties.ContainsKey(r.PropertyId)).OrderBy(r => properties[r.PropertyId].Ward, StringComparer.Ordinal).ThenBy(r => properties[r.PropertyId].StandNumber, StringComparer.Ordinal)
            .Select(r =>
            {
                Property p = properties[r.PropertyId];
                return Row(("Stand", p.StandNumber), ("Ward", p.Ward), ("Income", Money(r.HouseholdIncome)), ("Rate", r.SubsidyPercent.ToString("0.##", CultureInfo.InvariantCulture)),
                    ("ApprovedFrom", Date(r.ApprovedFrom)), ("ReviewDue", Date(r.ReviewDue) + (r.ReviewDue < asAt && r.Status == IndigentStatus.Active ? " (overdue)" : string.Empty)),
                    ("Status", r.Status.ToString()), ("Subsidy", credits.Where(c => c.DocumentNumber.StartsWith($"IND-{p.StandNumber}-", StringComparison.Ordinal)).Sum(c => c.OriginalBase)));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LicencesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<CouncilLicence> licences = await _db.Set<CouncilLicence>().AsNoTracking().Where(l => l.CompanyId == companyId).OrderBy(l => l.ExpiresOn).ThenBy(l => l.LicenceNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> holders = [.. licences.Select(l => l.HolderCustomerId).Distinct()];
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => holders.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        Dictionary<long, CouncilInspection> inspections = (await _db.Set<CouncilInspection>().AsNoTracking().Where(i => i.CompanyId == companyId && i.InspectedOn <= asAt).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(i => i.LicenceId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.InspectedOn).ThenBy(i => i.Id).Last());
        return [.. licences.Select(l => Row(("Licence", l.LicenceNumber), ("Kind", l.Kind.ToString()), ("Holder", names.GetValueOrDefault(l.HolderCustomerId, "?")), ("Expires", Date(l.ExpiresOn)),
            ("Due", l.Status != CouncilLicenceStatus.Active ? "—" : l.ExpiresOn < asAt ? $"expired {asAt.DayNumber - l.ExpiresOn.DayNumber} days" : (l.ExpiresOn.DayNumber - asAt.DayNumber).ToString(CultureInfo.InvariantCulture)),
            ("Status", l.Status.ToString()), ("Renewals", l.Renewals),
            ("Inspection", inspections.TryGetValue(l.Id, out CouncilInspection? i) ? $"{Date(i.InspectedOn)} {(i.Passed ? "passed" : "failed")} ({i.Inspector})" : "none"), ("Fee", l.Fee)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TicketSlaAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateTimeOffset end = new(asAt.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        Dictionary<long, string> categories = await _db.Set<TicketCategory>().AsNoTracking().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        List<ServiceTicket> tickets = await _db.Set<ServiceTicket>().AsNoTracking().Where(t => t.CompanyId == companyId && t.ReportedAt <= end).OrderBy(t => t.ReportedAt).ToListAsync(ct).ConfigureAwait(false);
        static string Against(DateTimeOffset? done, DateTimeOffset due, DateTimeOffset now)
            => done is DateTimeOffset at ? (at <= due ? "Met" : $"Late {(at - due).TotalHours:0.#}h") : now > due ? "Breached (open)" : "Open, within SLA";
        return [.. tickets.Select(t =>
        {
            DateTimeOffset stop = t.ClosedAt ?? t.ResolvedAt ?? end;
            return Row(("Ticket", t.Reference), ("Category", categories.GetValueOrDefault(t.CategoryId, "?")), ("Priority", t.Priority.ToString()),
                ("Reported", t.ReportedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Status", t.Status.ToString()),
                ("Response", Against(t.RespondedAt, t.ResponseDueAt, end)), ("Resolution", Against(t.ResolvedAt, t.ResolutionDueAt, end)),
                ("Hours", Math.Max(0, (stop - t.ReportedAt).TotalHours).ToString("0.#", CultureInfo.InvariantCulture)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TariffsAsync(long companyId, CancellationToken ct)
    {
        List<Tariff> tariffs = await _db.Tariffs.AsNoTracking().Include(t => t.Blocks).Include(t => t.Seasons).Where(t => t.CompanyId == companyId).OrderBy(t => t.ServiceType).ThenBy(t => t.Code).ThenBy(t => t.EffectiveFrom)
            .ToListAsync(ct).ConfigureAwait(false);
        static string Amount(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        return [.. tariffs.Select(t =>
        {
            List<string> charges = [];
            if (t.FixedCharge != 0m) charges.Add($"fixed {Amount(t.FixedCharge)}");
            if (t.PerUnitRate != 0m) charges.Add($"per unit {Amount(t.PerUnitRate)}");
            if (t.RatePerValuationCents != 0m) charges.Add($"{Amount(t.RatePerValuationCents)} c per value");
            if (t.RatePerSquareMetre != 0m) charges.Add($"per m² {Amount(t.RatePerSquareMetre)}");
            if (t.MinimumCharge != 0m) charges.Add($"minimum {Amount(t.MinimumCharge)}");
            IEnumerable<string> blocks = t.Blocks.Select(b => $"{Amount(b.FromUnit)}–{(b.ToUnit is decimal to ? Amount(to) : "∞")} @ {Amount(b.Rate)}")
                .Concat(t.Seasons.Select(s => $"months {s.FromMonth}–{s.ToMonth} @ {Amount(s.PerUnitRate)}"));
            return Row(("Tariff", t.Code), ("Name", t.Name), ("Service", t.ServiceType.ToString()), ("Type", t.TariffType.ToString()), ("Category", t.Category.ToString()), ("Ward", t.Ward ?? "All"),
                ("Effective", Date(t.EffectiveFrom)), ("Charges", charges.Count == 0 ? "—" : string.Join(", ", charges)), ("Blocks", string.Join("; ", blocks.DefaultIfEmpty("—"))),
                ("Active", t.IsActive ? "Yes" : "No"));
        })];
    }

    // Each ratepayer's ward, consumer category and property count (from their first property and its first active service).
    private async Task<Dictionary<long, (string Ward, string Category, int Properties)>> RatepayersAsync(long companyId, CancellationToken ct)
    {
        List<Property> properties = await _db.Properties.AsNoTracking().Include(p => p.Services).Where(p => p.CompanyId == companyId).OrderBy(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, ConsumerCategory> categories = await _db.Tariffs.AsNoTracking().IgnoreQueryFilters().Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Category, ct).ConfigureAwait(false);
        return properties.GroupBy(p => p.RatepayerCustomerId).ToDictionary(g => g.Key, g =>
        {
            PropertyServiceLink? link = g.SelectMany(p => p.Services).FirstOrDefault(s => s.IsActive);
            return (g.First().Ward, link is null ? "—" : categories.GetValueOrDefault(link.TariffId).ToString(), g.Count());
        });
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ArrearsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, (string Ward, string Category, int Properties)> ratepayers = await RatepayersAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItemAsAt> items = [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false)).Where(i => ratepayers.ContainsKey(i.Item.TraderId))];
        List<long> debtors = [.. items.Select(i => i.Item.TraderId).Distinct()];
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => debtors.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        Dictionary<long, DateOnly> lastPaid = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Receipt
            && debtors.Contains(i.TraderId) && i.DocumentDate <= asAt).GroupBy(i => i.TraderId).Select(g => new { g.Key, Last = g.Max(i => i.DocumentDate) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct).ConfigureAwait(false);
        Dictionary<long, string> arrangements = (await _db.Set<Core.Receipting.PaymentArrangement>().AsNoTracking().Where(a => a.CompanyId == companyId && debtors.Contains(a.CustomerId) && a.StartDate <= asAt)
            .ToListAsync(ct).ConfigureAwait(false)).GroupBy(a => a.CustomerId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.StartDate).ThenBy(a => a.Id).Last().Status.ToString());
        return [.. items.GroupBy(i => i.Item.TraderId).Select(g => (Debtor: g.Key, Owing: g.Sum(i => i.Signed(LedgerSign.Debit)),
                Overdue: Math.Max(0m, g.Sum(i => i.Signed(LedgerSign.Debit) < 0m || i.Item.DueDate < asAt ? i.Signed(LedgerSign.Debit) : 0m))))
            .Where(x => x.Overdue > 0m).OrderByDescending(x => x.Overdue).ThenBy(x => names.GetValueOrDefault(x.Debtor), StringComparer.Ordinal)
            .Select(x => Row(("Debtor", names.GetValueOrDefault(x.Debtor, "?")), ("Ward", ratepayers[x.Debtor].Ward), ("Properties", ratepayers[x.Debtor].Properties), ("Overdue", Money(x.Overdue)),
                ("LastPayment", lastPaid.TryGetValue(x.Debtor, out DateOnly paid) ? Date(paid) : "never"), ("Arrangement", arrangements.GetValueOrDefault(x.Debtor, "—")), ("Owing", x.Owing)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> InterestAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, (string Ward, string Category, int Properties)> ratepayers = await RatepayersAsync(companyId, ct).ConfigureAwait(false);
        List<long> debtors = [.. ratepayers.Keys];
        DateOnly yearStart = new(asAt.Year, 1, 1);
        var interest = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Interest
            && debtors.Contains(i.TraderId) && i.DocumentDate >= yearStart && i.DocumentDate <= asAt).OrderBy(i => i.DocumentDate).ThenBy(i => i.DocumentNumber)
            .Select(i => new { i.DocumentDate, i.TraderId, i.DocumentNumber, i.OriginalBase }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => debtors.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        return [.. interest.Select(i => Row(("Date", Date(i.DocumentDate)), ("Debtor", names.GetValueOrDefault(i.TraderId, "?")), ("Ward", ratepayers[i.TraderId].Ward), ("Document", i.DocumentNumber),
            ("Amount", i.OriginalBase)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WriteOffsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, (string Ward, string Category, int Properties)> ratepayers = await RatepayersAsync(companyId, ct).ConfigureAwait(false);
        List<long> debtors = [.. ratepayers.Keys];
        DateOnly yearStart = new(asAt.Year, 1, 1);
        var items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable
            && (i.ItemType == OpenItemType.WriteOff || i.ItemType == OpenItemType.CreditNote) && debtors.Contains(i.TraderId) && i.DocumentDate >= yearStart && i.DocumentDate <= asAt)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.DocumentNumber).Select(i => new { i.DocumentDate, i.TraderId, i.ItemType, i.DocumentNumber, i.OriginalBase }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => debtors.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        Dictionary<long, List<DebtCase>> cases = (await _db.Set<DebtCase>().AsNoTracking().Where(c => c.CompanyId == companyId && c.Stage == DebtCaseStage.WrittenOff).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.CustomerId).ToDictionary(g => g.Key, g => g.ToList());
        string Approval(long debtor, DateOnly date)
        {
            DebtCase? debtCase = cases.GetValueOrDefault(debtor)?.Where(c => c.ClosedOn == date).FirstOrDefault() ?? cases.GetValueOrDefault(debtor)?.FirstOrDefault();
            return debtCase is null ? "—"
                : $"{debtCase.CaseNumber}, approved by {(debtCase.WriteOffApprovedByUserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "?")}";
        }

        return [.. items.Select(i => Row(("Date", Date(i.DocumentDate)), ("Debtor", names.GetValueOrDefault(i.TraderId, "?")), ("Ward", ratepayers[i.TraderId].Ward),
            ("Type", i.ItemType == OpenItemType.WriteOff ? "Write-off" : "Credit adjustment"), ("Document", i.DocumentNumber),
            ("Approval", i.ItemType == OpenItemType.WriteOff ? Approval(i.TraderId, i.DocumentDate) : "—"), ("Amount", i.OriginalBase)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RevenueAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly yearStart = new(asAt.Year, 1, 1);
        List<BillingRun> runs = await _db.Set<BillingRun>().AsNoTracking().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.Status == BillingRunStatus.Posted && r.BillDate >= yearStart && r.BillDate <= asAt).ToListAsync(ct).ConfigureAwait(false);
        List<(string Source, string Vote, int Documents, decimal Amount)> rows = [.. runs.SelectMany(r => r.Lines.Where(l => l.Included).Select(l => (Service: r.ServiceType.ToString(), l.RevenueAccountCode, l.Charge)))
            .GroupBy(x => (x.Service, x.RevenueAccountCode)).Select(g => ($"Billed: {g.Key.Service}", g.Key.RevenueAccountCode, g.Count(), g.Sum(x => x.Charge)))];
        rows.AddRange((await _db.Set<PrepaidSale>().AsNoTracking().Where(p => p.CompanyId == companyId && p.SoldOn >= yearStart && p.SoldOn <= asAt).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(p => p.ServiceType).Select(g => ($"Prepaid: {g.Key}", "—", g.Count(), g.Sum(p => p.Amount - p.ToArrears))));
        rows.AddRange((await _db.Set<CouncilLicence>().AsNoTracking().Where(l => l.CompanyId == companyId && l.IssuedOn >= yearStart && l.IssuedOn <= asAt).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => (l.Kind, l.RevenueAccountCode)).Select(g => ($"Licences: {g.Key.Kind}", g.Key.RevenueAccountCode, g.Count(), g.Sum(l => l.Fee))));
        return [.. rows.OrderBy(r => r.Source, StringComparer.Ordinal).ThenBy(r => r.Vote, StringComparer.Ordinal)
            .Select(r => Row(("Source", r.Source), ("Vote", r.Vote), ("Documents", r.Documents), ("Amount", r.Amount)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StatutoryAgeingAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, (string Ward, string Category, int Properties)> ratepayers = await RatepayersAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItemAsAt> items = [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false)).Where(i => ratepayers.ContainsKey(i.Item.TraderId))];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        decimal[] grand = new decimal[DebtorAgeing.Bands.Count];
        foreach (IGrouping<string, OpenItemAsAt> category in items.GroupBy(i => ratepayers[i.Item.TraderId].Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            decimal[] bands = Age(category, asAt);
            for (int b = 0; b < bands.Length; b++)
            {
                grand[b] += bands[b];
            }

            rows.Add(AgeingRow(category.Key, category.Select(i => i.Item.TraderId).Distinct().Count(), bands));
        }

        rows.Add(AgeingRow("Total", items.Select(i => i.Item.TraderId).Distinct().Count(), grand, total: false));
        return rows;
    }

    private static decimal[] Age(IEnumerable<OpenItemAsAt> items, DateOnly asAt)
    {
        decimal[] bands = new decimal[DebtorAgeing.Bands.Count];
        foreach (OpenItemAsAt item in items)
        {
            decimal signed = item.Signed(LedgerSign.Debit);
            bands[signed < 0m ? 0 : DebtorAgeing.Band(item.Item.DueDate, asAt)] += signed;
        }

        return bands;
    }

    // The grand-total row carries its total as text, so the report footer (the sum of the money column) is not doubled.
    private static IReadOnlyDictionary<string, object?> AgeingRow(string category, int accounts, decimal[] bands, bool total = true)
        => Row(("Category", category), ("Accounts", accounts), ("Current", Money(bands[0])), ("D30", Money(bands[1])), ("D60", Money(bands[2])), ("D90", Money(bands[3])), ("D120", Money(bands[4])),
            ("Over120", Money(bands[5])), ("Total", total ? bands.Sum() : 0m));

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PrepaidAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly yearStart = new(asAt.Year, 1, 1);
        List<PrepaidSale> sales = await _db.Set<PrepaidSale>().AsNoTracking().Where(p => p.CompanyId == companyId && p.SoldOn >= yearStart && p.SoldOn <= asAt).ToListAsync(ct).ConfigureAwait(false);
        List<BillingRun> runs = await _db.Set<BillingRun>().AsNoTracking().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.Status == BillingRunStatus.Posted && r.BillDate >= yearStart && r.BillDate <= asAt).ToListAsync(ct).ConfigureAwait(false);
        HashSet<ServiceType> services = [.. sales.Select(s => s.ServiceType)];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        for (int month = 1; month <= asAt.Month; month++)
        {
            foreach (ServiceType service in services.Order())
            {
                List<PrepaidSale> vends = [.. sales.Where(s => s.ServiceType == service && s.SoldOn.Month == month)];
                decimal conventional = runs.Where(r => r.ServiceType == service && r.BillDate.Month == month).SelectMany(r => r.Lines).Where(l => l.Included).Sum(l => l.Charge);
                decimal prepaid = vends.Sum(v => v.Amount - v.ToArrears);
                rows.Add(Row(("Month", $"{asAt.Year}-{month:00}"), ("Service", service.ToString()), ("Vends", vends.Count), ("Units", vends.Sum(v => v.Units).ToString("0.##", CultureInfo.InvariantCulture)),
                    ("ToArrears", Money(vends.Sum(v => v.ToArrears))), ("Conventional", Money(conventional)),
                    ("Share", prepaid + conventional == 0m ? "—" : (prepaid * 100m / (prepaid + conventional)).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Prepaid", prepaid)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WardPackAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, DateOnly asAt, CancellationToken ct)
    {
        string? only = parameters.TryGetValue("Ward", out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;
        List<Property> properties = await _db.Properties.AsNoTracking().Include(p => p.Services).Where(p => p.CompanyId == companyId && (only == null || p.Ward == only)).ToListAsync(ct).ConfigureAwait(false);
        List<OpenItemAsAt> items = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false);
        DateOnly yearStart = new(asAt.Year, 1, 1);
        List<BillingRun> runs = await _db.Set<BillingRun>().AsNoTracking().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.Status == BillingRunStatus.Posted && r.BillDate >= yearStart && r.BillDate <= asAt).ToListAsync(ct).ConfigureAwait(false);
        var orders = await _db.Set<ServiceOrder>().AsNoTracking().Where(o => o.CompanyId == companyId).Select(o => new { o.PropertyId, o.Kind, o.Status }).ToListAsync(ct).ConfigureAwait(false);
        var indigent = await _db.Set<IndigentRegistration>().AsNoTracking().Where(r => r.CompanyId == companyId && r.Status == IndigentStatus.Active).Select(r => r.PropertyId).ToListAsync(ct).ConfigureAwait(false);
        var tickets = await _db.Set<ServiceTicket>().AsNoTracking().Where(t => t.CompanyId == companyId && t.PropertyId != null && (t.Status == TicketStatus.Open || t.Status == TicketStatus.Assigned))
            .Select(t => t.PropertyId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var licences = await _db.Set<CouncilLicence>().AsNoTracking().Where(l => l.CompanyId == companyId && l.PropertyId != null && l.Status == CouncilLicenceStatus.Active)
            .Select(l => new { PropertyId = l.PropertyId!.Value, l.ExpiresOn }).ToListAsync(ct).ConfigureAwait(false);
        List<Core.Receipting.PaymentArrangement> arrangements = await _db.Set<Core.Receipting.PaymentArrangement>().AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (IGrouping<string, Property> ward in properties.GroupBy(p => p.Ward).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            void Add(string section, string item, string v) => rows.Add(Row(("Section", section), ("Item", item), ("Value", v)));
            HashSet<long> ids = [.. ward.Select(p => p.Id)];
            HashSet<long> payers = [.. ward.Select(p => p.RatepayerCustomerId)];
            string w = $"Ward {ward.Key}";
            Add(w, "Properties", ward.Count().ToString(CultureInfo.InvariantCulture));
            Add(w, "Total valuation", Money(ward.Sum(p => p.Valuation)));
            Add(w, $"Billed {asAt.Year} to date", Money(runs.SelectMany(r => r.Lines).Where(l => l.Included && ids.Contains(l.PropertyId)).Sum(l => l.Charge)));
            decimal[] bands = Age(items.Where(i => payers.Contains(i.Item.TraderId)), asAt);
            Add(w, "Debtors (owing)", Money(bands.Sum()));
            Add(w, "of which over 90 days", Money(bands[4] + bands[5]));
            Add(w, "Payment arrangements active", arrangements.Count(a => payers.Contains(a.CustomerId) && a.Status == Core.Receipting.ArrangementStatus.Active).ToString(CultureInfo.InvariantCulture));
            Add(w, "Disconnection orders open", orders.Count(o => ids.Contains(o.PropertyId) && o.Kind == ServiceOrderKind.Disconnect && o.Status == ServiceOrderStatus.Open).ToString(CultureInfo.InvariantCulture));
            Add(w, "Indigent households", indigent.Count(ids.Contains).ToString(CultureInfo.InvariantCulture));
            Add(w, "Open fault tickets", tickets.Count(ids.Contains).ToString(CultureInfo.InvariantCulture));
            Add(w, "Licences expiring within 30 days", licences.Count(l => ids.Contains(l.PropertyId) && l.ExpiresOn >= asAt && l.ExpiresOn <= asAt.AddDays(30)).ToString(CultureInfo.InvariantCulture));
        }

        return rows;
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
