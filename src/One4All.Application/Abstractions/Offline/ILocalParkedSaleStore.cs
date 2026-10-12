using One4All.Application.Abstractions.Pos;

namespace One4All.Application.Abstractions.Offline;

/// <summary>A tab parked on this terminal while it was offline.</summary>
/// <param name="Id">Its local id.</param>
/// <param name="Tag">The name, table or customer it is held under.</param>
/// <param name="ParkedAt">When it was parked.</param>
/// <param name="Lines">The ticket lines.</param>
public sealed record LocalParkedSale(Guid Id, string Tag, DateTimeOffset ParkedAt, IReadOnlyList<PosSaleLineRequest> Lines)
{
    /// <summary>The ticket total.</summary>
    public decimal Total => Lines.Sum(l => l.LineNet + l.TaxAmount);
}

/// <summary>
/// Tabs parked while the terminal is offline (brief §4.1 tabs and §6B offline mode): sealed on this computer like the offline catalogue, listed with the
/// server's parked sales and resumed here even before the connection returns.
/// </summary>
public interface ILocalParkedSaleStore
{
    /// <summary>Parks a ticket locally.</summary>
    /// <param name="sale">The ticket.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task ParkAsync(LocalParkedSale sale, CancellationToken ct = default);

    /// <summary>The tabs parked here, oldest first.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tabs.</returns>
    Task<IReadOnlyList<LocalParkedSale>> ListAsync(CancellationToken ct = default);

    /// <summary>Takes a tab off the list to resume it.</summary>
    /// <param name="id">The tab.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tab, or null when it is no longer here.</returns>
    Task<LocalParkedSale?> TakeAsync(Guid id, CancellationToken ct = default);
}
