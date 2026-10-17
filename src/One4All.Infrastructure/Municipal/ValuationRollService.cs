using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Reads a property's valuation history, shared by billing and the valuation roll.</summary>
internal static class ValuationHistory
{
    /// <summary>The narrative a rates or service bill carries, which identifies the bills of a tariff for a stand.</summary>
    /// <param name="tariffName">The tariff's name.</param>
    /// <param name="standNumber">The stand.</param>
    /// <returns>The narrative.</returns>
    public static string BillNarrative(string tariffName, string standNumber) => $"{tariffName} - stand {standNumber}";

    /// <summary>The property's valuation records in the order they were made.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The records.</returns>
    public static async Task<List<DatedValue>> LoadAsync(One4AllDbContext db, long propertyId, CancellationToken ct)
        => (await db.Set<PropertyValuation>().AsNoTracking().Where(v => v.PropertyId == propertyId).OrderBy(v => v.Id)
            .Select(v => new { v.EffectiveFrom, v.Value }).ToListAsync(ct).ConfigureAwait(false))
            .Select(v => new DatedValue(v.EffectiveFrom, v.Value)).ToList();
}

/// <summary>Resolves the version of a tariff code in force on a date, shared by billing, back-billing and vending.</summary>
internal static class TariffVersions
{
    /// <summary>The active versions of the linked tariff's code, oldest first, with their blocks.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="linked">The tariff a service is linked to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The versions.</returns>
    public static Task<List<Tariff>> LoadAsync(One4AllDbContext db, Tariff linked, CancellationToken ct)
        => db.Set<Tariff>().Include(t => t.Blocks).Include(t => t.Seasons).AsNoTracking()
            .Where(t => t.CompanyId == linked.CompanyId && t.Code == linked.Code && t.IsActive)
            .OrderBy(t => t.EffectiveFrom).ToListAsync(ct);

    /// <summary>The version in force on a date: the latest effective by then, or the linked one before any is.</summary>
    /// <param name="versions">The versions, oldest first.</param>
    /// <param name="linked">The linked tariff.</param>
    /// <param name="date">The date.</param>
    /// <returns>The version.</returns>
    public static Tariff InForce(IReadOnlyList<Tariff> versions, Tariff linked, DateOnly date)
        => versions.LastOrDefault(t => t.EffectiveFrom <= date) ?? linked;
}

/// <summary>The valuation roll, supplementary valuations and objections, with rates back-billing through AR.</summary>
public sealed class ValuationRollService : IValuationRollService
{
    private const string OpeningReference = "OPENING";

    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    public ValuationRollService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
    }

    /// <inheritdoc />
    public Task<Result<int>> ImportRollAsync(long companyId, string rollName, DateOnly effectiveFrom, IReadOnlyList<RollEntry> entries, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return _db.AtomicAsync(() => ImportCoreAsync(companyId, rollName, effectiveFrom, entries, ct), ct);
    }

    private async Task<Result<int>> ImportCoreAsync(long companyId, string rollName, DateOnly effectiveFrom, IReadOnlyList<RollEntry> entries, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rollName) || entries.Count == 0)
        {
            return Error.Validation("Mun.RollEmpty", "A roll needs a name and at least one value.");
        }

        List<string> duplicates = entries.GroupBy(e => e.StandNumber.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            return Error.Validation("Mun.RollDuplicate", $"Stands listed twice: {string.Join(", ", duplicates)}.");
        }

        if (entries.Any(e => e.Value < 0m))
        {
            return Error.Validation("Mun.NegativeValue", "A value cannot be negative.");
        }

        List<string> stands = entries.Select(e => e.StandNumber.Trim()).ToList();
        List<Property> properties = await _db.Properties.Where(p => p.CompanyId == companyId && stands.Contains(p.StandNumber)).ToListAsync(ct).ConfigureAwait(false);
        List<string> unknown = stands.Where(s => !properties.Any(p => p.StandNumber == s)).ToList();
        if (unknown.Count > 0)
        {
            return Error.NotFound("Mun.UnknownStand", $"Stands not on the register: {string.Join(", ", unknown)}.");
        }

        foreach (RollEntry entry in entries)
        {
            Property property = properties.Single(p => p.StandNumber == entry.StandNumber.Trim());
            List<DatedValue> history = await EnsureOpeningAsync(property, ct).ConfigureAwait(false);
            history.Add(new DatedValue(effectiveFrom, entry.Value));
            _db.Set<PropertyValuation>().Add(new PropertyValuation(companyId, property.Id, effectiveFrom, entry.Value, ValuationKind.General, rollName.Trim(), null));
            property.Valuation = ValuationRoll.ValueAt(history, DateOnly.MaxValue, property.Valuation);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entries.Count;
    }

    /// <inheritdoc />
    public Task<Result<ValuationAdjustment>> RecordSupplementaryAsync(long companyId, long propertyId, DateOnly effectiveFrom, decimal value, string reason, DateOnly adjustmentDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => SupplementaryCoreAsync(companyId, propertyId, effectiveFrom, value, reason, adjustmentDate, ct), ct);

    private async Task<Result<ValuationAdjustment>> SupplementaryCoreAsync(long companyId, long propertyId, DateOnly effectiveFrom, decimal value, string reason, DateOnly adjustmentDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Mun.SupplementaryReason", "Say why the property is revalued.");
        }

        if (value < 0m)
        {
            return Error.Validation("Mun.NegativeValue", "A value cannot be negative.");
        }

        Property? property = await _db.Properties.Include(p => p.Services).FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "SV", adjustmentDate, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        PropertyValuation record = new(companyId, propertyId, effectiveFrom, value, ValuationKind.Supplementary, number.Value, reason.Trim());
        return await ApplyAsync(property, record, adjustmentDate, $"Supplementary valuation {number.Value}: {reason.Trim()}", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<string>> LodgeObjectionAsync(long companyId, long propertyId, DateOnly lodgedOn, decimal claimedValue, string grounds, CancellationToken ct = default)
        => _db.AtomicAsync(() => LodgeCoreAsync(companyId, propertyId, lodgedOn, claimedValue, grounds, ct), ct);

    private async Task<Result<string>> LodgeCoreAsync(long companyId, long propertyId, DateOnly lodgedOn, decimal claimedValue, string grounds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(grounds))
        {
            return Error.Validation("Mun.ObjectionGrounds", "An objection needs its grounds.");
        }

        if (claimedValue < 0m)
        {
            return Error.Validation("Mun.NegativeValue", "A value cannot be negative.");
        }

        Property? property = await _db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        if (await _db.Set<ValuationObjection>().AnyAsync(o => o.PropertyId == propertyId && o.Status == ObjectionStatus.Lodged, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Mun.ObjectionOpen", $"Stand {property.StandNumber} already has an objection awaiting a decision.");
        }

        await EnsureOpeningAsync(property, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        List<PropertyValuation> records = await _db.Set<PropertyValuation>().AsNoTracking().Where(v => v.PropertyId == propertyId).OrderBy(v => v.Id).ToListAsync(ct).ConfigureAwait(false);
        PropertyValuation? inForce = null;
        foreach (PropertyValuation r in records.Where(r => r.EffectiveFrom <= lodgedOn))
        {
            if (inForce is null || r.EffectiveFrom >= inForce.EffectiveFrom)
            {
                inForce = r;
            }
        }

        if (inForce is null)
        {
            return Error.Rule("Mun.NoValuation", $"Stand {property.StandNumber} has no value in force on {lodgedOn:yyyy-MM-dd}.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "OBJ", lodgedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<ValuationObjection>().Add(new ValuationObjection(companyId, number.Value, propertyId, inForce.Id, inForce.Value, claimedValue, grounds.Trim(), lodgedOn));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result<ValuationAdjustment>> DecideObjectionAsync(long companyId, string objectionNumber, DateOnly decidedOn, decimal? revisedValue, string notes, CancellationToken ct = default)
        => _db.AtomicAsync(() => DecideCoreAsync(companyId, objectionNumber, decidedOn, revisedValue, notes, ct), ct);

    private async Task<Result<ValuationAdjustment>> DecideCoreAsync(long companyId, string objectionNumber, DateOnly decidedOn, decimal? revisedValue, string notes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return Error.Validation("Mun.DecisionNotes", "A decision needs its reasons.");
        }

        if (revisedValue < 0m)
        {
            return Error.Validation("Mun.NegativeValue", "A value cannot be negative.");
        }

        ValuationObjection? objection = await _db.Set<ValuationObjection>().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.ObjectionNumber == objectionNumber, ct).ConfigureAwait(false);
        if (objection is null)
        {
            return Error.NotFound("Mun.ObjectionNotFound", $"Objection {objectionNumber} not found.");
        }

        if (objection.Status != ObjectionStatus.Lodged)
        {
            return Error.Rule("Mun.ObjectionDecided", $"Objection {objectionNumber} has already been decided.");
        }

        if (revisedValue is not decimal value)
        {
            objection.Dismiss(decidedOn, notes.Trim());
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new ValuationAdjustment(0, 0m, null);
        }

        objection.Uphold(decidedOn, value, notes.Trim());
        DateOnly effectiveFrom = await _db.Set<PropertyValuation>().Where(v => v.Id == objection.ObjectedValuationId).Select(v => v.EffectiveFrom).FirstAsync(ct).ConfigureAwait(false);
        Property property = await _db.Properties.Include(p => p.Services).FirstAsync(p => p.Id == objection.PropertyId, ct).ConfigureAwait(false);
        PropertyValuation record = new(companyId, property.Id, effectiveFrom, value, ValuationKind.Objection, objectionNumber, null);
        return await ApplyAsync(property, record, decidedOn, $"Valuation objection {objectionNumber} upheld", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValuationEntry>> HistoryAsync(long companyId, long propertyId, CancellationToken ct = default)
        => await _db.Set<PropertyValuation>().AsNoTracking().Where(v => v.CompanyId == companyId && v.PropertyId == propertyId).OrderBy(v => v.Id)
            .Select(v => new ValuationEntry(v.EffectiveFrom, v.Value, v.Kind, v.Reference)).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObjectionEntry>> ObjectionsAsync(long companyId, ObjectionStatus? status, CancellationToken ct = default)
        => await (
            from o in _db.Set<ValuationObjection>().AsNoTracking()
            join p in _db.Properties.AsNoTracking() on o.PropertyId equals p.Id
            where o.CompanyId == companyId && (status == null || o.Status == status)
            orderby o.LodgedOn, o.ObjectionNumber
            select new ObjectionEntry(o.ObjectionNumber, p.StandNumber, o.LodgedOn, o.ValueObjectedTo, o.ClaimedValue, o.Status, o.DecidedValue))
            .ToListAsync(ct).ConfigureAwait(false);

    // Records the opening value as the first entry of a property with no history, so earlier bills keep their value.
    private async Task<List<DatedValue>> EnsureOpeningAsync(Property property, CancellationToken ct)
    {
        List<DatedValue> history = await ValuationHistory.LoadAsync(_db, property.Id, ct).ConfigureAwait(false);
        bool pending = _db.ChangeTracker.Entries<PropertyValuation>().Any(e => e.State == EntityState.Added && e.Entity.PropertyId == property.Id);
        if (history.Count == 0 && !pending)
        {
            _db.Set<PropertyValuation>().Add(new PropertyValuation(property.CompanyId, property.Id, DateOnly.MinValue, property.Valuation, ValuationKind.General, OpeningReference, null));
            history.Add(new DatedValue(DateOnly.MinValue, property.Valuation));
        }

        return history;
    }

    // Adds the record, then invoices or credits the difference on rates bills raised from its date up to the given date.
    private async Task<Result<ValuationAdjustment>> ApplyAsync(Property property, PropertyValuation record, DateOnly adjustmentDate, string narrative, CancellationToken ct)
    {
        List<DatedValue> before = await EnsureOpeningAsync(property, ct).ConfigureAwait(false);
        List<DatedValue> after = [.. before, new DatedValue(record.EffectiveFrom, record.Value)];
        _db.Set<PropertyValuation>().Add(record);
        property.Valuation = ValuationRoll.ValueAt(after, DateOnly.MaxValue, property.Valuation);

        PropertyServiceLink? rates = property.Services.FirstOrDefault(s => s.ServiceType == ServiceType.Rates && s.IsActive);
        Tariff? tariff = rates is null ? null
            : await _db.Set<Tariff>().Include(t => t.Blocks).Include(t => t.Seasons).AsNoTracking().FirstOrDefaultAsync(t => t.Id == rates.TariffId, ct).ConfigureAwait(false);
        if (tariff is null)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new ValuationAdjustment(0, 0m, null);
        }

        // Bills were raised on whichever version of the tariff was in force, under that version's name.
        List<Tariff> versions = await TariffVersions.LoadAsync(_db, tariff, ct).ConfigureAwait(false);
        List<string> billNarratives = [.. versions.Append(tariff).Select(t => ValuationHistory.BillNarrative(t.Name, property.StandNumber)).Distinct()];
        List<DateOnly> billDates = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == property.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == property.RatepayerCustomerId
                && i.ItemType == OpenItemType.Invoice && billNarratives.Contains(i.Narrative!) && i.DocumentDate >= record.EffectiveFrom && i.DocumentDate <= adjustmentDate)
            .Select(i => i.DocumentDate).ToListAsync(ct).ConfigureAwait(false);
        decimal amount = ValuationRoll.BackBilling(billDates, before, after, property.Valuation, (d, v) => TariffVersions.InForce(versions, tariff, d).CalculateCharge(0m, v));
        string revenueAccount = TariffVersions.InForce(versions, tariff, adjustmentDate).RevenueAccountCode;
        if (amount == 0m)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new ValuationAdjustment(billDates.Count, 0m, null);
        }

        Company company = await _db.Set<Company>().AsNoTracking().FirstAsync(c => c.Id == property.CompanyId, ct).ConfigureAwait(false);
        string document = $"{record.Reference}-ADJ";
        Result<TradingPostingResult> posted = amount > 0m
            ? await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = property.CompanyId, CustomerId = property.RatepayerCustomerId, DocumentNumber = document, DocumentDate = adjustmentDate,
                Currency = company.BaseCurrency, NetAmount = amount, SalesAccountCode = revenueAccount, Narrative = $"{narrative}, stand {property.StandNumber}",
            }, ct).ConfigureAwait(false)
            : await _ar.PostCreditNoteAsync(new SalesCreditNoteRequest
            {
                CompanyId = property.CompanyId, CustomerId = property.RatepayerCustomerId, DocumentNumber = document, DocumentDate = adjustmentDate,
                Currency = company.BaseCurrency, NetAmount = -amount, SalesAccountCode = revenueAccount, Narrative = $"{narrative}, stand {property.StandNumber}",
            }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ValuationAdjustment(billDates.Count, amount, document);
    }
}
