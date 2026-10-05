using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Posts assembly runs: components out to a clearing account, finished goods in at their cost.</summary>
public sealed class AssemblyService : IAssemblyService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">Issues components and receives finished goods.</param>
    /// <param name="numbers">Numbers the runs.</param>
    public AssemblyService(One4AllDbContext db, IInventoryService inventory, INumberSequenceService numbers)
    {
        _db = db;
        _inventory = inventory;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public Task<Result<AssemblyResult>> AssembleAsync(AssemblyRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => AssembleCoreAsync(request, ct), ct);

    // One unit of work: every component issue, the finished-goods receipt and the run record commit together.
    private async Task<Result<AssemblyResult>> AssembleCoreAsync(AssemblyRequest request, CancellationToken ct)
    {
        decimal actualOutput = request.ActualOutput ?? request.PlannedOutput;
        if (request.PlannedOutput <= 0m || actualOutput <= 0m)
        {
            return Error.Validation("Inv.AssemblyOutput", "An assembly run needs a positive planned and actual output.");
        }

        if (string.IsNullOrWhiteSpace(request.ClearingAccountCode))
        {
            return Error.Validation("Inv.AssemblyClearing", "An assembly run needs a clearing account.");
        }

        BillOfMaterials? bom = await _db.BillsOfMaterials.AsNoTracking().Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == request.BillOfMaterialsId && b.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (bom is not { IsActive: true } || bom.Lines.Count == 0)
        {
            return Error.NotFound("Inv.BomNotFound", "No active bill of materials with components.");
        }

        HashSet<long> components = [.. bom.Lines.Select(l => l.ComponentItemId)];
        if (request.Usage.FirstOrDefault(u => !components.Contains(u.ComponentItemId) || u.Quantity < 0m) is { ComponentItemId: > 0 } stray)
        {
            return Error.Validation("Inv.AssemblyUsage", $"Item {stray.ComponentItemId} is not a component of {bom.Code}, or its usage is negative.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "ASM", request.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        List<AssemblyRunLine> consumed = [];
        foreach (BomLine line in bom.Lines)
        {
            decimal standard = BomRules.Requirement(line.Quantity, line.ScrapPercent, bom.OutputQuantity, actualOutput);
            decimal used = request.Usage.Where(u => u.ComponentItemId == line.ComponentItemId).Select(u => (decimal?)u.Quantity).FirstOrDefault()
                ?? BomRules.Requirement(line.Quantity, line.ScrapPercent, bom.OutputQuantity, request.PlannedOutput);
            decimal cost = 0m;
            if (used > 0m)
            {
                Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = request.CompanyId, ItemId = line.ComponentItemId, WarehouseId = request.WarehouseId, Date = request.Date, Quantity = used, Reference = number.Value,
                    ChargeAccountCode = request.ClearingAccountCode,
                }, ct).ConfigureAwait(false);
                if (issue.IsFailure)
                {
                    return issue.Error;
                }

                cost = issue.Value.Value;
            }

            consumed.Add(new AssemblyRunLine(line.ComponentItemId, standard, used, cost));
        }

        decimal total = consumed.Sum(l => l.Cost);
        decimal unitCost = Math.Round(total / actualOutput, 4, MidpointRounding.AwayFromZero);
        Result<StockMovementResult> receipt = await _inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = request.CompanyId, ItemId = bom.ParentItemId, WarehouseId = request.WarehouseId, Date = request.Date, Quantity = actualOutput, UnitCost = total / actualOutput,
            CreditAccountCode = request.ClearingAccountCode, Reference = number.Value,
        }, ct).ConfigureAwait(false);
        if (receipt.IsFailure)
        {
            return receipt.Error;
        }

        AssemblyRun run = new(request.CompanyId, number.Value, bom.Id, bom.ParentItemId, request.WarehouseId, request.Date, request.PlannedOutput, actualOutput, total);
        consumed.ForEach(run.AddLine);
        _db.AssemblyRuns.Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AssemblyResult(run.Id, run.RunNumber, total, unitCost);
    }
}
