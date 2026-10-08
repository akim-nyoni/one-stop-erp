using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A service connected to a property, as listed.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="Service">The service code (or the built-in category).</param>
/// <param name="Tariff">The tariff code.</param>
/// <param name="Quantity">How many units are billed (bins, points…).</param>
/// <param name="Deposit">The deposit taken.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="StartDate">When it was connected.</param>
public sealed record PropertyServiceRow(string StandNumber, string Service, string Tariff, decimal Quantity, decimal Deposit, ServiceLinkStatus Status, DateOnly StartDate);

/// <summary>
/// A council's configurable services and the connections to them (brief §4.9): service types of its own, billed like a built-in category; connecting
/// one to a property with a quantity and a deposit (invoiced to the ratepayer and held in the deposit account); and suspending, disconnecting,
/// reconnecting or closing it — only an active connection is billed.
/// </summary>
public interface IMunicipalServiceTypeService
{
    /// <summary>Creates or updates a service type.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">Its code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="category">The built-in category it bills as.</param>
    /// <param name="metered">Whether it is metered.</param>
    /// <param name="defaultDeposit">The deposit a new connection pays.</param>
    /// <param name="depositAccountCode">The account deposits are held in (needed when there is a deposit).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SaveAsync(long companyId, string code, string name, ServiceType category, bool metered, decimal defaultDeposit, string? depositAccountCode, CancellationToken ct = default);

    /// <summary>Connects a service to a property.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="serviceCode">The service type.</param>
    /// <param name="tariffCode">The tariff billed (of the service's category).</param>
    /// <param name="startDate">When it starts.</param>
    /// <param name="quantity">How many units (1 when blank).</param>
    /// <param name="deposit">The deposit (null: the service type's).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deposit invoice number, if a deposit was charged, or null.</returns>
    Task<Result<string?>> ConnectAsync(long companyId, string standNumber, string serviceCode, string tariffCode, DateOnly startDate, decimal? quantity, decimal? deposit, CancellationToken ct = default);

    /// <summary>Changes a connection's status (Active reconnects; Closed ends it on the date).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="serviceCode">The service type.</param>
    /// <param name="status">The new status.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetStatusAsync(long companyId, string standNumber, string serviceCode, ServiceLinkStatus status, DateOnly date, CancellationToken ct = default);

    /// <summary>Changes how many units a connection bills.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="serviceCode">The service type.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetQuantityAsync(long companyId, string standNumber, string serviceCode, decimal quantity, CancellationToken ct = default);

    /// <summary>A property's connections (all, or every property's when no stand is given).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standNumber">The stand, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The connections.</returns>
    Task<IReadOnlyList<PropertyServiceRow>> ConnectionsAsync(long companyId, string? standNumber, CancellationToken ct = default);
}
