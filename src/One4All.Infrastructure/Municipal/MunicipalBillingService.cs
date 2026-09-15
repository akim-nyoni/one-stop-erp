using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.System;
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

        decimal consumption = meter.RecordReading(reading);
        _db.Set<MeterReading>().Add(new MeterReading(companyId, meterId, readingDate, reading, previous, consumption));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return consumption;
    }

    /// <inheritdoc />
    public async Task<Result<BillingResult>> BillServiceAsync(long companyId, long propertyId, ServiceType serviceType, decimal consumption, string invoiceNumber, DateOnly date, decimal taxAmount = 0m, string? taxAccountCode = null, CancellationToken ct = default)
    {
        Property? property = await _db.Set<Property>().Include(p => p.Services)
            .FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        PropertyServiceLink? link = property.Services.FirstOrDefault(s => s.ServiceType == serviceType && s.IsActive);
        if (link is null)
        {
            return Error.Rule("Mun.ServiceNotLinked", $"Property {property.StandNumber} has no active {serviceType} service.");
        }

        Tariff? tariff = await _db.Set<Tariff>().Include(t => t.Blocks)
            .FirstOrDefaultAsync(t => t.Id == link.TariffId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (tariff is null)
        {
            return Error.NotFound("Mun.TariffNotFound", $"Tariff {link.TariffId} not found.");
        }

        decimal charge = tariff.CalculateCharge(consumption, property.Valuation);
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
            Narrative = $"{tariff.Name} — stand {property.StandNumber}",
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        return new BillingResult(charge, posted.Value.OpenItemId, posted.Value.JournalNumber);
    }
}
