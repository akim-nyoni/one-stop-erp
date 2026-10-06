using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Delivery charges by zone, weight and volume (brief §4.13).</summary>
public sealed class DeliveryChargeService : IDeliveryChargeService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _sales;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="sales">The sales ledger the charge is invoiced through.</param>
    public DeliveryChargeService(One4AllDbContext db, ISalesLedgerService sales)
    {
        _db = db;
        _sales = sales;
    }

    /// <inheritdoc />
    public async Task<Result<long>> SaveRateAsync(long companyId, string zone, decimal baseCharge, decimal perKg, decimal perCubicMetre, decimal? freeAboveValue, CancellationToken ct = default)
    {
        string key = (zone ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0 || baseCharge < 0m || perKg < 0m || perCubicMetre < 0m || freeAboveValue < 0m)
        {
            return Error.Validation("Sop.ZoneRate", "A zone rate needs a zone and no negative charges.");
        }

        DeliveryZoneRate? rate = await _db.DeliveryZoneRates.FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Zone == key, ct).ConfigureAwait(false);
        if (rate is null)
        {
            rate = new DeliveryZoneRate(companyId, key);
            _db.DeliveryZoneRates.Add(rate);
        }

        rate.BaseCharge = baseCharge;
        rate.PerKg = perKg;
        rate.PerCubicMetre = perCubicMetre;
        rate.FreeAboveValue = freeAboveValue;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rate.Id;
    }

    /// <inheritdoc />
    public async Task<Result<DeliveryChargeQuote>> QuoteAsync(long companyId, string deliveryReference, CancellationToken ct = default)
    {
        Result<(SalesDelivery Delivery, Customer Customer, DeliveryChargeQuote Quote)> worked = await WorkOutAsync(companyId, deliveryReference, ct).ConfigureAwait(false);
        return worked.IsFailure ? worked.Error : worked.Value.Quote;
    }

    /// <inheritdoc />
    public Task<Result<DeliveryChargeQuote>> InvoiceAsync(long companyId, string deliveryReference, string invoiceNumber, DateOnly date, string revenueAccountCode, string? taxCode, string? taxAccountCode,
        CancellationToken ct = default)
        => _db.AtomicAsync(() => InvoiceCoreAsync(companyId, deliveryReference, invoiceNumber, date, revenueAccountCode, taxCode, taxAccountCode, ct), ct);

    private async Task<Result<DeliveryChargeQuote>> InvoiceCoreAsync(long companyId, string deliveryReference, string invoiceNumber, DateOnly date, string revenueAccountCode, string? taxCode,
        string? taxAccountCode, CancellationToken ct)
    {
        Result<(SalesDelivery Delivery, Customer Customer, DeliveryChargeQuote Quote)> worked = await WorkOutAsync(companyId, deliveryReference, ct).ConfigureAwait(false);
        if (worked.IsFailure)
        {
            return worked.Error;
        }

        (SalesDelivery delivery, Customer customer, DeliveryChargeQuote quote) = worked.Value;
        if (delivery.ChargeInvoiceNumber is not null)
        {
            return Error.Conflict("Sop.DeliveryCharged", $"Delivery {delivery.Reference} was already charged on {delivery.ChargeInvoiceNumber}.");
        }

        if (quote.Charge == 0m)
        {
            return Error.Rule("Sop.FreeDelivery", $"Delivery {delivery.Reference} is free (goods value {quote.GoodsValue:N2}).");
        }

        decimal tax = 0m;
        if (!string.IsNullOrWhiteSpace(taxCode))
        {
            TaxCode? code = await _db.TaxCodes.FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == taxCode.Trim().ToUpper(), ct).ConfigureAwait(false);
            if (code is null || string.IsNullOrWhiteSpace(taxAccountCode))
            {
                return Error.Validation("Sop.DeliveryTax", "A taxable delivery charge needs a known tax code and the output tax account.");
            }

            tax = decimal.Round(quote.Charge * code.RateOn(date) / 100m, 2, MidpointRounding.AwayFromZero);
        }

        Result<TradingPostingResult> posted = await _sales.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = invoiceNumber, DocumentDate = date, Currency = customer.Currency, NetAmount = quote.Charge, TaxAmount = tax,
            SalesAccountCode = revenueAccountCode, TaxAccountCode = tax == 0m ? null : taxAccountCode, TaxCode = tax == 0m ? null : taxCode,
            Narrative = $"Delivery {delivery.Reference} to zone {quote.Zone}: {quote.WeightKg:0.##} kg, {quote.VolumeM3:0.###} m3",
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        delivery.RecordCharge(quote.Charge, invoiceNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return quote;
    }

    private async Task<Result<(SalesDelivery Delivery, Customer Customer, DeliveryChargeQuote Quote)>> WorkOutAsync(long companyId, string deliveryReference, CancellationToken ct)
    {
        string reference = (deliveryReference ?? string.Empty).Trim();
        SalesDelivery? delivery = await _db.SalesDeliveries.Include(d => d.Lines).FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Reference == reference, ct).ConfigureAwait(false);
        if (delivery is null)
        {
            return Error.NotFound("Sop.DeliveryNotFound", $"No delivery '{reference}'.");
        }

        SalesOrder order = await _db.SalesOrders.Include(o => o.Lines).FirstAsync(o => o.Id == delivery.SalesOrderId, ct).ConfigureAwait(false);
        Customer customer = await _db.Customers.FirstAsync(c => c.Id == order.CustomerId, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(customer.DeliveryZone))
        {
            return Error.Rule("Sop.NoZone", $"Customer {customer.Code} has no delivery zone.");
        }

        string zone = customer.DeliveryZone.Trim().ToUpperInvariant();
        DeliveryZoneRate? rate = await _db.DeliveryZoneRates.AsNoTracking().FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Zone == zone, ct).ConfigureAwait(false);
        if (rate is null)
        {
            return Error.Rule("Sop.NoZoneRate", $"Zone {zone} has no delivery rate.");
        }

        List<long> itemIds = [.. delivery.Lines.Select(l => l.ItemId).Distinct()];
        Dictionary<long, Item> items = await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        decimal weight = delivery.Lines.Sum(l => l.Quantity * (items.GetValueOrDefault(l.ItemId)?.UnitWeightKg ?? 0m));
        decimal volume = delivery.Lines.Sum(l => l.Quantity * (items.GetValueOrDefault(l.ItemId)?.UnitVolumeM3 ?? 0m));
        decimal value = delivery.Lines.Sum(l => l.Quantity * (order.Lines.FirstOrDefault(o => o.Id == l.SalesOrderLineId)?.UnitPrice ?? 0m));
        return (delivery, customer, new DeliveryChargeQuote(zone, weight, volume, value, rate.ChargeFor(weight, volume, value)));
    }
}
