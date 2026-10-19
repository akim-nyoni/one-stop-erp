using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>The municipal service desk: fault tickets with SLA timers, assignment, resolution and the public status.</summary>
public sealed class ServiceDeskService : IServiceDeskService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly INotificationService _notifications;

    private readonly Application.Abstractions.Housing.IRentalMaintenanceService? _maintenance;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="notifications">The notification centre.</param>
    /// <param name="maintenance">Raises the repair for a fault in a council rental unit.</param>
    public ServiceDeskService(One4AllDbContext db, INumberSequenceService numbers, INotificationService notifications,
        Application.Abstractions.Housing.IRentalMaintenanceService? maintenance = null)
    {
        _maintenance = maintenance;
        _db = db;
        _numbers = numbers;
        _notifications = notifications;
    }

    /// <inheritdoc />
    public async Task<Result<long>> DefineCategoryAsync(long companyId, string code, string name, ServiceType? serviceType, decimal responseHours, decimal resolutionHours, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || responseHours <= 0m || resolutionHours < responseHours)
        {
            return Error.Validation("Svc.Category", "A category needs a code, a name, and a positive response target no longer than the resolution target.");
        }

        string key = code.Trim().ToUpperInvariant();
        if (await _db.Set<TicketCategory>().AnyAsync(c => c.CompanyId == companyId && c.Code == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Svc.CategoryExists", $"Category {key} already exists.");
        }

        TicketCategory category = new(companyId, key, name.Trim(), serviceType, responseHours, resolutionHours);
        _db.Set<TicketCategory>().Add(category);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return category.Id;
    }

    /// <inheritdoc />
    public async Task<Result<string>> LogAsync(FaultReport report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(report.ReporterName) || string.IsNullOrWhiteSpace(report.ReporterContact) || string.IsNullOrWhiteSpace(report.Description) || !Enum.IsDefined(report.Priority))
        {
            return Error.Validation("Svc.Report", "A fault report needs the reporter, a contact, a description and a priority.");
        }

        string key = (report.CategoryCode ?? string.Empty).Trim().ToUpperInvariant();
        TicketCategory? category = await _db.Set<TicketCategory>().AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == report.CompanyId && c.Code == key, ct).ConfigureAwait(false);
        if (category is null)
        {
            return Error.NotFound("Svc.CategoryNotFound", $"Fault category {key} not found.");
        }

        long? propertyId = null;
        if (!string.IsNullOrWhiteSpace(report.StandNumber))
        {
            string stand = report.StandNumber.Trim();
            propertyId = await _db.Properties.Where(p => p.CompanyId == report.CompanyId && p.StandNumber == stand).Select(p => (long?)p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (propertyId is null)
            {
                return Error.NotFound("Svc.PropertyNotFound", $"No property {stand} on the municipal register.");
            }
        }

        // A fault in a council rental unit: the ticket follows the unit, and the unit's repair carries the job.
        long? unitId = null;
        string? unitCode = null;
        if (!string.IsNullOrWhiteSpace(report.RentalUnitCode))
        {
            unitCode = report.RentalUnitCode.Trim().ToUpperInvariant();
            string code = unitCode;
            unitId = await _db.Set<Core.Housing.RentalUnit>().Where(u => u.CompanyId == report.CompanyId && u.Code.ToUpper() == code).Select(u => (long?)u.Id).FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (unitId is null)
            {
                return Error.NotFound("Svc.RentalUnitNotFound", $"No rental unit {unitCode}.");
            }
        }

        Result<string> reference = await _numbers.AllocateAsync(report.CompanyId, "FLT", DateOnly.FromDateTime(report.ReportedAt.UtcDateTime), ct: ct).ConfigureAwait(false);
        if (reference.IsFailure)
        {
            return reference.Error;
        }

        ServiceTicket ticket = new(report.CompanyId, reference.Value, category.Id, propertyId, report.Priority, report.ReporterName.Trim(), report.ReporterContact.Trim(),
            report.Description.Trim(), report.ReportedAt, SlaClock.Due(report.ReportedAt, category.ResponseHours, report.Priority), SlaClock.Due(report.ReportedAt, category.ResolutionHours, report.Priority))
        {
            RentalUnitId = unitId,
        };
        if (unitCode is not null && _maintenance is not null)
        {
            Result<string> repair = await _maintenance.ReportRepairAsync(report.CompanyId, unitCode, DateOnly.FromDateTime(report.ReportedAt.UtcDateTime),
                $"{reference.Value}: {report.Description.Trim()}", ct).ConfigureAwait(false);
            if (repair.IsFailure)
            {
                return repair.Error;
            }

            ticket.RepairNumber = repair.Value;
        }

        _db.Set<ServiceTicket>().Add(ticket);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return reference.Value;
    }

    /// <inheritdoc />
    public Task<Result> AssignAsync(long companyId, string reference, long userId, DateTimeOffset at, string? note, CancellationToken ct = default)
        => _db.AtomicAsync(() => AssignCoreAsync(companyId, reference, userId, at, note, ct), ct);

    private async Task<Result> AssignCoreAsync(long companyId, string reference, long userId, DateTimeOffset at, string? note, CancellationToken ct)
    {
        Result<ServiceTicket> found = await FindAsync(companyId, reference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        ServiceTicket ticket = found.Value;
        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return Result.Failure(Error.Rule("Svc.TicketDone", $"{ticket.Reference} is {ticket.Status}; reopen it first."));
        }

        if (at < ticket.ReportedAt)
        {
            return Result.Failure(Error.Validation("Svc.TicketTime", "A ticket cannot be assigned before it was reported."));
        }

        ticket.Assign(userId, at, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        string category = await _db.Set<TicketCategory>().Where(c => c.Id == ticket.CategoryId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        Result<long> notified = await _notifications.NotifyUserAsync(companyId, userId, $"Fault {ticket.Reference} assigned to you",
            $"{category} ({ticket.Priority}): {ticket.Description}. Resolve by {ticket.ResolutionDueAt:yyyy-MM-dd HH:mm}.", ct).ConfigureAwait(false);
        return notified.IsFailure ? Result.Failure(notified.Error) : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ResolveAsync(long companyId, string reference, string resolution, DateTimeOffset at, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            return Result.Failure(Error.Validation("Svc.Resolution", "Record how the fault was resolved."));
        }

        Result<ServiceTicket> found = await FindAsync(companyId, reference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Status != TicketStatus.Assigned)
        {
            return Result.Failure(Error.Rule("Svc.NotAssigned", $"{found.Value.Reference} is {found.Value.Status}; only an assigned ticket can be resolved."));
        }

        found.Value.Resolve(resolution.Trim(), at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> CloseAsync(long companyId, string reference, DateTimeOffset at, CancellationToken ct = default)
    {
        Result<ServiceTicket> found = await FindAsync(companyId, reference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Status != TicketStatus.Resolved)
        {
            return Result.Failure(Error.Rule("Svc.NotResolved", $"{found.Value.Reference} is {found.Value.Status}; only a resolved ticket can be closed."));
        }

        found.Value.Close(at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ReopenAsync(long companyId, string reference, string reason, DateTimeOffset at, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Svc.ReopenReason", "Say why the ticket is reopened."));
        }

        Result<ServiceTicket> found = await FindAsync(companyId, reference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Status != TicketStatus.Resolved)
        {
            return Result.Failure(Error.Rule("Svc.NotResolved", $"{found.Value.Reference} is {found.Value.Status}; only a resolved ticket can be reopened."));
        }

        found.Value.Reopen(reason.Trim(), at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<PublicTicketStatus>> PublicStatusAsync(long companyId, string reference, CancellationToken ct = default)
    {
        string key = (reference ?? string.Empty).Trim().ToUpperInvariant();
        var ticket = await (
            from t in _db.Set<ServiceTicket>().AsNoTracking()
            join c in _db.Set<TicketCategory>().AsNoTracking() on t.CategoryId equals c.Id
            where t.CompanyId == companyId && t.Reference == key
            select new PublicTicketStatus(t.Reference, c.Name, t.Status, t.ReportedAt, t.ResolutionDueAt, t.ResolvedAt)).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Error.NotFound("Svc.TicketNotFound", $"No fault report {key}.");
        }

        return ticket;
    }

    /// <inheritdoc />
    public async Task<SlaBoard> SlaBoardAsync(long companyId, DateTimeOffset now, CancellationToken ct = default)
    {
        List<ServiceTicket> tickets = await _db.Set<ServiceTicket>().AsNoTracking().Include(t => t.Events)
            .Where(t => t.CompanyId == companyId && t.ReportedAt <= now).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> categories = await _db.Set<TicketCategory>().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);

        List<(TicketSla Sla, double HoursToResolve)> rows = [];
        foreach (ServiceTicket ticket in tickets)
        {
            // Rebuilt from the history as it stood at the moment asked about.
            List<TicketEvent> history = [.. ticket.Events.Where(e => e.At <= now).OrderBy(e => e.At).ThenBy(e => e.Id)];
            TicketStatus status = history.Count == 0 ? TicketStatus.Open : history[^1].Status;
            DateTimeOffset? responded = history.FirstOrDefault(e => e.Status == TicketStatus.Assigned)?.At;
            DateTimeOffset? resolved = status is TicketStatus.Resolved or TicketStatus.Closed ? history.Last(e => e.Status == TicketStatus.Resolved).At : null;
            TicketSla sla = new(ticket.Reference, categories[ticket.CategoryId], ticket.Priority, status,
                SlaClock.State(ticket.ReportedAt, ticket.ResponseDueAt, responded, now), SlaClock.State(ticket.ReportedAt, ticket.ResolutionDueAt, resolved, now));
            rows.Add((sla, resolved is DateTimeOffset r ? (r - ticket.ReportedAt).TotalHours : -1));
        }

        List<double> resolvedHours = [.. rows.Where(r => r.HoursToResolve >= 0).Select(r => r.HoursToResolve)];
        return new SlaBoard(
            [.. rows.Select(r => r.Sla).OrderByDescending(s => s.Resolution).ThenByDescending(s => s.Priority).ThenBy(s => s.Reference, StringComparer.Ordinal)],
            rows.Count(r => r.Sla.Resolution == SlaState.Breached),
            resolvedHours.Count == 0 ? 0m : Math.Round((decimal)resolvedHours.Average(), 1, MidpointRounding.AwayFromZero));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> EscalateBreachesAsync(long companyId, DateTimeOffset now, CancellationToken ct = default)
    {
        List<ServiceTicket> overdue = await _db.Set<ServiceTicket>().Include(t => t.Events)
            .Where(t => t.CompanyId == companyId && t.EscalatedAt == null && t.ResolutionDueAt < now && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .OrderBy(t => t.ResolutionDueAt).ToListAsync(ct).ConfigureAwait(false);
        foreach (ServiceTicket ticket in overdue)
        {
            ticket.Escalate(now);
            if (ticket.AssignedToUserId is long officer)
            {
                await _notifications.NotifyUserAsync(companyId, officer, $"Fault {ticket.Reference} escalated", $"{ticket.Description} was due by {ticket.ResolutionDueAt:yyyy-MM-dd HH:mm} and is now {ticket.Priority}.", ct)
                    .ConfigureAwait(false);
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return [.. overdue.Select(t => t.Reference)];
    }

    private async Task<Result<ServiceTicket>> FindAsync(long companyId, string reference, CancellationToken ct)
    {
        string key = (reference ?? string.Empty).Trim().ToUpperInvariant();
        ServiceTicket? ticket = await _db.Set<ServiceTicket>().Include(t => t.Events).FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Reference == key, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Error.NotFound("Svc.TicketNotFound", $"No fault report {key}.");
        }

        return ticket;
    }
}

/// <summary>Built-in task: escalate the fault tickets past their resolution deadline (brief §4.9 SLA escalation).</summary>
public sealed class ServiceDeskEscalationTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly IServiceDeskService _desk;

    /// <summary>Creates the task.</summary>
    /// <param name="desk">The service desk.</param>
    public ServiceDeskEscalationTask(IServiceDeskService desk) => _desk = desk;

    /// <inheritdoc />
    public string Key => "servicedesk.escalate";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        IReadOnlyList<string> escalated = await _desk.EscalateBreachesAsync(companyId, runAt, ct).ConfigureAwait(false);
        return escalated.Count == 0 ? "No fault tickets are past their resolution deadline." : $"{escalated.Count} ticket(s) escalated: {string.Join(", ", escalated)}.";
    }
}
