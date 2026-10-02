using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Procurement;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>Builds purchase order documents from the order, its supplier, the items and the buying company.</summary>
public sealed class PurchaseOrderDocumentService : IPurchaseOrderDocumentService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PurchaseOrderDocumentService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<PurchaseOrderDocument>> BuildAsync(long companyId, long purchaseOrderId, CancellationToken ct = default)
    {
        PurchaseOrder? order = await _db.Set<PurchaseOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Pop.OrderNotFound", $"Purchase order {purchaseOrderId} not found.");
        }

        if (order.Status == PurchaseOrderStatus.Cancelled)
        {
            return Error.Rule("Pop.Cancelled", $"Purchase order {order.OrderNumber} is cancelled.");
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == order.SupplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);

        List<long> itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
        Dictionary<long, string> codes = await _db.Items
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Code, ct).ConfigureAwait(false);

        List<PurchaseOrderDocumentLine> lines = order.Lines
            .Select(l => new PurchaseOrderDocumentLine(codes.GetValueOrDefault(l.ItemId, string.Empty), l.Description, l.OrderedQuantity, l.UnitPrice))
            .ToList();

        return new PurchaseOrderDocument(
            company.Name,
            string.IsNullOrWhiteSpace(company.TaxNumber) ? null : company.TaxNumber,
            supplier?.Code ?? string.Empty,
            supplier?.Name ?? string.Empty,
            order.OrderNumber,
            order.OrderDate,
            order.Currency.ToString(),
            supplier?.PaymentTermsDays ?? 30,
            order.Status.ToString(),
            lines);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderTextAsync(long companyId, long purchaseOrderId, int width = PurchaseOrderFormatter.DefaultWidth, CancellationToken ct = default)
    {
        Result<PurchaseOrderDocument> document = await BuildAsync(companyId, purchaseOrderId, ct).ConfigureAwait(false);
        return document.IsFailure ? document.Error : PurchaseOrderFormatter.ToPlainText(document.Value, width);
    }
}
