using One4All.Application.Abstractions.Pos;

namespace One4All.Application.Abstractions.Offline;

/// <summary>Seals and opens data kept on this machine (brief §6B offline resilience: the terminal's local store is encrypted).</summary>
public interface ILocalDataProtector
{
    /// <summary>Encrypts data for this machine and user.</summary>
    /// <param name="plain">The data.</param>
    /// <returns>The sealed bytes.</returns>
    byte[] Protect(byte[] plain);

    /// <summary>Decrypts data sealed by <see cref="Protect"/>; fails if it was altered or sealed elsewhere.</summary>
    /// <param name="sealedData">The sealed bytes.</param>
    /// <returns>The data.</returns>
    byte[] Unprotect(byte[] sealedData);
}

/// <summary>A customer as the terminal keeps it offline.</summary>
/// <param name="Id">The customer.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
public sealed record OfflineCustomer(long Id, string Code, string Name);

/// <summary>A tender currency as the terminal keeps it offline.</summary>
/// <param name="Code">The currency code.</param>
/// <param name="Rate">Its rate to the base currency.</param>
public sealed record OfflineTenderCurrency(string Code, decimal Rate);

/// <summary>
/// What a POS terminal needs to keep selling when the database is unreachable (brief §6B): the company, store and terminal, the priced
/// catalogue and its tax rates, the posting accounts, tender currencies, customers, and the till session open when it was taken.
/// </summary>
public sealed record PosOfflineSnapshot
{
    /// <summary>Gets when the snapshot was taken.</summary>
    public DateTimeOffset TakenUtc { get; init; }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the company's base currency code.</summary>
    public string BaseCurrency { get; init; } = string.Empty;

    /// <summary>Gets the warehouse sales issue from.</summary>
    public long WarehouseId { get; init; }

    /// <summary>Gets the terminal.</summary>
    public long TerminalId { get; init; }

    /// <summary>Gets the till session open when the snapshot was taken, if any.</summary>
    public long? OpenSessionId { get; init; }

    /// <summary>Gets the tax rates by code (as fractions).</summary>
    public IReadOnlyDictionary<string, decimal> TaxRates { get; init; } = new Dictionary<string, decimal>();

    /// <summary>Gets the priced catalogue.</summary>
    public IReadOnlyList<PosTile> Tiles { get; init; } = [];

    /// <summary>Gets the posting accounts by role (Cash, Card, Tax, OverShort, Discount, MobileMoney, GiftCard).</summary>
    public IReadOnlyDictionary<string, string> Accounts { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the tender currencies.</summary>
    public IReadOnlyList<OfflineTenderCurrency> TenderCurrencies { get; init; } = [];

    /// <summary>Gets the customers.</summary>
    public IReadOnlyList<OfflineCustomer> Customers { get; init; } = [];

    /// <summary>Gets the promotions running when the snapshot was taken or later, so offline sales are priced as online ones.</summary>
    public IReadOnlyList<One4All.Application.Pos.PromotionDefinition> Promotions { get; init; } = [];

    /// <summary>Gets the terminal's branch (for branch promotions).</summary>
    public long? BranchId { get; init; }

    /// <summary>Finds the tile a scanned or typed code names: its barcode, PLU or item code (any case).</summary>
    /// <param name="code">The code.</param>
    /// <returns>The tile, or null.</returns>
    public PosTile? Find(string code)
    {
        string key = code.Trim();
        return Tiles.FirstOrDefault(t => string.Equals(t.Barcode, key, StringComparison.OrdinalIgnoreCase))
            ?? Tiles.FirstOrDefault(t => string.Equals(t.Plu, key, StringComparison.OrdinalIgnoreCase))
            ?? Tiles.FirstOrDefault(t => string.Equals(t.Code, key, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The terminal's encrypted local copy of what it needs to sell offline.</summary>
public interface ILocalCatalogueStore
{
    /// <summary>Saves the snapshot, replacing the last one.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task SaveAsync(PosOfflineSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Loads the last snapshot.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot, or null when there is none (or it cannot be opened on this machine).</returns>
    Task<PosOfflineSnapshot?> LoadAsync(CancellationToken ct = default);
}
