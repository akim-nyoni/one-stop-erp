namespace One4All.Application.Abstractions.Reporting;

/// <summary>One snapshot's last refresh.</summary>
/// <param name="Name">The snapshot.</param>
/// <param name="RefreshedOnUtc">When.</param>
/// <param name="DurationMs">How long it took.</param>
/// <param name="Rows">Rows it holds.</param>
public sealed record SnapshotRefresh(string Name, DateTimeOffset RefreshedOnUtc, int DurationMs, long Rows);

/// <summary>Rows read from a snapshot, with when it was taken (null: never refreshed since it was created).</summary>
/// <typeparam name="T">The row type.</typeparam>
/// <param name="Rows">The rows.</param>
/// <param name="RefreshedOnUtc">When the snapshot was last refreshed.</param>
public sealed record Snapshot<T>(IReadOnlyList<T> Rows, DateTimeOffset? RefreshedOnUtc);

/// <summary>A customer's aged debt in the snapshot.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="CustomerCode">Its code.</param>
/// <param name="CustomerName">Its name.</param>
/// <param name="AsAt">The day it was aged to.</param>
/// <param name="Current">Not yet due.</param>
/// <param name="Days1To30">1–30 days overdue.</param>
/// <param name="Days31To60">31–60 days overdue.</param>
/// <param name="Days61To90">61–90 days overdue.</param>
/// <param name="Over90">More than 90 days overdue.</param>
/// <param name="Total">The balance.</param>
public sealed record AgedDebtSnapshotRow(long CustomerId, string? CustomerCode, string? CustomerName, DateOnly AsAt, decimal Current, decimal Days1To30, decimal Days31To60,
    decimal Days61To90, decimal Over90, decimal Total);

/// <summary>Stock value in the snapshot, by warehouse and category.</summary>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="WarehouseCode">Its code.</param>
/// <param name="Category">The item category ("" for none).</param>
/// <param name="Items">Items held.</param>
/// <param name="Quantity">Quantity on hand.</param>
/// <param name="Value">Value.</param>
public sealed record StockValuationSnapshotRow(long WarehouseId, string WarehouseCode, string Category, long Items, decimal Quantity, decimal Value);

/// <summary>Revenue posted in a day, by department, in the snapshot.</summary>
/// <param name="Date">The posting date.</param>
/// <param name="Department">The department ("" for none).</param>
/// <param name="Revenue">Net revenue (credits less debits on income accounts).</param>
/// <param name="Lines">Journal lines behind it.</param>
public sealed record SalesByDaySnapshotRow(DateOnly Date, string Department, decimal Revenue, long Lines);

/// <summary>A quick-search hit.</summary>
/// <param name="Kind">Item, Customer, Supplier, Lead or Account.</param>
/// <param name="Id">The record id.</param>
/// <param name="Code">Its code (e-mail for a lead).</param>
/// <param name="Name">Its name or description.</param>
/// <param name="Score">How close the match is (1 = contains the term).</param>
public sealed record QuickSearchHit(string Kind, long Id, string? Code, string Name, double Score);

/// <summary>
/// The heavy reporting aggregates kept as materialised snapshots (brief §13: aged debt, stock valuation, sales by day), refreshed together
/// on a schedule without blocking readers, each read stamped with when it was taken; and one typo-tolerant search across items, customers,
/// suppliers, leads and accounts on trigram indexes.
/// </summary>
public interface IReportingSnapshotService
{
    /// <summary>Refreshes every snapshot (concurrently: reports keep reading the previous one meanwhile).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Each refresh.</returns>
    Task<IReadOnlyList<SnapshotRefresh>> RefreshAsync(CancellationToken ct = default);

    /// <summary>When each snapshot was last refreshed.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refreshes.</returns>
    Task<IReadOnlyList<SnapshotRefresh>> LastRefreshesAsync(CancellationToken ct = default);

    /// <summary>Aged debt by customer, from the snapshot.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows, largest balance first.</returns>
    Task<Snapshot<AgedDebtSnapshotRow>> AgedDebtAsync(long companyId, CancellationToken ct = default);

    /// <summary>Stock value by warehouse and category, from the snapshot.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows.</returns>
    Task<Snapshot<StockValuationSnapshotRow>> StockValuationAsync(long companyId, CancellationToken ct = default);

    /// <summary>Revenue by day and department, from the snapshot.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="from">First day.</param>
    /// <param name="to">Last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows, by date.</returns>
    Task<Snapshot<SalesByDaySnapshotRow>> SalesByDayAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Finds items, customers, suppliers, leads and accounts whose name or code contains the term or nearly matches a word of it.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="term">What was typed.</param>
    /// <param name="take">At most this many.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hits, exact containment first, then by closeness.</returns>
    Task<IReadOnlyList<QuickSearchHit>> SearchAsync(long companyId, string term, int take = 25, CancellationToken ct = default);
}
