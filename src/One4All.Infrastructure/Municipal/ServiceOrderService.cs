using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Disconnection and reconnection orders over the ratepayers' AR balances.</summary>
public sealed class ServiceOrderService : IServiceOrderService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The sales ledger, which invoices reconnection fees.</param>
    /// <param name="numbers">The number sequences.</param>
    public ServiceOrderService(One4AllDbContext db, ISalesLedgerService ar, INumberSequenceService numbers)
    {
        _db = db;
        _ar = ar;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DisconnectionCandidate>> CandidatesAsync(long companyId, ServiceType serviceType, DateOnly asAtDate, decimal threshold, CancellationToken ct = default)
    {
        var properties = await _db.Properties.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Services.Any(s => s.ServiceType == serviceType && s.IsActive))
            .Select(p => new { p.Id, p.StandNumber, p.RatepayerCustomerId })
            .ToListAsync(ct).ConfigureAwait(false);
        List<DisconnectionCandidate> candidates = [];
        foreach (var property in properties)
        {
            (decimal arrears, DateOnly? oldest) = await ArrearsAsync(companyId, property.RatepayerCustomerId, asAtDate, ct).ConfigureAwait(false);
            if (arrears >= threshold && oldest is DateOnly due)
            {
                candidates.Add(new DisconnectionCandidate(property.Id, property.StandNumber, arrears, due));
            }
        }

        return [.. candidates.OrderByDescending(c => c.Arrears).ThenBy(c => c.StandNumber, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public Task<Result<string>> RaiseAsync(long companyId, long propertyId, ServiceType serviceType, ServiceOrderKind kind, DateOnly date, decimal threshold, CancellationToken ct = default)
        => _db.AtomicAsync(() => RaiseCoreAsync(companyId, propertyId, serviceType, kind, date, threshold, ct), ct);

    private async Task<Result<string>> RaiseCoreAsync(long companyId, long propertyId, ServiceType serviceType, ServiceOrderKind kind, DateOnly date, decimal threshold, CancellationToken ct)
    {
        Property? property = await _db.Properties.Include(p => p.Services).FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        PropertyServiceLink? link = property?.Services.FirstOrDefault(s => s.ServiceType == serviceType);
        if (property is null || link is null)
        {
            return Error.NotFound("Mun.ServiceNotFound", $"Property {propertyId} has no {serviceType} service.");
        }

        if (await _db.Set<ServiceOrder>().AnyAsync(o => o.PropertyId == propertyId && o.ServiceType == serviceType && o.Status == ServiceOrderStatus.Open, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Mun.OrderOpen", $"Stand {property.StandNumber} already has an open {serviceType} order.");
        }

        (decimal arrears, _) = await ArrearsAsync(companyId, property.RatepayerCustomerId, date, ct).ConfigureAwait(false);
        if (kind == ServiceOrderKind.Disconnect && (!link.IsActive || arrears < threshold))
        {
            return Error.Rule("Mun.NotForDisconnection", $"Stand {property.StandNumber}'s {serviceType} is not connected or its arrears of {arrears:0.00} are under {threshold:0.00}.");
        }

        if (kind == ServiceOrderKind.Reconnect && (link.IsActive || arrears >= threshold))
        {
            return Error.Rule("Mun.NotForReconnection", $"Stand {property.StandNumber}'s {serviceType} is connected or its arrears of {arrears:0.00} are still at or over {threshold:0.00}.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, kind == ServiceOrderKind.Disconnect ? "DO" : "RO", date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<ServiceOrder>().Add(new ServiceOrder(companyId, number.Value, propertyId, serviceType, kind, date, arrears));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result> ExecuteAsync(long companyId, string orderNumber, DateOnly date, ReconnectionFee? fee, CancellationToken ct = default)
        => _db.AtomicAsync(() => ExecuteCoreAsync(companyId, orderNumber, date, fee, ct), ct);

    // One unit: the service change, the fee invoice and the order are kept together.
    private async Task<Result> ExecuteCoreAsync(long companyId, string orderNumber, DateOnly date, ReconnectionFee? fee, CancellationToken ct)
    {
        ServiceOrder? order = await _db.Set<ServiceOrder>().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.OrderNumber == orderNumber, ct).ConfigureAwait(false);
        if (order is null || order.Status != ServiceOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Mun.OrderNotOpen", $"Order {orderNumber} is not open."));
        }

        Property property = await _db.Properties.Include(p => p.Services).FirstAsync(p => p.Id == order.PropertyId, ct).ConfigureAwait(false);
        PropertyServiceLink link = property.Services.First(s => s.ServiceType == order.ServiceType);
        string? feeInvoice = null;
        if (order.Kind == ServiceOrderKind.Disconnect)
        {
            link.IsActive = false;
        }
        else
        {
            link.IsActive = true;
            if (fee is { Amount: > 0m })
            {
                Customer ratepayer = await _db.Customers.FirstAsync(c => c.Id == property.RatepayerCustomerId, ct).ConfigureAwait(false);
                feeInvoice = $"{order.OrderNumber}-FEE";
                Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
                {
                    CompanyId = companyId, CustomerId = ratepayer.Id, DocumentNumber = feeInvoice, DocumentDate = date, Currency = ratepayer.Currency,
                    NetAmount = fee.Amount, SalesAccountCode = fee.RevenueAccountCode, Narrative = $"Reconnection fee, stand {property.StandNumber}",
                }, ct).ConfigureAwait(false);
                if (invoiced.IsFailure)
                {
                    return Result.Failure(invoiced.Error);
                }
            }
        }

        order.Execute(date, feeInvoice);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(long companyId, string orderNumber, CancellationToken ct = default)
    {
        ServiceOrder? order = await _db.Set<ServiceOrder>().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.OrderNumber == orderNumber, ct).ConfigureAwait(false);
        if (order is null || order.Status != ServiceOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Mun.OrderNotOpen", $"Order {orderNumber} is not open."));
        }

        order.Cancel();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // Overdue debts less unallocated credits (which settle the oldest debts first), and the oldest unpaid due date.
    private async Task<(decimal Arrears, DateOnly? OldestDue)> ArrearsAsync(long companyId, long customerId, DateOnly asAtDate, CancellationToken ct)
    {
        var items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.OutstandingBase > 0m)
            .Select(i => new { i.Sign, i.DueDate, i.OutstandingBase })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal overdue = items.Where(i => i.Sign == LedgerSign.Debit && i.DueDate < asAtDate).Sum(i => i.OutstandingBase);
        decimal credits = items.Where(i => i.Sign == LedgerSign.Credit).Sum(i => i.OutstandingBase);
        DateOnly? oldest = items.Where(i => i.Sign == LedgerSign.Debit && i.DueDate < asAtDate).Select(i => (DateOnly?)i.DueDate).Min();
        return (Math.Max(0m, overdue - credits), oldest);
    }
}
