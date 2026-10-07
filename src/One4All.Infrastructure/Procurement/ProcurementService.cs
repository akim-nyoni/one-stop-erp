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
    private readonly One4All.Application.Abstractions.Budgeting.IBudgetGateService? _budget;
    private readonly One4All.Application.Abstractions.Security.IPermissionService? _permissions;
    private readonly Lazy<Application.Abstractions.Workflow.IWorkflowService>? _workflow;
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
    /// <param name="budget">The budget gate at order raising and invoice posting.</param>
    /// <param name="permissions">Checks who may approve a budget overrun.</param>
    public ProcurementService(One4AllDbContext db, IInventoryService inventory, IPostingEngine postingEngine, One4All.Application.Abstractions.Numbering.INumberSequenceService numbers,
        One4All.Application.Abstractions.Budgeting.IBudgetGateService? budget = null, One4All.Application.Abstractions.Security.IPermissionService? permissions = null,
        Lazy<Application.Abstractions.Workflow.IWorkflowService>? workflow = null)
    {
        _workflow = workflow;
        _budget = budget;
        _permissions = permissions;
        _db = db;
        _inventory = inventory;
        _postingEngine = postingEngine;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public Task<Result<PurchaseOrderResult>> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => CreatePurchaseOrderCoreAsync(request, ct), ct);

    // One unit of work: the order and its budget commitment are saved together.
    private async Task<Result<PurchaseOrderResult>> CreatePurchaseOrderCoreAsync(CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.Where(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId)
            .Select(s => new { s.Code, s.IsBlacklisted }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Pop.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        if (supplier.IsBlacklisted)
        {
            return Error.Rule("Pop.SupplierBlacklisted", $"Supplier {supplier.Code} is blacklisted; no new orders can be placed.");
        }

        if (request.FrameworkAgreementId is long agreementId)
        {
            FrameworkAgreement? agreement = await _db.FrameworkAgreements.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agreementId && a.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
            if (agreement is null || agreement.SupplierId != request.SupplierId)
            {
                return Error.Validation("Pop.FrameworkSupplier", "The framework agreement does not exist or is with a different supplier.");
            }

            if (request.OrderDate < agreement.StartDate || request.OrderDate > agreement.EndDate)
            {
                return Error.Rule("Pop.FrameworkOutOfTerm", $"Agreement {agreement.Reference} runs {agreement.StartDate:yyyy-MM-dd} to {agreement.EndDate:yyyy-MM-dd}.");
            }

            decimal called = (await _db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines).Where(o => o.FrameworkAgreementId == agreementId && o.Status != PurchaseOrderStatus.Cancelled)
                .ToListAsync(ct).ConfigureAwait(false)).Sum(o => o.TotalNet);
            decimal thisOrder = request.Lines.Sum(l => l.Quantity * l.UnitPrice);
            if (called + thisOrder > agreement.CeilingValue)
            {
                return Error.Rule("Pop.FrameworkExceeded", $"Agreement {agreement.Reference} has {agreement.CeilingValue - called:0.00} left; this order is {thisOrder:0.00}.");
            }
        }

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

        // The budget gate at order raising (brief §4.18.5); budgeted lines commit their spend under the order number.
        List<One4All.Application.Abstractions.Budgeting.BudgetDemand> demands = [.. request.Lines.Where(l => !string.IsNullOrWhiteSpace(l.ExpenseAccountCode))
            .Select(l => new One4All.Application.Abstractions.Budgeting.BudgetDemand(l.ExpenseAccountCode!, null, l.Quantity * l.UnitPrice))];
        if (_budget is not null && demands.Count > 0)
        {
            Result<One4All.Application.Abstractions.Budgeting.BudgetGateResult> check = await _budget.CheckAsync(request.CompanyId, request.OrderDate, demands, ct).ConfigureAwait(false);
            if (check.IsSuccess && await BudgetRefusalAsync(check.Value, request.BudgetApprovedByUserId, ct).ConfigureAwait(false) is Error refused)
            {
                return refused;
            }
        }

        // A line bought for a job is received straight to the job's WIP account: it needs that account and an open job.
        List<long> jobIds = [.. request.Lines.Where(l => l.JobContractId is not null).Select(l => l.JobContractId!.Value).Distinct()];
        if (jobIds.Count > 0)
        {
            if (request.Lines.Any(l => l.JobContractId is not null && string.IsNullOrWhiteSpace(l.ExpenseAccountCode)))
            {
                return Error.Validation("Pop.JobLineAccount", "A line bought for a job needs the job's WIP account as its expense account.");
            }

            int open = await _db.Set<Core.JobCosting.JobContract>().CountAsync(c => c.CompanyId == request.CompanyId && jobIds.Contains(c.Id) && c.Status == Core.JobCosting.JobStatus.Open, ct)
                .ConfigureAwait(false);
            if (open != jobIds.Count)
            {
                return Error.Rule("Job.ContractNotOpen", "A purchase order line is for a job that is not found or not open.");
            }
        }

        PurchaseOrder order = new(request.CompanyId, request.SupplierId, orderNumber, request.OrderDate, request.Currency) { RequiredBy = request.RequiredBy, FrameworkAgreementId = request.FrameworkAgreementId };
        foreach (PurchaseOrderLineRequest line in request.Lines)
        {
            PurchaseOrderLine added = order.AddLine(line.ItemId, line.Description, line.Quantity, line.UnitPrice);
            added.ExpenseAccountCode = string.IsNullOrWhiteSpace(line.ExpenseAccountCode) ? null : line.ExpenseAccountCode.Trim().ToUpperInvariant();
            added.JobContractId = line.JobContractId;
            added.JobCostHead = line.JobContractId is null ? null : string.IsNullOrWhiteSpace(line.JobCostHead) ? "Materials" : line.JobCostHead.Trim();
        }

        _db.Set<PurchaseOrder>().Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The company's purchase-order workflow, when defined, holds the order until it is approved.
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, request.CompanyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.PurchaseOrder,
            order.OrderNumber, order.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice), request.RaisedByUserId, ct).ConfigureAwait(false);
        if (governed.IsFailure)
        {
            return governed.Error;
        }

        if (governed.Value)
        {
            order.HoldForApproval();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (_budget is not null && demands.Count > 0)
        {
            Result<int> committed = await _budget.CommitAsync(request.CompanyId, order.OrderNumber, request.OrderDate, demands, ct).ConfigureAwait(false);
            if (committed.IsFailure && committed.Error.Code != "Budget.NoFiscalYear")
            {
                return committed.Error;
            }
        }

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
        if (_budget is not null)
        {
            await _budget.ReleaseAsync(companyId, order.OrderNumber, ct).ConfigureAwait(false); // a cancelled order frees its budget
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<GrnResult>> ReceiveGoodsAsync(ReceiveGoodsRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReceiveGoodsCoreAsync(request, ct), ct);

    // One unit of work: every line's stock receipt, the PO lines and the GRN commit together or not at all.
    private async Task<Result<GrnResult>> ReceiveGoodsCoreAsync(ReceiveGoodsRequest request, CancellationToken ct)
    {
        PurchaseOrder? order = await _db.Set<PurchaseOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId && o.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Pop.OrderNotFound", $"Purchase order {request.PurchaseOrderId} not found.");
        }

        if (order.AwaitingApproval)
        {
            return Error.Rule("Pop.AwaitingApproval", $"Purchase order {order.OrderNumber} is awaiting approval.");
        }

        if (order.AwaitingReapproval)
        {
            return Error.Rule("Pop.AwaitingReapproval", $"Purchase order {order.OrderNumber} has an amendment awaiting re-approval.");
        }

        string? grniCode = await ControlCodeAsync(request.CompanyId, ControlAccountType.GoodsReceivedNotInvoiced, ct).ConfigureAwait(false);
        if (grniCode is null)
        {
            return Error.Rule("Pop.NoGrni", "No goods-received-not-invoiced control account is configured.");
        }

        // Over-receipt: within the supplier's (else the company's) tolerance of each line, or authorised by a supervisor who may.
        decimal tolerance = await _db.Suppliers.Where(s => s.Id == order.SupplierId).Select(s => s.OverReceiptTolerancePercent).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.OverReceiptTolerancePercent).FirstAsync(ct).ConfigureAwait(false);
        List<string> over = [];
        foreach (IGrouping<long, ReceiveLine> receiving in request.Lines.GroupBy(l => l.PurchaseOrderLineId))
        {
            if (order.Lines.FirstOrDefault(l => l.Id == receiving.Key) is PurchaseOrderLine ordered
                && ordered.ReceivedQuantity + receiving.Sum(r => r.Quantity) > ordered.OrderedQuantity * (1m + tolerance / 100m))
            {
                over.Add($"{ordered.Description}: {ordered.ReceivedQuantity + receiving.Sum(r => r.Quantity):0.###} of {ordered.OrderedQuantity:0.###} ordered");
            }
        }

        if (over.Count > 0)
        {
            if (request.OverReceiptAuthorisedByUserId is not long authoriser)
            {
                return Error.Rule("Pop.OverReceipt", $"More than ordered (tolerance {tolerance:0.##}%): {string.Join("; ", over)}. A supervisor must authorise it.");
            }

            if (authoriser == request.ReceivedByUserId && request.ReceivedByUserId != 0)
            {
                return Error.Rule("Pop.OverReceiptSelf", "The receiver cannot authorise their own over-receipt.");
            }

            if (_permissions is null || !(await _permissions.ResolveAsync(authoriser, ct).ConfigureAwait(false)).Has("Procurement.Grn", One4All.Core.Security.OperationFlags.AuthoriseLevel1))
            {
                return Error.Forbidden("Pop.OverReceiptNotAuthorised", "Only a user who may authorise goods received notes can accept more than was ordered.");
            }
        }

        GoodsReceivedNote grn = new(request.CompanyId, order.SupplierId, order.Id, request.GrnNumber, request.Date)
        {
            OverReceiptAuthorisedByUserId = over.Count > 0 ? request.OverReceiptAuthorisedByUserId : null,
        };
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
                Lots = received.Lots ?? [],
            }, ct).ConfigureAwait(false);

            if (receipt.IsFailure)
            {
                return receipt.Error;
            }

            // Bought for a job: delivered to site, so straight out of stock to the job's WIP account and onto the job's cost.
            if (line.JobContractId is long jobId)
            {
                Core.JobCosting.JobContract? job = await _db.Set<Core.JobCosting.JobContract>().FirstOrDefaultAsync(c => c.Id == jobId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
                if (job is null || job.Status != Core.JobCosting.JobStatus.Open)
                {
                    return Error.Rule("Job.ContractNotOpen", $"PO line {line.Id} is for a job that is not open.");
                }

                Result<StockMovementResult> toJob = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = request.CompanyId, ItemId = line.ItemId, WarehouseId = request.WarehouseId, Date = request.Date, Quantity = received.Quantity,
                    Reference = $"{request.GrnNumber} {job.ContractNumber}", ChargeAccountCode = line.ExpenseAccountCode!,
                }, ct).ConfigureAwait(false);
                if (toJob.IsFailure)
                {
                    return toJob.Error;
                }

                await JobCosting.JobCosts.CaptureAsync(_db, job, line.JobCostHead ?? "Materials", Core.JobCosting.JobCostSource.GoodsReceived, toJob.Value.Value, request.Date,
                    $"{request.GrnNumber} {line.Description}", ct).ConfigureAwait(false);
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
    public Task<Result<MatchInvoiceResult>> MatchInvoiceAsync(MatchInvoiceRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => MatchInvoiceCoreAsync(request, ct), ct);

    // One unit of work: a refused match (a price or quantity check on a later line, a duplicate invoice, a closed period) leaves no PO line marked invoiced.
    private async Task<Result<MatchInvoiceResult>> MatchInvoiceCoreAsync(MatchInvoiceRequest request, CancellationToken ct)
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

        // The same duplicate-invoice control as direct purchase-ledger entry.
        if (string.IsNullOrWhiteSpace(request.InvoiceNumber))
        {
            return Error.Validation("Ap.InvoiceNumberRequired", "A supplier invoice needs the supplier's invoice number.");
        }

        List<string> existingInvoices = await _db.OpenItems
            .Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplier.Id && i.ItemType == OpenItemType.Invoice)
            .Select(i => i.DocumentNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        string? clash = existingInvoices.FirstOrDefault(n => One4All.Application.Trading.DocumentNumbers.Same(n, request.InvoiceNumber));
        if (clash is not null)
        {
            return Error.Conflict("Ap.DuplicateInvoice", $"Supplier {supplier.Code} invoice {request.InvoiceNumber} has already been entered (as {clash}).");
        }

        string? grniCode = await ControlCodeAsync(request.CompanyId, ControlAccountType.GoodsReceivedNotInvoiced, ct).ConfigureAwait(false);
        string? apCode = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (grniCode is null || apCode is null)
        {
            return Error.Rule("Pop.NoControlAccounts", "GRNI and/or AP control accounts are not configured.");
        }

        decimal invoiceValue = 0m;
        decimal accruedValue = 0m;
        List<(string Account, decimal Committed, decimal Invoiced)> spent = [];
        List<(PurchaseOrderLine Line, decimal Difference)> jobDifferences = [];
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
            accruedValue += decimal.Round(invLine.Quantity * line.UnitPrice, 4, MidpointRounding.ToEven);
            if (line.JobContractId is not null && invLine.UnitPrice != line.UnitPrice)
            {
                jobDifferences.Add((line, decimal.Round(invLine.Quantity * invLine.UnitPrice, 4, MidpointRounding.ToEven) - decimal.Round(invLine.Quantity * line.UnitPrice, 4, MidpointRounding.ToEven)));
            }
            if (line.ExpenseAccountCode is not null)
            {
                spent.Add((line.ExpenseAccountCode, invLine.Quantity * line.UnitPrice, invLine.Quantity * invLine.UnitPrice));
            }

            line.Invoice(invLine.Quantity);
        }

        // The budget gate at invoice posting: only spend beyond what the order committed is checked again.
        List<One4All.Application.Abstractions.Budgeting.BudgetDemand> excess = [.. spent.Where(s => s.Invoiced > s.Committed)
            .Select(s => new One4All.Application.Abstractions.Budgeting.BudgetDemand(s.Account, null, s.Invoiced - s.Committed))];
        if (_budget is not null && excess.Count > 0)
        {
            Result<One4All.Application.Abstractions.Budgeting.BudgetGateResult> check = await _budget.CheckAsync(request.CompanyId, request.Date, excess, ct).ConfigureAwait(false);
            if (check.IsSuccess && await BudgetRefusalAsync(check.Value, request.BudgetApprovedByUserId, ct).ConfigureAwait(false) is Error refused)
            {
                return refused;
            }
        }

        // Clear GRNI at what was accrued (the order price) and owe the supplier the invoice price; any difference is a
        // purchase price variance, so GRNI clears exactly.
        decimal variance = invoiceValue - accruedValue - jobDifferences.Sum(d => d.Difference);
        if (variance != 0m && string.IsNullOrWhiteSpace(request.PriceVarianceAccountCode))
        {
            return Error.Rule("Pop.NoPriceVarianceAccount", "The invoice is priced differently from the order: give a purchase price variance account.");
        }

        List<JournalLineRequest> matchLines =
        [
            new JournalLineRequest { AccountCode = grniCode, Side = JournalSide.Debit, TransactionAmount = accruedValue },
            new JournalLineRequest { AccountCode = apCode, Side = JournalSide.Credit, TransactionAmount = invoiceValue },
        ];
        if (variance != 0m)
        {
            matchLines.Add(new JournalLineRequest
            {
                AccountCode = request.PriceVarianceAccountCode!.Trim(), Side = variance > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(variance),
            });
        }

        foreach ((PurchaseOrderLine line, decimal difference) in jobDifferences.Where(d => d.Difference != 0m))
        {
            matchLines.Add(new JournalLineRequest
            {
                AccountCode = line.ExpenseAccountCode!, Side = difference > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(difference),
                Narrative = $"Price difference to job ({line.Description})",
            });
        }

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
            Lines = matchLines,
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

        foreach ((PurchaseOrderLine line, decimal difference) in jobDifferences.Where(d => d.Difference != 0m))
        {
            Core.JobCosting.JobContract job = await _db.Set<Core.JobCosting.JobContract>().FirstAsync(c => c.Id == line.JobContractId, ct).ConfigureAwait(false);
            await JobCosting.JobCosts.CaptureAsync(_db, job, line.JobCostHead ?? "Materials", Core.JobCosting.JobCostSource.PurchaseInvoice, decimal.Round(difference * rate, 2, MidpointRounding.AwayFromZero),
                request.Date, $"{request.InvoiceNumber} price difference", ct).ConfigureAwait(false);
        }

        order.RecomputeStatus();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // What is invoiced is spent: its share of the order's commitment is used up.
        if (_budget is not null)
        {
            foreach ((string account, decimal committed, _) in spent)
            {
                await _budget.ReduceAsync(request.CompanyId, order.OrderNumber, account, committed, ct).ConfigureAwait(false);
            }
        }

        return new MatchInvoiceResult(openItem.Id, posting.Value.JournalNumber, invoiceValue);
    }

    /// <inheritdoc />
    public Task<Result<SupplierReturnResult>> ReturnToSupplierAsync(long companyId, long supplierId, long itemId, long warehouseId, decimal quantity, decimal returnUnitPrice, DateOnly date, string documentNumber, string priceVarianceAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReturnToSupplierCoreAsync(companyId, supplierId, itemId, warehouseId, quantity, returnUnitPrice, date, documentNumber, priceVarianceAccountCode, ct), ct);

    // One unit of work: the stock reduction, its journal and the supplier credit commit together or not at all.
    private async Task<Result<SupplierReturnResult>> ReturnToSupplierCoreAsync(long companyId, long supplierId, long itemId, long warehouseId, decimal quantity, decimal returnUnitPrice, DateOnly date, string documentNumber, string priceVarianceAccountCode, CancellationToken ct)
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
        Inventory.StockIssue issue = await Inventory.StockCosting.PlanIssueAsync(_db, item, balance, quantity, ct).ConfigureAwait(false);
        decimal stockValue = issue.Cost;                                               // the stock's cost: average or oldest FIFO layers
        decimal creditValue = decimal.Round(quantity * returnUnitPrice, 4, MidpointRounding.ToEven);
        decimal variance = creditValue - stockValue;                                   // + gain, − loss

        // Dr AP control (credit note), Cr stock control at its cost, PPV takes the difference.
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

        issue.Apply();
        _db.Set<Core.Inventory.StockMovement>().Add(new Core.Inventory.StockMovement(companyId, itemId, warehouseId, date, Core.Inventory.StockMovementType.SupplierReturn, -quantity, issue.UnitCost, -stockValue, documentNumber)
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

    // A blocking overrun refuses; one needing approval needs an approver who may override budget blocks.
    private async Task<Error?> BudgetRefusalAsync(One4All.Application.Abstractions.Budgeting.BudgetGateResult check, long? approverUserId, CancellationToken ct)
    {
        if (check.Outcome == One4All.Core.Budgeting.BudgetAction.Block)
        {
            return Error.Rule("Budget.Blocked", check.Warning!);
        }

        if (check.Outcome == One4All.Core.Budgeting.BudgetAction.RequireApproval
            && (approverUserId is not long approver || _permissions is null
                || !(await _permissions.ResolveAsync(approver, ct).ConfigureAwait(false)).Has("Budget.OverrideBlock", One4All.Core.Security.OperationFlags.View)))
        {
            return Error.Rule("Budget.ApprovalNeeded", $"{check.Warning} It needs the approval of someone who may override budget blocks.");
        }

        return null;
    }
}

/// <summary>Applies the end of a purchase order's workflow: approved → released for receiving; rejected or returned → cancelled.</summary>
internal sealed class PurchaseOrderWorkflowHandler : Application.Abstractions.Workflow.IWorkflowDocumentHandler
{
    private readonly One4AllDbContext _db;
    private readonly IProcurementService _procurement;

    public PurchaseOrderWorkflowHandler(One4AllDbContext db, IProcurementService procurement)
    {
        _db = db;
        _procurement = procurement;
    }

    public string DocumentType => Application.Abstractions.Workflow.WorkflowDocumentTypes.PurchaseOrder;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        PurchaseOrder? order = await _db.Set<PurchaseOrder>().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.OrderNumber == documentReference && o.AwaitingApproval, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Success();
        }

        if (outcome == Core.Workflow.WorkflowStatus.Approved)
        {
            order.Approve();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }

        order.Approve();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await _procurement.CancelPurchaseOrderAsync(companyId, order.Id, string.IsNullOrWhiteSpace(comment) ? $"{outcome} in the approval workflow" : comment!, ct).ConfigureAwait(false);
    }
}
