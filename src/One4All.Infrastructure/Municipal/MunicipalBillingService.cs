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

        List<long> propertyIds = await MunicipalCharges.PropertiesToBillAsync(_db, companyId, serviceType, date, null, ct).ConfigureAwait(false);

        // One bill per property per service per month: the number carries the billing month, and a number
        // already on the receivables ledger means that property has been billed for the month.
        Dictionary<long, string> numbers = propertyIds.ToDictionary(id => id, id => $"{prefix}-{id}-{date:yyyyMM}");
        List<string> candidates = [.. numbers.Values];
        HashSet<string> alreadyRaised = await MunicipalCharges.AlreadyRaisedAsync(_db, companyId, candidates, ct).ConfigureAwait(false);

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

            Result<BillingResult> result = await _db.AtomicAsync(() => BillServiceCoreAsync(companyId, propertyId, serviceType, 0m, invoiceNumber, date, 0m, null, ct, prorate: true, runPrefix: prefix), ct)
                .ConfigureAwait(false);
            if (result.IsFailure && result.Error.Code is "Mun.NotDueThisCycle" or "Mun.CycleBilled")
            {
                skipped++;
                continue;
            }

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

    /// <inheritdoc />
    public async Task<Result> SetBillingCycleAsync(long companyId, string tariffCode, BillingCycle cycle, int startMonth, int instalments, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(cycle) || startMonth is < 1 or > 12 || instalments < 1 || instalments > (int)cycle)
        {
            return Result.Failure(Error.Validation("Mun.Cycle", $"A {cycle} cycle starts in a month 1–12 and is billed in 1 to {(int)cycle} instalment(s)."));
        }

        string code = (tariffCode ?? string.Empty).Trim().ToUpperInvariant();
        List<Tariff> versions = await _db.Set<Tariff>().Where(t => t.CompanyId == companyId && t.Code == code).ToListAsync(ct).ConfigureAwait(false);
        if (versions.Count == 0)
        {
            return Result.Failure(Error.NotFound("Mun.TariffNotFound", $"Tariff {code} not found."));
        }

        foreach (Tariff version in versions)
        {
            version.SetCycle(cycle, startMonth, instalments);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // One unit of work: its postings and record changes commit together or not at all.
    // With prorate, the link may have ended within the bill's month and the charge covers only the days it was in place.
    private async Task<Result<BillingResult>> BillServiceCoreAsync(long companyId, long propertyId, ServiceType serviceType, decimal consumption, string invoiceNumber, DateOnly date, decimal taxAmount, string? taxAccountCode, CancellationToken ct, bool prorate,
        string? runPrefix = null)
    {
        Result<ComputedCharge> computed = await MunicipalCharges.ComputeAsync(_db, companyId, propertyId, serviceType, consumption, date, prorate, ct).ConfigureAwait(false);
        if (computed.IsFailure)
        {
            return computed.Error;
        }

        ComputedCharge bill = computed.Value;

        // In a run, a cycle charge's bill carries its period (or instalment month), so it is raised once per period.
        if (runPrefix is not null && bill.BillMonth is DateOnly billMonth)
        {
            invoiceNumber = $"{runPrefix}-{propertyId}-{billMonth:yyyyMM}";
            if ((await MunicipalCharges.AlreadyRaisedAsync(_db, companyId, [invoiceNumber], ct).ConfigureAwait(false)).Count > 0)
            {
                return Error.Rule("Mun.CycleBilled", $"{invoiceNumber} is already raised for this period.");
            }
        }

        Result<TradingPostingResult> posted = await MunicipalCharges.PostAsync(_db, _salesLedger, companyId, bill.Property.RatepayerCustomerId, invoiceNumber, date,
            bill.Charge, taxAmount, bill.Tariff.RevenueAccountCode, taxAccountCode, bill.Narrative, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        return new BillingResult(bill.Charge, posted.Value.OpenItemId, posted.Value.JournalNumber);
    }
}
