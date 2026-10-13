using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Common;
using One4All.Core.SalesOrders;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>The delivery fleet, vehicle runs and loads.</summary>
public sealed class DeliveryPlanningService : IDeliveryPlanningService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public DeliveryPlanningService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> SaveVehicleAsync(long companyId, DeliveryVehicleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Registration) || request.CapacityKg <= 0m || request.CapacityM3 <= 0m || request.MaxStops < 1)
        {
            return Error.Validation("Sop.Vehicle", "A vehicle needs a registration and a positive capacity (and, if given, a positive volume and number of drops).");
        }

        string registration = request.Registration.Trim().ToUpperInvariant();
        DeliveryVehicle? vehicle = await _db.Set<DeliveryVehicle>().FirstOrDefaultAsync(v => v.CompanyId == companyId && v.Registration == registration, ct).ConfigureAwait(false);
        if (vehicle is null)
        {
            vehicle = new DeliveryVehicle(companyId, registration, request.Description, request.CapacityKg);
            _db.Add(vehicle);
        }

        vehicle.Description = request.Description.Trim();
        vehicle.CapacityKg = request.CapacityKg;
        vehicle.CapacityM3 = request.CapacityM3;
        vehicle.MaxStops = request.MaxStops;
        vehicle.DefaultDriver = string.IsNullOrWhiteSpace(request.DefaultDriver) ? null : request.DefaultDriver.Trim();
        vehicle.IsActive = true;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return vehicle.Id;
    }

    /// <inheritdoc />
    public async Task<Result<VehicleLoad>> AssignAsync(long companyId, DeliveryAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        string registration = (assignment.Vehicle ?? string.Empty).Trim().ToUpperInvariant();
        DeliveryVehicle? vehicle = await _db.Set<DeliveryVehicle>().AsNoTracking().FirstOrDefaultAsync(v => v.CompanyId == companyId && v.Registration == registration, ct).ConfigureAwait(false);
        if (vehicle is null || !vehicle.IsActive)
        {
            return Error.NotFound("Sop.VehicleNotFound", $"No active vehicle {registration}.");
        }

        string reference = (assignment.DeliveryReference ?? string.Empty).Trim();
        SalesDelivery? delivery = await _db.SalesDeliveries.Include(d => d.Lines).FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Reference == reference, ct).ConfigureAwait(false);
        if (delivery is null)
        {
            return Error.NotFound("Sop.DeliveryNotFound", $"No delivery {reference}.");
        }

        if (delivery.DeliveredOn is not null)
        {
            return Error.Rule("Sop.DeliveryConfirmed", $"Delivery {reference} has already been delivered.");
        }

        // The run that day without this delivery, then with it at its stop.
        List<SalesDelivery> run = await _db.SalesDeliveries.Include(d => d.Lines)
            .Where(d => d.CompanyId == companyId && d.Vehicle == registration && d.PlannedFor == assignment.PlannedFor && d.Id != delivery.Id && d.DeliveredOn == null)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> weights = await WeightsAsync(companyId, [.. run, delivery], ct).ConfigureAwait(false);
        decimal planned = run.Sum(d => weights[d.Id]);
        if (planned + weights[delivery.Id] > vehicle.CapacityKg)
        {
            return Error.Rule("Sop.VehicleOverloaded",
                $"{registration} carries {vehicle.CapacityKg:0.##} kg; {planned:0.##} kg is already planned on {assignment.PlannedFor:yyyy-MM-dd} and {reference} weighs {weights[delivery.Id]:0.##} kg.");
        }

        if (vehicle.MaxStops is int maxStops && run.Count + 1 > maxStops)
        {
            return Error.Rule("Sop.VehicleFull", $"{registration} makes at most {maxStops} drops a day.");
        }

        List<SalesDelivery> ordered = [.. run.OrderBy(d => d.StopSequence ?? int.MaxValue).ThenBy(d => d.Id)];
        int position = Math.Clamp((assignment.Stop ?? ordered.Count + 1) - 1, 0, ordered.Count);
        ordered.Insert(position, delivery);
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].StopSequence = i + 1;
        }

        delivery.Vehicle = registration;
        delivery.PlannedFor = assignment.PlannedFor;
        delivery.Route = string.IsNullOrWhiteSpace(assignment.Route) ? delivery.Route : assignment.Route.Trim();
        delivery.Driver = string.IsNullOrWhiteSpace(assignment.Driver) ? vehicle.DefaultDriver ?? delivery.Driver : assignment.Driver.Trim();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Load(vehicle, assignment.PlannedFor, ordered, weights);
    }

    /// <inheritdoc />
    public async Task<Result<VehicleLoad>> LoadAsync(long companyId, string vehicle, DateOnly date, CancellationToken ct = default)
    {
        string registration = (vehicle ?? string.Empty).Trim().ToUpperInvariant();
        DeliveryVehicle? found = await _db.Set<DeliveryVehicle>().AsNoTracking().FirstOrDefaultAsync(v => v.CompanyId == companyId && v.Registration == registration, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Error.NotFound("Sop.VehicleNotFound", $"No vehicle {registration}.");
        }

        List<SalesDelivery> run = await _db.SalesDeliveries.AsNoTracking().Include(d => d.Lines)
            .Where(d => d.CompanyId == companyId && d.Vehicle == registration && d.PlannedFor == date).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> weights = await WeightsAsync(companyId, run, ct).ConfigureAwait(false);
        return Load(found, date, [.. run.OrderBy(d => d.StopSequence ?? int.MaxValue).ThenBy(d => d.Id)], weights);
    }

    private static VehicleLoad Load(DeliveryVehicle vehicle, DateOnly date, List<SalesDelivery> ordered, Dictionary<long, decimal> weights)
        => new(vehicle.Registration, date, vehicle.CapacityKg, ordered.Sum(d => weights[d.Id]),
            [.. ordered.Select((d, i) => new VehicleStop(d.StopSequence ?? i + 1, d.Reference, d.Route, weights[d.Id]))]);

    // What each delivery weighs: its quantities at the items' unit weights.
    private async Task<Dictionary<long, decimal>> WeightsAsync(long companyId, List<SalesDelivery> deliveries, CancellationToken ct)
    {
        List<long> itemIds = [.. deliveries.SelectMany(d => d.Lines).Select(l => l.ItemId).Distinct()];
        Dictionary<long, decimal> unit = await _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.UnitWeightKg ?? 0m, ct).ConfigureAwait(false);
        return deliveries.ToDictionary(d => d.Id, d => d.Lines.Sum(l => l.Quantity * unit.GetValueOrDefault(l.ItemId)));
    }
}
