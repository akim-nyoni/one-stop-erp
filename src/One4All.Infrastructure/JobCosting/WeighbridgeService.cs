using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.JobCosting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.JobCosting;

/// <summary>Runs the weighbridge: first and second weighs, manual-weight control, haulage and stockpile issues.</summary>
public sealed class WeighbridgeService : IWeighbridgeService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">Issues stockpile stock.</param>
    /// <param name="numbers">Numbers the tickets.</param>
    /// <param name="clock">Stamps time in and out.</param>
    public WeighbridgeService(One4AllDbContext db, IInventoryService inventory, INumberSequenceService numbers, IClock clock)
    {
        _db = db;
        _inventory = inventory;
        _numbers = numbers;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<Result<(long TicketId, string TicketNumber)>> FirstWeighAsync(FirstWeighRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => FirstWeighCoreAsync(request, ct), ct);

    // One unit of work: the ticket number and the ticket commit together.
    private async Task<Result<(long TicketId, string TicketNumber)>> FirstWeighCoreAsync(FirstWeighRequest request, CancellationToken ct)
    {
        string registration = request.Vehicle.Trim().ToUpperInvariant();
        WeighbridgeVehicle? vehicle = await _db.WeighbridgeVehicles.AsNoTracking().FirstOrDefaultAsync(v => v.CompanyId == request.CompanyId && v.Registration == registration, ct).ConfigureAwait(false);
        if (vehicle is { IsActive: false })
        {
            return Error.Rule("Wb.VehicleBlocked", $"Vehicle {registration} is barred from the weighbridge.");
        }

        decimal? weight = request.WeightKg;
        if (weight is null && request.Direction == WeighDirection.Outbound)
        {
            weight = vehicle?.StoredTareKg;
        }

        if (weight is null)
        {
            return Error.Validation("Wb.Weight", "The first weigh needs a weight (or a stored tare for an outbound vehicle).");
        }

        if (WeighbridgeRules.ValidateWeight(weight.Value, request.IsManual, request.ManualReason, request.OperatorUserId, request.AuthorisedByUserId) is { } invalid)
        {
            return invalid;
        }

        Item? item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.ItemId && i.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Wb.ProductNotFound", "The product is not an item of this company.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "WB", request.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        WeighbridgeTicket ticket = request.Direction == WeighDirection.Outbound
            ? WeighbridgeTicket.Outbound(request.CompanyId, number.Value, registration, item.Description, weight.Value, request.CubicMetresPerTonne, request.Date)
            : new WeighbridgeTicket(request.CompanyId, number.Value, registration, item.Description, weight.Value, request.CubicMetresPerTonne, request.Date);
        ticket.ItemId = item.Id;
        ticket.CustomerId = request.CustomerId;
        ticket.SupplierId = request.SupplierId;
        ticket.DriverName = request.Driver?.Trim();
        ticket.SiteName = request.Site?.Trim();
        ticket.WarehouseId = request.WarehouseId;
        ticket.DistanceKm = request.DistanceKm;
        ticket.BatchRunNumber = request.BatchRunNumber;
        ticket.TimeInUtc = _clock.UtcNow;
        ticket.OperatorUserId = request.OperatorUserId;
        if (request.IsManual)
        {
            ticket.IsManualWeight = true;
            ticket.ManualReason = request.ManualReason!.Trim();
            ticket.ManualAuthorisedByUserId = request.AuthorisedByUserId;
        }

        _db.WeighbridgeTickets.Add(ticket);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (ticket.Id, ticket.TicketNumber);
    }

    /// <inheritdoc />
    public Task<Result<WeighbridgeCompletion>> SecondWeighAsync(SecondWeighRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => SecondWeighCoreAsync(request, ct), ct);

    // One unit of work: the completed ticket and the stockpile issue commit together or not at all.
    private async Task<Result<WeighbridgeCompletion>> SecondWeighCoreAsync(SecondWeighRequest request, CancellationToken ct)
    {
        WeighbridgeTicket? ticket = await _db.WeighbridgeTickets.FirstOrDefaultAsync(t => t.Id == request.TicketId && t.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Error.NotFound("Wb.TicketNotFound", $"Ticket {request.TicketId} not found.");
        }

        if (ticket.IsComplete)
        {
            return Error.Rule("Wb.TicketComplete", $"Ticket {ticket.TicketNumber} is already complete.");
        }

        if (WeighbridgeRules.ValidateWeight(request.WeightKg, request.IsManual, request.ManualReason, request.OperatorUserId, request.AuthorisedByUserId) is { } invalid)
        {
            return invalid;
        }

        if (request.DeductionKg < 0m)
        {
            return Error.Validation("Wb.Deduction", "A deduction cannot be negative.");
        }

        ticket.DeductionKg = request.DeductionKg;
        ticket.Complete(request.WeightKg, _clock.UtcNow);
        if (ticket.NetWeightKg <= 0m)
        {
            return Error.Rule("Wb.NoNetWeight", $"The weights leave no net load on ticket {ticket.TicketNumber}; re-weigh.");
        }

        if (request.IsManual)
        {
            ticket.IsManualWeight = true;
            ticket.ManualReason = string.Join("; ", new[] { ticket.ManualReason, request.ManualReason!.Trim() }.Where(r => !string.IsNullOrEmpty(r)));
            ticket.ManualAuthorisedByUserId = request.AuthorisedByUserId;
        }

        if (ticket.DistanceKm is decimal distance)
        {
            List<HaulageBand> bands = [.. (await _db.HaulageZones.AsNoTracking().Where(z => z.CompanyId == request.CompanyId).ToListAsync(ct).ConfigureAwait(false))
                .Select(z => new HaulageBand(z.Code, z.FromKm, z.ToKm, z.RatePerTonne, z.MinimumCharge))];
            if (WeighbridgeRules.Haulage(bands, distance, ticket.NetTonnes) is { } charge)
            {
                ticket.HaulageZoneCode = charge.Zone;
                ticket.HaulageCharge = charge.Charge;
            }
        }

        if (ticket is { Direction: WeighDirection.Outbound, WarehouseId: long stockpile, ItemId: long itemId })
        {
            Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
            {
                CompanyId = request.CompanyId, ItemId = itemId, WarehouseId = stockpile, Date = ticket.Date, Quantity = ticket.NetTonnes, Reference = ticket.TicketNumber,
            }, ct).ConfigureAwait(false);
            if (issue.IsFailure)
            {
                return issue.Error;
            }
        }

        decimal? maxGross = await _db.WeighbridgeVehicles.AsNoTracking().Where(v => v.CompanyId == request.CompanyId && v.Registration == ticket.VehicleRegistration)
            .Select(v => v.MaxGrossKg).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WeighbridgeCompletion(ticket.NetTonnes, ticket.NetCubicMetres, ticket.HaulageCharge, maxGross is decimal max && ticket.GrossWeightKg > max);
    }
}
