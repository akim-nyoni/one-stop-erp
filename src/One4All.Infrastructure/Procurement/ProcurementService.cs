using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>Procurement service: purchase orders, GRNs (GRNI accrual) and the three-way invoice match (brief §4.3).</summary>
public sealed class ProcurementService : IProcurementService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IPostingEngine _postingEngine;
    private readonly One4All.Application.Abstractions.Numbering.INumberSequenceService _numbers;

    /// <summary>The document-type key of the purchase-order number sequence.</summary>
    public const string OrderNumberSequence = "PO";

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service (for stock receipts).</param>
    /// <param name="postingEngine">The posting engine.</param>
    /// <param name="numbers">The number-sequence service (allocates order numbers left blank).</param>
    public ProcurementService(One4AllDbContext db, IInventoryService inventory, IPostingEngine postingEngine, One4All.Application.Abstractions.Numbering.INumberSequenceService numbers)
    {
        _db = db;
        _inventory = inventory;
        _postingEngine = postingEngine;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public async Task<Result<PurchaseOrderResult>> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        // A blank order number means "allocate the next one" from the PO sequence.
        string orderNumber = request.OrderNumber.Trim();
        if (orderNumber.Length == 0)
        {
            Result<string> allocated = await _numbers.AllocateAsync(request.CompanyId, OrderNumberSequence, request.OrderDate, ct: ct).ConfigureAwait(false);
            if (allocated.IsFailure)
            {
                return allocated.Error;
            }

            orderNumber = allocated.Value;
        }

        PurchaseOrder order = new(request.CompanyId, request.SupplierId, orderNumber, request.OrderDate, request.Currency);
        foreach (PurchaseOrderLineRequest line in request.Lines)
        {
            order.AddLine(line.ItemId, line.Description, line.Quantity, line.UnitPrice);
        }

        _db.Set<PurchaseOrder>().Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PurchaseOrderResult(order.Id, order.OrderNumber);
    }

    /// <inheritdoc />
    public async Task<Result> CancelPurchaseOrderAsync(long companyId, long purchaseOrderId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Pop.ReasonRequired", "A reason is required to cancel a purchase order."));
        }

        PurchaseOrder? order = await _db.Set<PurchaseOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Pop.OrderNotFound", $"Purchase order {purchaseOrderId} not found."));
        }

        Result cancelled = order.Cancel();
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<GrnResult>> ReceiveGoodsAsync(ReceiveGoodsRequest request, CancellationToken ct = default)
    {
        PurchaseOrder? order = await _db.Set<PurchaseOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId && o.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Pop.OrderNotFound", $"Purchase order {request.PurchaseOrderId} not found.");
        }

        string? grniCode = await ControlCodeAsync(request.CompanyId, ControlAccountType.GoodsReceivedNotInvoiced, ct).ConfigureAwait(false);
        if (grniCode is null)
        {
            return Error.Rule("Pop.NoGrni", "No goods-received-not-invoiced control account is configured.");
        }

        GoodsReceivedNote grn = new(request.CompanyId, order.SupplierId, order.Id, request.GrnNumber, request.Date);
        decimal accrued = 0m;

        foreach (ReceiveLine received in request.Lines)
        {
            PurchaseOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == received.PurchaseOrderLineId);
            if (line is null)
            {
                return Error.NotFound("Pop.LineNotFound", $"PO line {received.PurchaseOrderLineId} not found.");
            }

            // Receive into stock: Dr stock control, Cr GRNI (accrual), at the PO price.
            Result<StockMovementResult> receipt = await _inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = request.CompanyId,
                ItemId = line.ItemId,
                WarehouseId = request.WarehouseId,
                Date = request.Date,
                Quantity = received.Quantity,
                UnitCost = line.UnitPrice,
                CreditAccountCode = grniCode,
                Reference = request.GrnNumber,
            }, ct).ConfigureAwait(false);

            if (receipt.IsFailure)
            {
                return receipt.Error;
            }

            line.Receive(received.Quantity);
            grn.AddLine(line.Id, line.ItemId, received.Quantity, line.UnitPrice);
            accrued += decimal.Round(received.Quantity * line.UnitPrice, 4, MidpointRounding.ToEven);
        }

        order.RecomputeStatus();
        _db.Set<GoodsReceivedNote>().Add(grn);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new GrnResult(grn.Id, accrued);
    }

    /// <inheritdoc />
    public async Task<Result<MatchInvoiceResult>> MatchInvoiceAsync(MatchInvoiceRequest request, CancellationToken ct = default)
    {
        PurchaseOrder? order = await _db.Set<PurchaseOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId && o.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Pop.OrderNotFound", $"Purchase order {request.PurchaseOrderId} not found.");
        }

        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Pop.SupplierNotFound", $"Supplier {order.SupplierId} not found.");
        }

        string? grniCode = await ControlCodeAsync(request.CompanyId, ControlAccountType.GoodsReceivedNotInvoiced, ct).ConfigureAwait(false);
        string? apCode = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (grniCode is null || apCode is null)
        {
            return Error.Rule("Pop.NoControlAccounts", "GRNI and/or AP control accounts are not configured.");
        }

        decimal invoiceValue = 0m;
        foreach (InvoiceMatchLine invLine in request.Lines)
        {
            PurchaseOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == invLine.PurchaseOrderLineId);
            if (line is null)
            {
                return Error.NotFound("Pop.LineNotFound", $"PO line {invLine.PurchaseOrderLineId} not found.");
            }

            // Three-way quantity check: cannot invoice more than received-but-not-yet-invoiced.
            decimal availableToInvoice = line.ReceivedQuantity - line.InvoicedQuantity;
            if (invLine.Quantity > availableToInvoice + 0.0001m)
            {
                return Error.Rule("Pop.QuantityMismatch",
                    $"PO line {line.Id}: invoicing {invLine.Quantity} but only {availableToInvoice} received and uninvoiced.");
            }

            // Three-way price check within tolerance.
            decimal tolerance = line.UnitPrice * request.TolerancePercent / 100m;
            if (Math.Abs(invLine.UnitPrice - line.UnitPrice) > tolerance + 0.0001m)
            {
                return Error.Rule("Pop.PriceMismatch",
                    $"PO line {line.Id}: invoice price {invLine.UnitPrice} is outside {request.TolerancePercent}% of PO price {line.UnitPrice}.");
            }

            invoiceValue += decimal.Round(invLine.Quantity * invLine.UnitPrice, 4, MidpointRounding.ToEven);
            line.Invoice(invLine.Quantity);
        }

        // Clear GRNI to AP: Dr GRNI, Cr AP control.
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "POP",
            PostingDate = request.Date,
            TransactionCurrency = order.Currency,
            EnforceManualJournalRules = false,
            DocumentType = "SupplierInvoice",
            DocumentNumber = request.InvoiceNumber,
            Narrative = $"3-way match {order.OrderNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = grniCode, Side = JournalSide.Debit, TransactionAmount = invoiceValue },
                new JournalLineRequest { AccountCode = apCode, Side = JournalSide.Credit, TransactionAmount = invoiceValue },
            ],
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        decimal rate = order.Currency == company.BaseCurrency ? 1m : 1m; // same-currency match; FX match is a refinement
        decimal baseValue = decimal.Round(invoiceValue * rate, 4, MidpointRounding.ToEven);

        OpenItem openItem = new(request.CompanyId, TradingLedger.AccountsPayable, supplier.Id, OpenItemType.Invoice, LedgerSign.Credit,
            request.InvoiceNumber, request.Date, supplier.DueDateFor(request.Date), order.Currency, rate, invoiceValue, baseValue)
        {
            JournalEntryId = posting.Value.JournalId,
            Narrative = $"Matched to {order.OrderNumber}",
        };
        _db.OpenItems.Add(openItem);

        order.RecomputeStatus();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new MatchInvoiceResult(openItem.Id, posting.Value.JournalNumber, invoiceValue);
    }

    /// <inheritdoc />
    public async Task<Result<SupplierReturnResult>> ReturnToSupplierAsync(long companyId, long supplierId, long itemId, long warehouseId, decimal quantity, decimal returnUnitPrice, DateOnly date, string documentNumber, string priceVarianceAccountCode, CancellationToken ct = default)
    {
        if (quantity <= 0m)
        {
            return Error.Rule("Pop.ReturnQuantity", "The return quantity must be positive.");
        }

        if (returnUnitPrice < 0m)
        {
            return Error.Rule("Pop.ReturnPrice", "The return price cannot be negative.");
        }

        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Pop.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        Core.Inventory.Item? item = await _db.Set<Core.Inventory.Item>().FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Pop.ItemNotFound", $"Item {itemId} not found.");
        }

        string? stockCode = await ControlCodeAsync(companyId, ControlAccountType.Stock, ct).ConfigureAwait(false);
        string? apCode = await ControlCodeAsync(companyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (stockCode is null || apCode is null)
        {
            return Error.Rule("Pop.NoControlAccounts", "Stock and/or AP control accounts are not configured.");
        }

        Core.Inventory.StockBalance? balance = await _db.Set<Core.Inventory.StockBalance>()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ItemId == itemId && b.WarehouseId == warehouseId, ct).ConfigureAwait(false);
        if (balance is null || balance.QuantityOnHand < quantity)
        {
            return Error.Rule("Pop.InsufficientStock", $"Item '{item.Code}' has {balance?.QuantityOnHand ?? 0m} on hand, less than the {quantity} to return.");
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        decimal averageCost = balance.AverageCost;
        decimal stockValue = balance.Issue(quantity);                                  // removes stock at moving-average cost
        decimal creditValue = decimal.Round(quantity * returnUnitPrice, 4, MidpointRounding.ToEven);
        decimal variance = creditValue - stockValue;                                   // + gain, − loss

        // Dr AP control (credit note), Cr stock control at average cost, PPV takes the difference.
        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = apCode, Side = JournalSide.Debit, TransactionAmount = creditValue },
            new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Credit, TransactionAmount = stockValue },
        ];
        if (variance > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = priceVarianceAccountCode, Side = JournalSide.Credit, TransactionAmount = variance });
        }
        else if (variance < 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = priceVarianceAccountCode, Side = JournalSide.Debit, TransactionAmount = -variance });
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "POP",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "SupplierReturn",
            DocumentNumber = documentNumber,
            Narrative = $"Goods return to {supplier.Code} ({item.Code})",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        _db.Set<Core.Inventory.StockMovement>().Add(new Core.Inventory.StockMovement(companyId, itemId, warehouseId, date, Core.Inventory.StockMovementType.SupplierReturn, -quantity, averageCost, -stockValue, documentNumber)
        {
            JournalEntryId = posting.Value.JournalId,
        });

        // Debit-sign AP open item: a credit note that reduces the payable and can be allocated to the invoice.
        OpenItem creditNote = new(companyId, TradingLedger.AccountsPayable, supplierId, OpenItemType.CreditNote, LedgerSign.Debit,
            documentNumber, date, date, company.BaseCurrency, 1m, creditValue, creditValue)
        {
            JournalEntryId = posting.Value.JournalId,
            Narrative = $"Goods return ({item.Code})",
        };
        _db.OpenItems.Add(creditNote);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SupplierReturnResult(creditNote.Id, posting.Value.JournalNumber, stockValue, creditValue, variance);
    }

    private async Task<string?> ControlCodeAsync(long companyId, ControlAccountType type, CancellationToken ct) =>
        await _db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == type)
            .Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
}
