using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>
/// Municipal billing (brief §4.9). Records meter readings and bills a property's services by resolving the
/// linked tariff, computing the charge (metered / valuation / flat) and raising it on the ratepayer's
/// account through the central AR ledger, so it posts through the engine and ages like any receivable.
/// </summary>
public sealed class MunicipalBillingService : IMunicipalBillingService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _salesLedger;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="salesLedger">The AR ledger the charge is raised on.</param>
    public MunicipalBillingService(One4AllDbContext db, ISalesLedgerService salesLedger)
    {
        _db = db;
        _salesLedger = salesLedger;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> RecordReadingAsync(long companyId, long meterId, DateOnly readingDate, decimal reading, CancellationToken ct = default)
    {
        Meter? meter = await _db.Set<Meter>()
            .FirstOrDefaultAsync(m => m.Id == meterId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (meter is null)
        {
            return Error.NotFound("Mun.MeterNotFound", $"Meter {meterId} not found.");
        }

        decimal previous = meter.LastReading;
        if (reading < previous)
        {
            return Error.Rule("Mun.ReadingRegressed", $"Reading {reading} is below the previous reading {previous}; record a correction instead.");
        }

        decimal consumption = meter.RecordReading(reading, readingDate);
        _db.Set<MeterReading>().Add(new MeterReading(companyId, meterId, readingDate, reading, previous, consumption));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return consumption;
    }

    /// <inheritdoc />
    public async Task<Result<MunicipalBillingRunResult>> RunFixedChargeBillingAsync(long companyId, ServiceType serviceType, DateOnly date, string invoicePrefix, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoicePrefix))
        {
            return Error.Validation("Mun.PrefixRequired", "A billing run needs a bill-number prefix.");
        }

        string prefix = invoicePrefix.Trim();

        // Services in place for at least part of the month: connected by its end and either running (not cut off) or
        // ended within it. Part months are billed pro rata.
        (DateOnly monthStart, DateOnly monthEnd) = ProRata.Month(date);
        List<long> propertyIds = await _db.Set<Property>()
            .Where(p => p.CompanyId == companyId && p.Services.Any(s => s.ServiceType == serviceType && s.StartDate <= monthEnd
                && ((s.IsActive && (s.EndDate == null || s.EndDate >= monthStart)) || (s.EndDate != null && s.EndDate >= monthStart))))
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        // One bill per property per service per month: the number carries the billing month, and a number
        // already on the receivables ledger means that property has been billed for the month.
        Dictionary<long, string> numbers = propertyIds.ToDictionary(id => id, id => $"{prefix}-{id}-{date:yyyyMM}");
        List<string> candidates = [.. numbers.Values];
        HashSet<string> alreadyRaised = (await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && candidates.Contains(i.DocumentNumber))
            .Select(i => i.DocumentNumber)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        int billed = 0;
        int skipped = 0;
        decimal total = 0m;
        foreach (long propertyId in propertyIds)
        {
            string invoiceNumber = numbers[propertyId];
            if (alreadyRaised.Contains(invoiceNumber))
            {
                skipped++;
                continue;
            }

            Result<BillingResult> result = await _db.AtomicAsync(() => BillServiceCoreAsync(companyId, propertyId, serviceType, 0m, invoiceNumber, date, 0m, null, ct, prorate: true), ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return result.Error;
            }

            billed++;
            total += result.Value.Charge;
        }

        return new MunicipalBillingRunResult(billed, decimal.Round(total, 2, MidpointRounding.AwayFromZero), skipped);
    }

    /// <inheritdoc />
    public Task<Result<BillingResult>> BillServiceAsync(long companyId, long propertyId, ServiceType serviceType, decimal consumption, string invoiceNumber, DateOnly date, decimal taxAmount = 0m, string? taxAccountCode = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => BillServiceCoreAsync(companyId, propertyId, serviceType, consumption, invoiceNumber, date, taxAmount, taxAccountCode, ct, prorate: false), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    // With prorate, the link may have ended within the bill's month and the charge covers only the days it was in place.
    private async Task<Result<BillingResult>> BillServiceCoreAsync(long companyId, long propertyId, ServiceType serviceType, decimal consumption, string invoiceNumber, DateOnly date, decimal taxAmount, string? taxAccountCode, CancellationToken ct, bool prorate)
    {
        Property? property = await _db.Set<Property>().Include(p => p.Services)
            .FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        decimal fraction = 1m;
        PropertyServiceLink? link = property.Services.FirstOrDefault(s => s.ServiceType == serviceType && s.IsActive);
        if (prorate)
        {
            link = property.Services.Where(s => s.ServiceType == serviceType)
                .Select(s => (Link: s, Share: s.IsActive || s.EndDate != null ? ProRata.Fraction(date, s.StartDate, s.EndDate) : 0m))
                .Where(x => x.Share > 0m).OrderByDescending(x => x.Share).Select(x => x.Link).FirstOrDefault();
            fraction = link is null ? 0m : ProRata.Fraction(date, link.StartDate, link.EndDate);
        }

        if (link is null)
        {
            return Error.Rule("Mun.ServiceNotLinked", $"Property {property.StandNumber} has no active {serviceType} service.");
        }

        Tariff? linked = await _db.Set<Tariff>().Include(t => t.Blocks)
            .FirstOrDefaultAsync(t => t.Id == link.TariffId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (linked is null)
        {
            return Error.NotFound("Mun.TariffNotFound", $"Tariff {link.TariffId} not found.");
        }

        // The version of the linked tariff's code in force on the bill date (the linked version itself before any is).
        Tariff tariff = TariffVersions.InForce(await TariffVersions.LoadAsync(_db, linked, ct).ConfigureAwait(false), linked, date);

        // The value in force on the bill date, from the valuation roll (the register value when there is no history).
        decimal valuation = ValuationRoll.ValueAt(await ValuationHistory.LoadAsync(_db, property.Id, ct).ConfigureAwait(false), date, property.Valuation);
        decimal charge = ProRata.Apply(tariff.CalculateCharge(consumption, valuation), fraction);
        if (charge <= 0m)
        {
            return Error.Rule("Mun.NothingToBill", $"Computed charge for {serviceType} on {property.StandNumber} is zero.");
        }

        Company? company = await _db.Set<Company>()
            .FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Mun.CompanyNotFound", $"Company {companyId} not found.");
        }

        Result<TradingPostingResult> posted = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId,
            CustomerId = property.RatepayerCustomerId,
            DocumentNumber = invoiceNumber,
            DocumentDate = date,
            Currency = company.BaseCurrency,
            NetAmount = charge,
            TaxAmount = taxAmount,
            SalesAccountCode = tariff.RevenueAccountCode,
            TaxAccountCode = taxAccountCode,
            Narrative = ValuationHistory.BillNarrative(tariff.Name, property.StandNumber),
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        return new BillingResult(charge, posted.Value.OpenItemId, posted.Value.JournalNumber);
    }
}
