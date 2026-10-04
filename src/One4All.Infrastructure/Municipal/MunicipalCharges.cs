using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>A service charge worked out for a property, ready to post.</summary>
/// <param name="Property">The property.</param>
/// <param name="Tariff">The tariff version in force on the bill date.</param>
/// <param name="Charge">The charge.</param>
internal sealed record ComputedCharge(Property Property, Tariff Tariff, decimal Charge)
{
    /// <summary>The narrative the bill carries.</summary>
    public string Narrative => ValuationHistory.BillNarrative(Tariff.Name, Property.StandNumber);
}

/// <summary>
/// Works out and posts municipal service charges, shared by single bills, the fixed-charge run and the reviewed billing
/// run: the linked tariff's version in force on the bill date, the value in force from the valuation roll and, for runs,
/// the share of the month the service was in place.
/// </summary>
internal static class MunicipalCharges
{
    /// <summary>The properties with the service in place for at least part of the month, in id order.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The council.</param>
    /// <param name="serviceType">The service.</param>
    /// <param name="date">Any day in the billing month.</param>
    /// <param name="ward">Only this ward, if given.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The property ids.</returns>
    public static Task<List<long>> PropertiesToBillAsync(One4AllDbContext db, long companyId, ServiceType serviceType, DateOnly date, string? ward, CancellationToken ct)
    {
        // Connected by the month end and either running (not cut off) or ended within the month.
        (DateOnly monthStart, DateOnly monthEnd) = ProRata.Month(date);
        string? onlyWard = string.IsNullOrWhiteSpace(ward) ? null : ward.Trim();
        return db.Set<Property>()
            .Where(p => p.CompanyId == companyId && (onlyWard == null || p.Ward == onlyWard) && p.Services.Any(s => s.ServiceType == serviceType && s.StartDate <= monthEnd
                && ((s.IsActive && (s.EndDate == null || s.EndDate >= monthStart)) || (s.EndDate != null && s.EndDate >= monthStart))))
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    /// <summary>Works out a property's charge for a service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="serviceType">The service.</param>
    /// <param name="consumption">The consumption billed (0 for fixed charges).</param>
    /// <param name="date">The bill date.</param>
    /// <param name="prorate">Whether to bill only the share of the month the service was in place.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The charge, or why there is none.</returns>
    public static async Task<Result<ComputedCharge>> ComputeAsync(One4AllDbContext db, long companyId, long propertyId, ServiceType serviceType, decimal consumption, DateOnly date, bool prorate, CancellationToken ct)
    {
        Property? property = await db.Set<Property>().Include(p => p.Services)
            .FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        // With prorate, the link may have ended within the bill's month and the charge covers only the days it was in place.
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

        Tariff? linked = await db.Set<Tariff>().Include(t => t.Blocks).Include(t => t.Seasons)
            .FirstOrDefaultAsync(t => t.Id == link.TariffId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (linked is null)
        {
            return Error.NotFound("Mun.TariffNotFound", $"Tariff {link.TariffId} not found.");
        }

        // The version of the linked tariff's code in force on the bill date (the linked version itself before any is).
        Tariff tariff = TariffVersions.InForce(await TariffVersions.LoadAsync(db, linked, ct).ConfigureAwait(false), linked, date);

        // The value in force on the bill date, from the valuation roll (the register value when there is no history).
        decimal valuation = ValuationRoll.ValueAt(await ValuationHistory.LoadAsync(db, property.Id, ct).ConfigureAwait(false), date, property.Valuation);
        decimal charge = ProRata.Apply(tariff.CalculateCharge(consumption, valuation, property.SizeSquareMetres, date.Month), fraction);
        if (charge <= 0m)
        {
            return Error.Rule("Mun.NothingToBill", $"Computed charge for {serviceType} on {property.StandNumber} is zero.");
        }

        return new ComputedCharge(property, tariff, charge);
    }

    /// <summary>Raises a bill on the ratepayer's account through the receivables ledger.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The receivables ledger.</param>
    /// <param name="companyId">The council.</param>
    /// <param name="ratepayerCustomerId">The ratepayer billed.</param>
    /// <param name="invoiceNumber">The bill number.</param>
    /// <param name="date">The bill date.</param>
    /// <param name="charge">The charge before tax.</param>
    /// <param name="taxAmount">Any tax.</param>
    /// <param name="revenueAccountCode">The revenue account credited.</param>
    /// <param name="taxAccountCode">The tax account credited, when there is tax.</param>
    /// <param name="narrative">The bill narrative.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting, or why it failed.</returns>
    public static async Task<Result<TradingPostingResult>> PostAsync(One4AllDbContext db, ISalesLedgerService ar, long companyId, long ratepayerCustomerId, string invoiceNumber, DateOnly date,
        decimal charge, decimal taxAmount, string revenueAccountCode, string? taxAccountCode, string narrative, CancellationToken ct)
    {
        Company? company = await db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Mun.CompanyNotFound", $"Company {companyId} not found.");
        }

        return await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId,
            CustomerId = ratepayerCustomerId,
            DocumentNumber = invoiceNumber,
            DocumentDate = date,
            Currency = company.BaseCurrency,
            NetAmount = charge,
            TaxAmount = taxAmount,
            SalesAccountCode = revenueAccountCode,
            TaxAccountCode = taxAccountCode,
            Narrative = narrative,
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The bill numbers among the candidates already on the receivables ledger.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The council.</param>
    /// <param name="candidates">The bill numbers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Those already raised.</returns>
    public static async Task<HashSet<string>> AlreadyRaisedAsync(One4AllDbContext db, long companyId, IReadOnlyCollection<string> candidates, CancellationToken ct)
        => (await db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && candidates.Contains(i.DocumentNumber))
            .Select(i => i.DocumentNumber)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
}
