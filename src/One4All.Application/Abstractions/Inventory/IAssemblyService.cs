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
}
