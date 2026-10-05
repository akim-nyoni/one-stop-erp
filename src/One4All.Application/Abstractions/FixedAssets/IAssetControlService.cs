using One4All.Core.Common;

namespace One4All.Application.Abstractions.FixedAssets;

/// <summary>Physical verification and planned maintenance of fixed assets (brief §14 M 210, 214).</summary>
public interface IAssetControlService
{
    /// <summary>Starts an FAC-numbered verification listing every held asset the register places at the location (or everywhere).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The count date.</param>
    /// <param name="location">The location, or null for all.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The count id, or an error.</returns>
    Task<Result<long>> StartCountAsync(long companyId, DateOnly date, string? location, CancellationToken ct = default);

    /// <summary>
    /// Records an asset seen during a count: found or found elsewhere when expected; found elsewhere when registered but expected at
    /// another location; unregistered when the tag is not on the register.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="countId">The count.</param>
    /// <param name="tag">The asset number / tag scanned.</param>
    /// <param name="foundLocation">Where it was seen.</param>
    /// <param name="condition">Its condition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> RecordSightingAsync(long companyId, long countId, string tag, string? foundLocation, string? condition, CancellationToken ct = default);

    /// <summary>Closes a count (unseen assets become missing), optionally moving assets found elsewhere to where they were seen.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="countId">The count.</param>
    /// <param name="updateLocations">Whether to update the register's location for assets found elsewhere.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many assets are missing, or an error.</returns>
    Task<Result<int>> CloseCountAsync(long companyId, long countId, bool updateLocations, CancellationToken ct = default);

    /// <summary>Plans a recurring maintenance task.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="description">The task.</param>
    /// <param name="intervalMonths">How often, in months.</param>
    /// <param name="firstDue">When it is first due.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The plan id, or an error.</returns>
    Task<Result<long>> PlanMaintenanceAsync(long companyId, long assetId, string description, int intervalMonths, DateOnly firstDue, CancellationToken ct = default);

    /// <summary>Logs a planned task as done (into the maintenance history) and rolls its next due date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="planId">The plan.</param>
    /// <param name="date">When it was done.</param>
    /// <param name="cost">What it cost.</param>
    /// <param name="provider">Who did it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The maintenance log id, or an error.</returns>
    Task<Result<long>> CompletePlannedAsync(long companyId, long planId, DateOnly date, decimal cost, string? provider, CancellationToken ct = default);
}
