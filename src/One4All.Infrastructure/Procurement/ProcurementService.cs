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
            .Select(s => new { s.Code, s.IsBlacklisted, s.DefaultTaxCode }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
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
            .Select(l => new One4All.Application.Abstractions.Budgeting.BudgetDemand(l.ExpenseAccountCode!, Dimension(request.Department), l.Quantity * l.UnitPrice))];
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

        // Each line's tax code: the one given, else the item's, else the supplier's default (a default that is not an active code is left off).
        List<long> itemIds = [.. request.Lines.Select(l => l.ItemId).Distinct()];
        Dictionary<long, string?> itemTaxCodes = await _db.Set<Core.Inventory.Item>().AsNoTracking().Where(i => i.CompanyId == request.CompanyId && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.TaxCode, ct).ConfigureAwait(false);
        HashSet<string> activeTaxCodes = [.. await _db.TaxCodes.AsNoTracking().Where(t => t.CompanyId == request.CompanyId && t.IsActive).Select(t => t.Code).ToListAsync(ct).ConfigureAwait(false)];
        if (request.Lines.Select(l => Dimension(l.TaxCode)).FirstOrDefault(c => c is not null && !activeTaxCodes.Contains(c)) is string unknownTax)
        {
            return Error.NotFound("Tax.CodeNotFound", $"No active tax code {unknownTax}.");
        }

        string? LineTaxCode(PurchaseOrderLineRequest line)
            => Dimension(line.TaxCode) ?? new[] { Dimension(itemTaxCodes.GetValueOrDefault(line.ItemId)), Dimension(supplier.DefaultTaxCode) }.FirstOrDefault(c => c is not null && activeTaxCodes.Contains(c));

        PurchaseOrder order = new(request.CompanyId, request.SupplierId, orderNumber, request.OrderDate, request.Currency)
        {
            RequiredBy = request.RequiredBy, FrameworkAgreementId = request.FrameworkAgreementId,
            Department = Dimension(request.Department), CostCentre = Dimension(request.CostCentre), Project = Dimension(request.Project),
        };
        foreach (PurchaseOrderLineRequest line in request.Lines)
        {
            // Ordered in a pack (a box of 12): the line holds the item's own units and unit price, and remembers the pack it was ordered in.
            Result<(string? Unit, decimal Factor)> unit = await Inventory.ItemUnits.FactorAsync(_db, request.CompanyId, line.ItemId, line.UnitCode, ct).ConfigureAwait(false);
            if (unit.IsFailure)
            {
                return unit.Error;
            }

            PurchaseOrderLine added = order.AddLine(line.ItemId, line.Description, line.Quantity * unit.Value.Factor, decimal.Round(line.UnitPrice / unit.Value.Factor, 6, MidpointRounding.ToEven));
            added.OrderedIn(unit.Value.Unit, unit.Value.Factor);
            added.ExpenseAccountCode = string.IsNullOrWhiteSpace(line.ExpenseAccountCode) ? null : line.ExpenseAccountCode.Trim().ToUpperInvariant();
            added.JobContractId = line.JobContractId;
            added.JobCostHead = line.JobContractId is null ? null : string.IsNullOrWhiteSpace(line.JobCostHead) ? "Materials" : line.JobCostHead.Trim();
            added.TaxCode = LineTaxCode(line);
        }

        _db.Set<PurchaseOrder>().Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The company's purchase-order workflow, when defined, holds the order until it is approved.
        long? job = order.Lines.Select(l => l.JobContractId).FirstOrDefault(j => j != null);
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, request.CompanyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.PurchaseOrder,
            order.OrderNumber, order.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice), request.RaisedByUserId,
            [
                ("Currency", order.Currency.Value), ("Supplier", await _db.Suppliers.Where(s => s.Id == order.SupplierId).Select(s => s.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false)),
                ("Project", job is long j ? await _db.JobContracts.Where(c => c.Id == j).Select(c => c.ContractNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null),
            ], ct).ConfigureAwait(false);
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

        // A foreign-currency order comes into stock (and GRNI) in base currency at the receipt date's rate.
        Result<decimal> grnRate = await OrderRateAsync(request.CompanyId, order.Currency, "GoodsReceived", request.Date, ct).ConfigureAwait(false);
        if (grnRate.IsFailure)
        {
            return grnRate.Error;
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

            // Receive into stock: Dr stock control, Cr GRNI (accrual), at the PO price in base currency.
            decimal baseUnitCost = grnRate.Value == 1m ? line.UnitPrice : decimal.Round(line.UnitPrice * grnRate.Value, 6, MidpointRounding.ToEven);
            Result<StockMovementResult> receipt = await _inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = request.CompanyId,
                ItemId = line.ItemId,
                WarehouseId = request.WarehouseId,
                Date = request.Date,
                Quantity = received.Quantity,
                UnitCost = baseUnitCost,
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

            decimal lineAccrued = decimal.Round(received.Quantity * baseUnitCost, 4, MidpointRounding.ToEven); // what the stock receipt credited to GRNI
            line.Receive(received.Quantity, lineAccrued);
            grn.AddLine(line.Id, line.ItemId, received.Quantity, line.UnitPrice);
            accrued += lineAccrued;
        }

        order.RecomputeStatus();
        _db.Set<GoodsReceivedNote>().Add(grn);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new GrnResult(grn.Id, accrued);
    }

    // The rate (base per unit) a document in the order's currency converts at on the date: 1 for the base currency, else the company's rate policy for the
    // document type (the daily rate by default).
    private async Task<Result<decimal>> OrderRateAsync(long companyId, CurrencyCode currency, string documentType, DateOnly date, CancellationToken ct)
    {
        CurrencyCode baseCurrency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        if (currency == baseCurrency)
        {
            return 1m;
        }

        decimal? rate = await Accounting.ExchangeRateResolver.PolicyRateAsync(_db, companyId, currency, baseCurrency, documentType, "POP", date, ct).ConfigureAwait(false);
        return rate is > 0m ? rate.Value : Error.Rule("Pop.NoExchangeRate", $"No {currency} to {baseCurrency} exchange rate on or before {date:yyyy-MM-dd}.");
    }

    private static string? Dimension(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

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

        // The match rules: the supplier's own tolerances, else the company's; a tolerance given with the request may tighten the price rule, never widen it.
        var supplierRules = await _db.Suppliers.Where(s => s.Id == order.SupplierId).Select(s => new { s.InvoicePriceTolerancePercent, s.InvoiceQuantityTolerancePercent }).FirstAsync(ct).ConfigureAwait(false);
        var companyRules = await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => new { c.InvoicePriceTolerancePercent, c.InvoiceQuantityTolerancePercent }).FirstAsync(ct).ConfigureAwait(false);
        decimal priceTolerance = supplierRules.InvoicePriceTolerancePercent ?? companyRules.InvoicePriceTolerancePercent;
        decimal quantityTolerance = supplierRules.InvoiceQuantityTolerancePercent ?? companyRules.InvoiceQuantityTolerancePercent;
        if (!request.PriceAcceptedThroughDispute && request.TolerancePercent is decimal asked)
        {
            if (asked > priceTolerance + 0.0001m)
            {
                return Error.Rule("Pop.ToleranceAboveRule", $"A price tolerance of {asked}% is wider than the {priceTolerance}% rule for this supplier (Purchasing › Matching tolerances).");
            }

            priceTolerance = asked;
        }

        // A foreign-currency invoice is owed at the invoice date's rate; the accrual it clears was made at each receipt's rate.
        Company company = await _db.Companies.FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        bool foreign = order.Currency != company.BaseCurrency;
        Result<decimal> rateResult = await OrderRateAsync(request.CompanyId, order.Currency, "SupplierInvoice", request.Date, ct).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal ToBase(decimal amount) => foreign ? decimal.Round(amount * rate, 2, MidpointRounding.AwayFromZero) : amount;

        decimal invoiceValue = 0m;
        decimal accruedValue = 0m;
        decimal grniCleared = 0m;
        List<(string Account, decimal Committed, decimal Invoiced)> spent = [];
        List<(PurchaseOrderLine Line, decimal Difference)> jobDifferences = [];
        List<(PurchaseOrderLine Line, decimal Quantity, decimal Invoiced, decimal Difference)> stockDifferences = [];
        Dictionary<string, (decimal Net, string? CostAccount)> taxable = new(StringComparer.Ordinal);
        foreach (InvoiceMatchLine invLine in request.Lines)
        {
            PurchaseOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == invLine.PurchaseOrderLineId);
            if (line is null)
            {
                return Error.NotFound("Pop.LineNotFound", $"PO line {invLine.PurchaseOrderLineId} not found.");
            }

            // Three-way quantity check: cannot invoice more than received-but-not-yet-invoiced.
            decimal availableToInvoice = line.ReceivedQuantity - line.InvoicedQuantity;
            if (invLine.Quantity > (availableToInvoice * (1m + quantityTolerance / 100m)) + 0.0001m)
            {
                return Error.Rule("Pop.QuantityMismatch",
                    $"PO line {line.Id}: invoicing {invLine.Quantity} but only {availableToInvoice} received and uninvoiced (quantity tolerance {quantityTolerance}%).");
            }

            // Three-way price check within tolerance.
            decimal tolerance = line.UnitPrice * priceTolerance / 100m;
            if (!request.PriceAcceptedThroughDispute && Math.Abs(invLine.UnitPrice - line.UnitPrice) > tolerance + 0.0001m)
            {
                return Error.Rule("Pop.PriceMismatch",
                    $"PO line {line.Id}: invoice price {invLine.UnitPrice} is outside {priceTolerance}% of PO price {line.UnitPrice}.");
            }

            decimal lineNet = decimal.Round(invLine.Quantity * invLine.UnitPrice, 4, MidpointRounding.ToEven);
            decimal lineAccrued = decimal.Round(invLine.Quantity * line.UnitPrice, 4, MidpointRounding.ToEven);
            invoiceValue += lineNet;
            accruedValue += lineAccrued;
            if (line.JobContractId is not null && invLine.UnitPrice != line.UnitPrice)
            {
                jobDifferences.Add((line, lineNet - lineAccrued));
            }
            else if (invLine.UnitPrice != line.UnitPrice)
            {
                stockDifferences.Add((line, Math.Max(0m, Math.Min(invLine.Quantity, availableToInvoice)), invLine.Quantity, lineNet - lineAccrued));
            }

            if (line.ExpenseAccountCode is not null)
            {
                spent.Add((line.ExpenseAccountCode, invLine.Quantity * line.UnitPrice, invLine.Quantity * invLine.UnitPrice));
            }

            if (line.TaxCode is string lineTax)
            {
                (decimal net, string? costAccount) = taxable.GetValueOrDefault(lineTax);
                taxable[lineTax] = (net + lineNet, costAccount ?? line.ExpenseAccountCode);
            }

            // GRNI clears at what the receipts accrued (in base): the order price at each receipt's rate.
            decimal cleared = foreign ? line.AccrualToClear(invLine.Quantity, line.UnitPrice * rate) : lineAccrued;
            grniCleared += cleared;
            line.Invoice(invLine.Quantity, cleared);
            _db.Set<InvoicedOrderLine>().Add(new InvoicedOrderLine(request.CompanyId, order.SupplierId, line.Id, request.InvoiceNumber.Trim(), request.Date, invLine.Quantity, line.UnitPrice,
                invLine.UnitPrice));
        }

        // The budget gate at invoice posting: only spend beyond what the order committed is checked again.
        List<One4All.Application.Abstractions.Budgeting.BudgetDemand> excess = [.. spent.Where(s => s.Invoiced > s.Committed)
            .Select(s => new One4All.Application.Abstractions.Budgeting.BudgetDemand(s.Account, order.Department, s.Invoiced - s.Committed))];
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

        // The input tax on the lines' tax codes (brief §3.5), as a purchase invoice posts it: the recoverable share claimed, the rest a cost, and a
        // reverse-charge or deemed code self-assessed (charged as output, never owed to the supplier). A supplier holding an exemption certificate charges none.
        Result<(List<JournalLineRequest> Lines, decimal Owed)> taxed = await MatchTaxLinesAsync(request, supplier.Id, taxable, rate, foreign, ToBase, ct).ConfigureAwait(false);
        if (taxed.IsFailure)
        {
            return taxed.Error;
        }

        decimal gross = invoiceValue + taxed.Value.Owed;
        decimal apBase = ToBase(gross);
        List<JournalLineRequest> matchLines =
        [
            new JournalLineRequest { AccountCode = grniCode, Side = JournalSide.Debit, TransactionAmount = grniCleared, Currency = company.BaseCurrency },
            new JournalLineRequest { AccountCode = apCode, Side = JournalSide.Credit, TransactionAmount = gross, ExchangeRate = rate, BaseAmount = foreign ? apBase : null },
            .. taxed.Value.Lines,
        ];

        // The price variance (in base, at the invoice rate): on a weighted-average item the share still in stock goes onto its value, the rest to the variance account.
        Result<(string? StockCode, List<(Core.Inventory.StockBalance Balance, decimal Amount)> Revalued)> stocked =
            await PriceDifferenceOnStockAsync(request.CompanyId, order.Id, stockDifferences, rate, ct).ConfigureAwait(false);
        if (stocked.IsFailure)
        {
            return stocked.Error;
        }

        List<(Core.Inventory.StockBalance Balance, decimal Amount)> revalued = stocked.Value.Revalued;
        decimal toStockTotal = revalued.Sum(r => r.Amount);
        if (toStockTotal != 0m)
        {
            matchLines.Add(new JournalLineRequest
            {
                AccountCode = stocked.Value.StockCode!, Side = toStockTotal > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(toStockTotal), Currency = company.BaseCurrency,
                Narrative = "Price difference on goods still in stock",
            });
        }

        decimal varianceBase = ToBase(variance) - toStockTotal;
        if (varianceBase != 0m)
        {
            matchLines.Add(new JournalLineRequest
            {
                AccountCode = request.PriceVarianceAccountCode!.Trim(), Side = varianceBase > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(varianceBase),
                Currency = company.BaseCurrency, CostCentre = order.CostCentre, Department = order.Department, Project = order.Project,
            });
        }

        foreach ((PurchaseOrderLine line, decimal difference) in jobDifferences.Where(d => d.Difference != 0m))
        {
            matchLines.Add(new JournalLineRequest
            {
                AccountCode = line.ExpenseAccountCode!, Side = difference > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(difference),
                ExchangeRate = rate, BaseAmount = foreign ? ToBase(Math.Abs(difference)) : null,
                Narrative = $"Price difference to job ({line.Description})", CostCentre = order.CostCentre, Department = order.Department, Project = order.Project,
            });
        }

        // What GRNI accrued at the receipts' rates and what the goods cost at the invoice's rate differ by an exchange difference, kept apart from the
        // price variance: a gain (the currency weakened since the receipt) to the FX gain account, a loss to the FX loss one.
        decimal exchange = foreign ? matchLines.Sum(l => (l.Side == JournalSide.Debit ? 1m : -1m) * (l.BaseAmount ?? l.TransactionAmount)) : 0m;
        if (exchange != 0m)
        {
            ControlAccountType fxType = exchange > 0m ? ControlAccountType.FxGain : ControlAccountType.FxLoss;
            string? fxCode = await ControlCodeAsync(request.CompanyId, fxType, ct).ConfigureAwait(false);
            if (fxCode is null)
            {
                return Error.Rule("Pop.NoFxAccount", $"The invoice at {rate} against goods received at other rates gives an exchange {(exchange > 0m ? "gain" : "loss")} of {Math.Abs(exchange):N2}; "
                    + $"set up an {fxType} control account first.");
            }

            matchLines.Add(new JournalLineRequest
            {
                AccountCode = fxCode, Side = exchange > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(exchange), Currency = company.BaseCurrency,
                Narrative = $"Exchange difference on {order.OrderNumber}",
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

        // The stock's own value takes its share (a value-only movement, so the stock valuation still ties to the stock control account).
        foreach ((Core.Inventory.StockBalance balance, decimal amount) in revalued)
        {
            balance.AddValue(amount);
            _db.StockMovements.Add(new Core.Inventory.StockMovement(request.CompanyId, balance.ItemId, balance.WarehouseId, request.Date, Core.Inventory.StockMovementType.Adjustment, 0m, 0m,
                amount, request.InvoiceNumber.Trim())
            {
                JournalEntryId = posting.Value.JournalId,
            });
        }

        OpenItem openItem = new(request.CompanyId, TradingLedger.AccountsPayable, supplier.Id, OpenItemType.Invoice, LedgerSign.Credit,
            request.InvoiceNumber, request.Date, supplier.DueDateFor(request.Date), order.Currency, rate, gross, apBase)
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

        return new MatchInvoiceResult(openItem.Id, posting.Value.JournalNumber, gross);
    }

    // The tax lines of a matched invoice, one tax code at a time at its rate on the invoice date, and the tax owed to the supplier (in the order's currency).
    private async Task<Result<(List<JournalLineRequest> Lines, decimal Owed)>> MatchTaxLinesAsync(MatchInvoiceRequest request, long supplierId,
        Dictionary<string, (decimal Net, string? CostAccount)> taxable, decimal rate, bool foreign, Func<decimal, decimal> toBase, CancellationToken ct)
    {
        List<JournalLineRequest> lines = [];
        if (taxable.Count == 0
            || await Trading.TraderCertificates.HoldsAsync(_db, request.CompanyId, TradingLedger.AccountsPayable, supplierId, CertificatePurpose.TaxExemption, request.Date, ct).ConfigureAwait(false))
        {
            return (lines, 0m);
        }

        List<string> codes = [.. taxable.Keys];
        Dictionary<string, TaxCode> defined = await _db.TaxCodes.AsNoTracking().Include(t => t.Rates).Where(t => t.CompanyId == request.CompanyId && codes.Contains(t.Code))
            .ToDictionaryAsync(t => t.Code, ct).ConfigureAwait(false);
        decimal owed = 0m;
        foreach ((string code, (decimal net, string? costAccount)) in taxable.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (!defined.TryGetValue(code, out TaxCode? taxCode))
            {
                return Error.NotFound("Tax.CodeNotFound", $"No tax code {code}.");
            }

            decimal tax = decimal.Round(net * taxCode.RateOn(request.Date) / 100m, 2, MidpointRounding.AwayFromZero);
            if (tax == 0m)
            {
                continue;
            }

            bool selfAssessed = taxCode.Type is TaxType.ReverseCharge or TaxType.Deemed;
            decimal recoverablePercent = Math.Clamp(taxCode.RecoverablePercent, 0m, 100m);
            decimal recoverable = decimal.Round(tax * recoverablePercent / 100m, 2, MidpointRounding.AwayFromZero);
            decimal taxBase = toBase(tax);
            decimal recoverableBase = recoverable == tax ? taxBase : toBase(recoverable);
            if (recoverable != 0m)
            {
                string? input = taxCode.InputAccountCode ?? await ControlCodeAsync(request.CompanyId, ControlAccountType.VatInput, ct).ConfigureAwait(false);
                if (input is null)
                {
                    return Error.Validation("Tax.InputAccount", "The invoice carries tax but there is no input tax account to claim it on.");
                }

                lines.Add(new JournalLineRequest
                {
                    AccountCode = input, Side = JournalSide.Debit, TransactionAmount = recoverable, ExchangeRate = rate, BaseAmount = foreign ? recoverableBase : null, TaxCode = code,
                });
            }

            if (tax - recoverable != 0m)
            {
                // The irrecoverable part is part of what the purchase cost: the line's expense account, else the price variance account.
                string? cost = costAccount ?? (string.IsNullOrWhiteSpace(request.PriceVarianceAccountCode) ? null : request.PriceVarianceAccountCode.Trim());
                if (cost is null)
                {
                    return Error.Validation("Tax.IrrecoverableAccount", $"Part of the {code} tax cannot be claimed: give a price variance account to charge it to.");
                }

                lines.Add(new JournalLineRequest
                {
                    AccountCode = cost, Side = JournalSide.Debit, TransactionAmount = tax - recoverable, ExchangeRate = rate, BaseAmount = foreign ? taxBase - recoverableBase : null, TaxCode = code,
                    Narrative = $"Irrecoverable tax ({100m - recoverablePercent:0.##}%)",
                });
            }

            if (selfAssessed)
            {
                string? output = taxCode.OutputAccountCode ?? await ControlCodeAsync(request.CompanyId, ControlAccountType.VatOutput, ct).ConfigureAwait(false);
                if (output is null)
                {
                    return Error.Validation("Tax.OutputAccount", $"Tax code {code} is self-assessed but there is no output tax account to charge it to.");
                }

                lines.Add(new JournalLineRequest
                {
                    AccountCode = output, Side = JournalSide.Credit, TransactionAmount = tax, ExchangeRate = rate, BaseAmount = foreign ? taxBase : null, TaxCode = code,
                });
            }
            else
            {
                owed += tax;
            }
        }

        return (lines, owed);
    }

    // The share of an invoice's price difference that belongs on stock still held: for a weighted-average item, the part of the invoiced quantity the
    // warehouses its receipts went into still hold (in base, at the invoice rate), spread over those warehouses by quantity. The rest stays a variance.
    private async Task<Result<(string? StockCode, List<(Core.Inventory.StockBalance Balance, decimal Amount)> Revalued)>> PriceDifferenceOnStockAsync(long companyId, long orderId,
        List<(PurchaseOrderLine Line, decimal Quantity, decimal Invoiced, decimal Difference)> differences, decimal rate, CancellationToken ct)
    {
        List<(Core.Inventory.StockBalance Balance, decimal Amount)> revalued = [];
        string? stockCode = differences.Count == 0 ? null : await ControlCodeAsync(companyId, ControlAccountType.Stock, ct).ConfigureAwait(false);
        if (stockCode is null)
        {
            return ((string?)null, revalued);
        }

        List<string> grnNumbers = await _db.Set<GoodsReceivedNote>().Where(g => g.CompanyId == companyId && g.PurchaseOrderId == orderId).Select(g => g.GrnNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> onHandLeft = [];
        foreach ((PurchaseOrderLine line, decimal quantity, decimal invoiced, decimal difference) in differences)
        {
            Core.Inventory.CostingMethod? costing = await _db.Set<Core.Inventory.Item>().Where(i => i.Id == line.ItemId).Select(i => (Core.Inventory.CostingMethod?)i.CostingMethod)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (costing != Core.Inventory.CostingMethod.WeightedAverage || quantity <= 0m || invoiced <= 0m || difference == 0m)
            {
                continue;
            }

            List<long> warehouses = await _db.StockMovements.Where(m => m.CompanyId == companyId && m.ItemId == line.ItemId && m.Type == Core.Inventory.StockMovementType.Receipt
                    && m.Reference != null && grnNumbers.Contains(m.Reference))
                .Select(m => m.WarehouseId).Distinct().ToListAsync(ct).ConfigureAwait(false);
            List<Core.Inventory.StockBalance> balances = await _db.StockBalances.Where(b => b.CompanyId == companyId && b.ItemId == line.ItemId && warehouses.Contains(b.WarehouseId)
                    && b.QuantityOnHand > 0m)
                .OrderBy(b => b.WarehouseId).ToListAsync(ct).ConfigureAwait(false);
            decimal held = balances.Sum(b => b.QuantityOnHand);
            decimal onHand = onHandLeft.TryGetValue(line.ItemId, out decimal left) ? left : held;
            decimal inStock = Math.Min(quantity, onHand);
            onHandLeft[line.ItemId] = onHand - inStock;
            decimal toStock = inStock <= 0m ? 0m : decimal.Round(difference * rate * inStock / invoiced, 2, MidpointRounding.AwayFromZero);
            decimal spread = toStock;
            for (int i = 0; i < balances.Count && toStock != 0m; i++)
            {
                decimal part = i == balances.Count - 1 ? spread : decimal.Round(toStock * balances[i].QuantityOnHand / held, 2, MidpointRounding.AwayFromZero);
                revalued.Add((balances[i], part));
                spread -= part;
            }
        }

        return (stockCode, revalued);
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
