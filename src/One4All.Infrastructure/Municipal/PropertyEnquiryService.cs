using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>The consumer enquiry desk: a read-only view assembled from the register, meters and the ratepayer's account.</summary>
public sealed class PropertyEnquiryService : IPropertyEnquiryService
{
    private readonly One4AllDbContext _db;
    private readonly IOwnershipService _owners;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="owners">The owner register.</param>
    public PropertyEnquiryService(One4AllDbContext db, IOwnershipService owners)
    {
        _db = db;
        _owners = owners;
    }

    /// <inheritdoc />
    public async Task<Result<PropertyEnquiry>> EnquireAsync(long companyId, long propertyId, DateOnly asAtDate, int recentTransactions = 10, CancellationToken ct = default)
    {
        Property? property = await _db.Properties.AsNoTracking().Include(p => p.Services)
            .FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        Customer ratepayer = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == property.RatepayerCustomerId, ct).ConfigureAwait(false);
        decimal valuation = ValuationRoll.ValueAt(await ValuationHistory.LoadAsync(_db, property.Id, ct).ConfigureAwait(false), asAtDate, property.Valuation);
        IReadOnlyList<OwnerEntry> owners = await _owners.OwnersAsync(companyId, propertyId, asAtDate, ct).ConfigureAwait(false);

        // Services with their tariff codes and meters.
        List<long> tariffIds = [.. property.Services.Select(s => s.TariffId).Distinct()];
        Dictionary<long, string> tariffCodes = await _db.Tariffs.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(t => tariffIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Code, ct).ConfigureAwait(false);
        List<long> meterIds = [.. property.Services.Where(s => s.MeterId != null).Select(s => s.MeterId!.Value).Distinct()];
        Dictionary<long, Meter> meters = await _db.Set<Meter>().AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(m => meterIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        List<EnquiryService> services = [.. property.Services.OrderBy(s => s.ServiceType).ThenBy(s => s.StartDate).Select(s =>
        {
            Meter? meter = s.MeterId is long id ? meters.GetValueOrDefault(id) : null;
            return new EnquiryService(s.ServiceType, tariffCodes.GetValueOrDefault(s.TariffId, string.Empty), s.IsActive && (s.EndDate == null || s.EndDate >= asAtDate),
                s.StartDate, s.EndDate, meter?.Serial, meter?.LastReading, meter?.LastReadingDate);
        })];

        // The account: balance, overdue less unallocated credits, and the latest transactions.
        var items = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == ratepayer.Id && i.DocumentDate <= asAtDate)
            .Select(i => new { i.Id, i.DocumentDate, i.DueDate, i.DocumentNumber, i.ItemType, i.Sign, i.OriginalBase, i.OutstandingBase })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal balance = items.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase);
        decimal pastDue = items.Where(i => i.Sign == LedgerSign.Debit && i.DueDate < asAtDate).Sum(i => i.OutstandingBase);
        decimal credits = items.Where(i => i.Sign == LedgerSign.Credit).Sum(i => i.OutstandingBase);
        List<EnquiryTransaction> recent = [.. items.OrderByDescending(i => i.DocumentDate).ThenByDescending(i => i.Id).Take(Math.Max(0, recentTransactions))
            .Select(i => new EnquiryTransaction(i.DocumentDate, i.DocumentNumber, i.ItemType, i.Sign == LedgerSign.Debit ? i.OriginalBase : -i.OriginalBase,
                i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase))];

        decimal? subsidy = await _db.Set<IndigentRegistration>().AsNoTracking()
            .Where(r => r.PropertyId == propertyId && r.Status == IndigentStatus.Active)
            .Select(r => (decimal?)r.SubsidyPercent).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        EnquiryArrangement? arrangement = await _db.Set<PaymentArrangement>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.CustomerId == ratepayer.Id && a.Status == ArrangementStatus.Active)
            .OrderByDescending(a => a.StartDate)
            .Select(a => new EnquiryArrangement(a.TotalAmount, a.InstalmentAmount, a.StartDate)).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        List<EnquiryOrder> orders = await _db.Set<ServiceOrder>().AsNoTracking()
            .Where(o => o.PropertyId == propertyId && o.Status == ServiceOrderStatus.Open)
            .OrderBy(o => o.RaisedOn)
            .Select(o => new EnquiryOrder(o.OrderNumber, o.Kind, o.ServiceType, o.RaisedOn)).ToListAsync(ct).ConfigureAwait(false);
        List<EnquiryLicence> licences = await _db.Set<CouncilLicence>().AsNoTracking()
            .Where(l => l.CompanyId == companyId && (l.PropertyId == propertyId || l.HolderCustomerId == ratepayer.Id))
            .OrderBy(l => l.ExpiresOn)
            .Select(l => new EnquiryLicence(l.LicenceNumber, l.Kind, l.ExpiresOn, l.Status)).ToListAsync(ct).ConfigureAwait(false);
        DateOnly? clearance = await _db.Set<ClearanceCertificate>().AsNoTracking()
            .Where(c => c.PropertyId == propertyId && c.IssuedOn <= asAtDate && c.ValidUntil >= asAtDate)
            .OrderByDescending(c => c.ValidUntil)
            .Select(c => (DateOnly?)c.ValidUntil).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return new PropertyEnquiry(property.StandNumber, property.Ward, valuation, ratepayer.Code, ratepayer.Name, owners, services,
            balance, Math.Max(0m, pastDue - credits), recent, subsidy, arrangement, orders, licences, clearance);
    }
}
