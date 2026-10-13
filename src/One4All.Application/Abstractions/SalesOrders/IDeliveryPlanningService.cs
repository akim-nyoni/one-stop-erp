using One4All.Core.Common;

namespace One4All.Application.Abstractions.SalesOrders;

/// <summary>A vehicle to register or update.</summary>
/// <param name="Registration">The registration.</param>
/// <param name="Description">What it is.</param>
/// <param name="CapacityKg">The most it carries, in kilograms.</param>
/// <param name="CapacityM3">The most it carries, in cubic metres.</param>
/// <param name="MaxStops">The most drops in a day.</param>
/// <param name="DefaultDriver">The usual driver.</param>
public sealed record DeliveryVehicleRequest(string Registration, string Description, decimal CapacityKg, decimal? CapacityM3 = null, int? MaxStops = null, string? DefaultDriver = null);

/// <summary>Where a delivery goes on a day's run.</summary>
/// <param name="DeliveryReference">The delivery note.</param>
/// <param name="Vehicle">The vehicle registration.</param>
/// <param name="PlannedFor">The day.</param>
/// <param name="Route">The route.</param>
/// <param name="Driver">The driver (blank: the vehicle's usual driver).</param>
/// <param name="Stop">The drop's place in the run (blank: after the last).</param>
public sealed record DeliveryAssignment(string DeliveryReference, string Vehicle, DateOnly PlannedFor, string? Route = null, string? Driver = null, int? Stop = null);

/// <summary>A drop on a vehicle's run.</summary>
/// <param name="Stop">Its place.</param>
/// <param name="DeliveryReference">The delivery note.</param>
/// <param name="Route">The route.</param>
/// <param name="WeightKg">What it weighs.</param>
public sealed record VehicleStop(int Stop, string DeliveryReference, string? Route, decimal WeightKg);

/// <summary>A vehicle's load for a day.</summary>
/// <param name="Vehicle">The registration.</param>
/// <param name="Date">The day.</param>
/// <param name="CapacityKg">What it may carry.</param>
/// <param name="WeightKg">What is planned on it.</param>
/// <param name="Stops">The drops in run order.</param>
public sealed record VehicleLoad(string Vehicle, DateOnly Date, decimal CapacityKg, decimal WeightKg, IReadOnlyList<VehicleStop> Stops)
{
    /// <summary>Gets the share of the capacity used.</summary>
    public decimal Utilisation => CapacityKg <= 0m ? 0m : decimal.Round(WeightKg / CapacityKg * 100m, 1);
}

/// <summary>
/// The delivery fleet and vehicle loads (brief §4.2 delivery scheduling, vehicle loads, route planning): vehicles with their capacity, deliveries
/// assigned to a vehicle's run in stop order, refused when they would overload it (by weight, from the items' unit weights) or exceed its
/// drops, and each vehicle's load for a day.
/// </summary>
public interface IDeliveryPlanningService
{
    /// <summary>Registers a vehicle, or updates one already registered.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="request">The vehicle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The vehicle id, or why it was refused.</returns>
    Task<Result<long>> SaveVehicleAsync(long companyId, DeliveryVehicleRequest request, CancellationToken ct = default);

    /// <summary>Puts a delivery on a vehicle's run for a day, checking the vehicle's weight and drops; the stops are renumbered in order.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assignment">Where it goes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The vehicle's load with it, or why it was refused.</returns>
    Task<Result<VehicleLoad>> AssignAsync(long companyId, DeliveryAssignment assignment, CancellationToken ct = default);

    /// <summary>A vehicle's load for a day.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="vehicle">The registration.</param>
    /// <param name="date">The day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The load, or why there is none.</returns>
    Task<Result<VehicleLoad>> LoadAsync(long companyId, string vehicle, DateOnly date, CancellationToken ct = default);
}
