using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>
/// Manages purchase requisitions and their conversion into purchase orders. Requisitions are ledger-neutral;
/// conversion delegates to <see cref="IProcurementService"/> so the resulting PO behaves like any other.
/// </summary>
public sealed class PurchaseRequisitionService : IPurchaseRequisitionService
{
    private readonly One4AllDbContext _db;
    private readonly IProcurementService _procurement;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="procurement">The procurement service (conversion target).</param>
    /// <param name="budget">Checks and commits the budget at submission, approval and conversion.</param>
    /// <param name="permissions">Checks an approver may override a budget block.</param>
    public PurchaseRequisitionService(One4AllDbContext db, IProcurementService procurement, Application.Abstractions.Budgeting.IBudgetGateService? budget = null,
        Application.Abstractions.Security.IPermissionService? permissions = null)
    {
        _db = db;
        _procurement = procurement;
        _budget = budget;
        _permissions = permissions;
    }

    private readonly Application.Abstractions.Budgeting.IBudgetGateService? _budget;
    private readonly Application.Abstractions.Security.IPermissionService? _permissions;

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, long supplierId, string requisitionNumber, DateOnly requestedDate, CurrencyCode currency, IReadOnlyList<PurchaseRequisitionLineRequest> lines, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return Error.Validation("Pop.NoReqLines", "A requisition needs at least one line.");
        }

        if (lines.Any(l => l.Quantity <= 0m))
        {
            return Error.Rule("Pop.ReqLineQty", "Every requisition line quantity must be positive.");
        }

        bool supplierExists = await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId && s.IsBlacklisted, ct).ConfigureAwait(false))
        {
            return Error.Rule("Pop.SupplierBlacklisted", "The supplier is blacklisted; requisitions cannot name it.");
        }

        if (!supplierExists)
        {
            return Error.NotFound("Pop.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        PurchaseRequisition req = new(companyId, supplierId, requisitionNumber, requestedDate, currency);
        foreach (PurchaseRequisitionLineRequest line in lines)
        {
            req.AddLine(line.ItemId, line.Description, line.Quantity, line.UnitPrice).ExpenseAccountCode = string.IsNullOrWhiteSpace(line.ExpenseAccountCode) ? null : line.ExpenseAccountCode.Trim().ToUpperInvariant();
        }

        _db.Set<PurchaseRequisition>().Add(req);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return req.Id;
    }

    /// <inheritdoc />
    public async Task<Result> SubmitAsync(long companyId, long requisitionId, CancellationToken ct = default)
    {
        // The budget gate at submission (brief §4.18.5): a block refuses it; a warning or an approval need travels with it.
        Result<Core.Budgeting.BudgetAction> gate = await GateAsync(companyId, requisitionId, null, ct).ConfigureAwait(false);
        return gate.IsFailure
            ? Result.Failure(gate.Error)
            : await TransitionAsync(companyId, requisitionId, r => r.Status == PurchaseRequisitionStatus.Draft, r => { r.Submit(); r.BudgetOutcome = gate.Value; },
                "Only a draft requisition can be submitted.", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result> ApproveAsync(long companyId, long requisitionId, long approvedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            // Checked again at approval, when the budget may have moved; an overrun needing approval needs someone who may override.
            Result<Core.Budgeting.BudgetAction> gate = await GateAsync(companyId, requisitionId, approvedByUserId, ct).ConfigureAwait(false);
            if (gate.IsFailure)
            {
                return Result.Failure(gate.Error);
            }

            Result moved = await TransitionAsync(companyId, requisitionId, r => r.Status == PurchaseRequisitionStatus.Submitted, r => { r.Approve(approvedByUserId); r.BudgetOutcome = gate.Value; },
                "Only a submitted requisition can be approved.", ct).ConfigureAwait(false);
            if (moved.IsFailure || _budget is null)
            {
                return moved;
            }

            // An approved requisition commits its budget until it becomes an order.
            PurchaseRequisition req = await _db.Set<PurchaseRequisition>().Include(r => r.Lines).SingleAsync(r => r.Id == requisitionId, ct).ConfigureAwait(false);
            Result<int> committed = await _budget.CommitAsync(companyId, req.RequisitionNumber, req.RequestedDate, Demands(req), ct).ConfigureAwait(false);
            return committed.IsFailure ? Result.Failure(committed.Error) : Result.Success();
        }, ct);

    private static List<Application.Abstractions.Budgeting.BudgetDemand> Demands(PurchaseRequisition req)
        => [.. req.Lines.Where(l => !string.IsNullOrWhiteSpace(l.ExpenseAccountCode)).Select(l => new Application.Abstractions.Budgeting.BudgetDemand(l.ExpenseAccountCode!, null, l.Quantity * l.UnitPrice))];

    // The budget check for a requisition: Block refuses; RequireApproval at approval needs an approver who may override budget blocks.
    private async Task<Result<Core.Budgeting.BudgetAction>> GateAsync(long companyId, long requisitionId, long? approverUserId, CancellationToken ct)
    {
        PurchaseRequisition? req = await _db.Set<PurchaseRequisition>().AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == requisitionId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        List<Application.Abstractions.Budgeting.BudgetDemand> demands = req is null ? [] : Demands(req);
        if (_budget is null || demands.Count == 0)
        {
            return Core.Budgeting.BudgetAction.Allow;
        }

        Result<Application.Abstractions.Budgeting.BudgetGateResult> check = await _budget.CheckAsync(companyId, req!.RequestedDate, demands, ct).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error.Code == "Budget.NoFiscalYear" ? Core.Budgeting.BudgetAction.Allow : check.Error;
        }

        Core.Budgeting.BudgetAction outcome = check.Value.Outcome;
        if (outcome == Core.Budgeting.BudgetAction.Block)
        {
            return Error.Rule("Budget.Blocked", check.Value.Warning!);
        }

        if (outcome == Core.Budgeting.BudgetAction.RequireApproval && approverUserId is long approver
            && (_permissions is null || !(await _permissions.ResolveAsync(approver, ct).ConfigureAwait(false)).Has("Budget.OverrideBlock", Core.Security.OperationFlags.View)))
        {
            return Error.Rule("Budget.ApprovalNeeded", $"{check.Value.Warning} Approval needs someone who may override budget blocks.");
        }

        return outcome;
    }

    /// <inheritdoc />
    public Task<Result> RejectAsync(long companyId, long requisitionId, CancellationToken ct = default)
        => TransitionAsync(companyId, requisitionId, r => r.Status is PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Submitted, r => r.Reject(), "Only a draft or submitted requisition can be rejected.", ct);

    /// <inheritdoc />
    public Task<Result<long>> ConvertToPurchaseOrderAsync(long companyId, long requisitionId, string orderNumber, DateOnly orderDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => ConvertCoreAsync(companyId, requisitionId, orderNumber, orderDate, ct), ct);

    // One unit of work: the requisition's commitment is released and the order raised (committing its own) together.
    private async Task<Result<long>> ConvertCoreAsync(long companyId, long requisitionId, string orderNumber, DateOnly orderDate, CancellationToken ct)
    {
        PurchaseRequisition? req = await _db.Set<PurchaseRequisition>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == requisitionId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (req is null)
        {
            return Error.NotFound("Pop.ReqNotFound", $"Requisition {requisitionId} not found.");
        }

        if (req.Status != PurchaseRequisitionStatus.Approved)
        {
            return Error.Rule("Pop.ReqNotApproved", $"Only an approved requisition can be converted; {req.RequisitionNumber} is {req.Status}.");
        }

        if (_budget is not null)
        {
            await _budget.ReleaseAsync(companyId, req.RequisitionNumber, ct).ConfigureAwait(false);
        }

        Result<PurchaseOrderResult> order = await _procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            BudgetApprovedByUserId = req.ApprovedByUserId,
            CompanyId = companyId,
            SupplierId = req.SupplierId,
            OrderNumber = orderNumber,
            OrderDate = orderDate,
            Currency = req.Currency,
            Lines = req.Lines
                .Select(l => new PurchaseOrderLineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.ExpenseAccountCode))
                .ToList(),
        }, ct).ConfigureAwait(false);

        if (order.IsFailure)
        {
            return order.Error;
        }

        req.MarkConverted(order.Value.PurchaseOrderId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Value.PurchaseOrderId;
    }

    private async Task<Result> TransitionAsync(long companyId, long requisitionId, Func<PurchaseRequisition, bool> allowed, Action<PurchaseRequisition> apply, string message, CancellationToken ct)
    {
        PurchaseRequisition? req = await _db.Set<PurchaseRequisition>().FirstOrDefaultAsync(r => r.Id == requisitionId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (req is null)
        {
            return Result.Failure(Error.NotFound("Pop.ReqNotFound", $"Requisition {requisitionId} not found."));
        }

        if (!allowed(req))
        {
            return Result.Failure(Error.Rule("Pop.ReqState", message));
        }

        apply(req);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
