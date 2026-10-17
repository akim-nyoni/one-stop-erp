using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>A component quantity actually consumed, overriding the BOM standard.</summary>
/// <param name="ComponentItemId">The component.</param>
/// <param name="Quantity">The quantity consumed.</param>
public readonly record struct ComponentUsage(long ComponentItemId, decimal Quantity);

/// <summary>An assembly run to post.</summary>
public sealed class AssemblyRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the BOM.</summary>
    public long BillOfMaterialsId { get; init; }

    /// <summary>Gets the warehouse components come from and finished goods go to.</summary>
    public long WarehouseId { get; init; }

    /// <summary>Gets the run date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the output the run was planned for.</summary>
    public decimal PlannedOutput { get; init; }

    /// <summary>Gets the output actually produced (defaults to planned).</summary>
    public decimal? ActualOutput { get; init; }

    /// <summary>Gets the components actually consumed, where they differ from the standard for the planned output.</summary>
    public IReadOnlyList<ComponentUsage> Usage { get; init; } = [];

    /// <summary>Gets the clearing account components are charged to and finished goods credited from (nets to zero).</summary>
    public string ClearingAccountCode { get; init; } = string.Empty;
}

/// <summary>A disassembly to post: finished goods taken apart into their components.</summary>
public sealed class DisassemblyRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the BOM.</summary>
    public long BillOfMaterialsId { get; init; }

    /// <summary>Gets the warehouse the finished goods come from and components go to.</summary>
    public long WarehouseId { get; init; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the finished units taken apart.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Gets the components actually recovered, where they differ from the BOM (scrap excluded) for the quantity.</summary>
    public IReadOnlyList<ComponentUsage> Recovered { get; init; } = [];

    /// <summary>Gets the clearing account the finished goods are issued to and the components received from (nets to zero).</summary>
    public string ClearingAccountCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the cost is split by the components' relative sales value (each one's selling price on the default price list times the
    /// quantity recovered - the joint-product method) instead of by their cost. Components with no price get no cost.
    /// </summary>
    public bool BySalesValue { get; init; }
}

/// <summary>The posted run.</summary>
/// <param name="RunId">The run.</param>
/// <param name="RunNumber">Its number.</param>
/// <param name="TotalCost">The cost moved into finished goods.</param>
/// <param name="UnitCost">The cost per finished unit.</param>
public readonly record struct AssemblyResult(long RunId, string RunNumber, decimal TotalCost, decimal UnitCost);

/// <summary>Assembles finished goods from their bill of materials (brief §14 E 98–99).</summary>
public interface IAssemblyService
{
    /// <summary>
    /// Posts a run: issues each component (the standard for the planned output, or the usage given) to the clearing account and receives
    /// the actual output into stock at the total component cost from the same account, so the clearing account nets to zero.
    /// </summary>
    /// <param name="request">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run, or an error.</returns>
    Task<Result<AssemblyResult>> AssembleAsync(AssemblyRequest request, CancellationToken ct = default);

    /// <summary>
    /// Takes finished goods apart (returns, rework, kit breaking): issues them to the clearing account at their cost and receives each
    /// component recovered from the same account, the cost split by each component's recovered quantity at its current average cost
    /// (standard cost when it has none), so nothing is gained or lost. Recorded as a run marked as a disassembly.
    /// </summary>
    /// <param name="request">The disassembly.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run (total cost taken apart, cost per finished unit).</returns>
    Task<Result<AssemblyResult>> DisassembleAsync(DisassemblyRequest request, CancellationToken ct = default);
}
