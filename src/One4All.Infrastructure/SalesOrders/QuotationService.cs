using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>
/// Manages sales quotations and their conversion into orders. Quotations are ledger-neutral; conversion
/// delegates to <see cref="ISalesOrderService"/> so the resulting order behaves exactly like any other.
/// </summary>
public sealed class QuotationService : IQuotationService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesOrderService _orders;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="orders">The sales-order service (conversion target).</param>
    public QuotationService(One4AllDbContext db, ISalesOrderService orders)
    {
        _db = db;
        _orders = orders;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, long customerId, string quoteNumber, DateOnly quoteDate, DateOnly validUntil, CurrencyCode currency, IReadOnlyList<QuotationLineRequest> lines, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return Error.Validation("Sop.NoQuoteLines", "A quotation needs at least one line.");
        }

        if (lines.Any(l => l.Quantity <= 0m))
        {
            return Error.Rule("Sop.QuoteLineQty", "Every quotation line quantity must be positive.");
        }

        if (validUntil < quoteDate)
        {
            return Error.Rule("Sop.QuoteValidity", "The valid-until date cannot be before the quote date.");
        }

        bool customerExists = await _db.Customers.AnyAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!customerExists)
        {
            return Error.NotFound("Sop.CustomerNotFound", $"Customer {customerId} not found.");
        }

        Quotation quote = new(companyId, customerId, quoteNumber, quoteDate, validUntil, currency);
        foreach (QuotationLineRequest line in lines)
        {
            quote.AddLine(line.ItemId, line.Description, line.Quantity, line.UnitPrice, line.TaxCode, line.RevenueAccountCode);
        }

        _db.Set<Quotation>().Add(quote);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return quote.Id;
    }

    /// <inheritdoc />
    public Task<Result> SendAsync(long companyId, long quotationId, CancellationToken ct = default)
        => TransitionAsync(companyId, quotationId, q => q.Status == QuotationStatus.Draft, q => q.Send(), "Only a draft quotation can be sent.", ct);

    /// <inheritdoc />
    public Task<Result> AcceptAsync(long companyId, long quotationId, CancellationToken ct = default)
        => TransitionAsync(companyId, quotationId, q => q.Status is QuotationStatus.Draft or QuotationStatus.Sent, q => q.Accept(), "Only a draft or sent quotation can be accepted.", ct);

    /// <inheritdoc />
    public Task<Result> DeclineAsync(long companyId, long quotationId, CancellationToken ct = default)
        => TransitionAsync(companyId, quotationId, q => q.Status is QuotationStatus.Draft or QuotationStatus.Sent, q => q.Decline(), "Only a draft or sent quotation can be declined.", ct);

    /// <inheritdoc />
    public async Task<Result<long>> ConvertToOrderAsync(long companyId, long quotationId, string orderNumber, DateOnly orderDate, CancellationToken ct = default)
    {
        Quotation? quote = await _db.Set<Quotation>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.CompanyId == companyId, ct).ConfigureAwait(false);
        if (quote is null)
        {
            return Error.NotFound("Sop.QuoteNotFound", $"Quotation {quotationId} not found.");
        }

        if (quote.Status != QuotationStatus.Accepted)
        {
            return Error.Rule("Sop.QuoteNotAccepted", $"Only an accepted quotation can be converted; {quote.QuoteNumber} is {quote.Status}.");
        }

        Result<SalesOrderResult> order = await _orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId,
            CustomerId = quote.CustomerId,
            OrderNumber = orderNumber,
            OrderDate = orderDate,
            Currency = quote.Currency,
            Lines = quote.Lines
                .Select(l => new SalesOrderLineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.TaxCode, l.RevenueAccountCode))
                .ToList(),
        }, ct).ConfigureAwait(false);

        if (order.IsFailure)
        {
            return order.Error;
        }

        quote.MarkConverted(order.Value.SalesOrderId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Value.SalesOrderId;
    }

    private async Task<Result> TransitionAsync(long companyId, long quotationId, Func<Quotation, bool> allowed, Action<Quotation> apply, string message, CancellationToken ct)
    {
        Quotation? quote = await _db.Set<Quotation>().FirstOrDefaultAsync(q => q.Id == quotationId && q.CompanyId == companyId, ct).ConfigureAwait(false);
        if (quote is null)
        {
            return Result.Failure(Error.NotFound("Sop.QuoteNotFound", $"Quotation {quotationId} not found."));
        }

        if (!allowed(quote))
        {
            return Result.Failure(Error.Rule("Sop.QuoteState", message));
        }

        apply(quote);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
