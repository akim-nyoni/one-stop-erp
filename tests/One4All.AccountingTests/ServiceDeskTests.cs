using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the municipal service desk: fault categories with targets; a critical burst pipe logged against a property with
/// a public reference and shortened SLA; assignment notifying the officer in-app; resolution within target; a refuse
/// complaint responded to late and left unresolved; the public status; close, reopen and the history; and the SLA board
/// as at different moments (at risk, breached, average time to resolve).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ServiceDeskTests
{
    private readonly AccountingFixture _fixture;

    public ServiceDeskTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Faults_are_logged_assigned_resolved_and_measured_against_their_sla()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer ratepayer = new(companyId, "SD-RP", "Ratepayer", Scenario.Usd);
        db.Customers.Add(ratepayer);
        User officer = new($"sd-officer-{companyId}", "Water Foreman");
        db.Users.Add(officer);
        await db.SaveChangesAsync();
        db.Properties.Add(new Property(companyId, "SD-1", "Ward 4", ratepayer.Id));
        await db.SaveChangesAsync();

        IServiceDeskService desk = scope.ServiceProvider.GetRequiredService<IServiceDeskService>();
        (await desk.DefineCategoryAsync(companyId, "BAD", "Bad targets", null, 12m, 2m)).Error.Code.Should().Be("Svc.Category");
        (await desk.DefineCategoryAsync(companyId, "burst", "Burst pipe", ServiceType.Water, 2m, 12m)).IsSuccess.Should().BeTrue();
        (await desk.DefineCategoryAsync(companyId, "REFUSE", "Refuse not collected", ServiceType.Refuse, 24m, 72m)).IsSuccess.Should().BeTrue();

        DateTimeOffset eight = new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);
        FaultReport Report(string category, TicketPriority priority, string? stand, string description) => new()
        {
            CompanyId = companyId, CategoryCode = category, StandNumber = stand, Priority = priority, ReporterName = "Mrs Ncube", ReporterContact = "+263771234567",
            Description = description, ReportedAt = eight,
        };
        (await desk.LogAsync(Report("BURST", TicketPriority.Critical, "NOPE-9", "Water everywhere"))).Error.Code.Should().Be("Svc.PropertyNotFound");
        (await desk.LogAsync(Report("FLOOD", TicketPriority.Normal, null, "?"))).Error.Code.Should().Be("Svc.CategoryNotFound");
        string burst = (await desk.LogAsync(Report("burst", TicketPriority.Critical, "SD-1", "Burst main outside SD-1"))).Value;
        string refuse = (await desk.LogAsync(Report("REFUSE", TicketPriority.Normal, null, "Bins not collected on 3rd Street"))).Value;
        burst.Should().StartWith("FLT");

        // The burst: assigned at 08:20 (the foreman is notified), fixed at 10:30 - inside the 3-hour critical target.
        (await desk.ResolveAsync(companyId, burst, "Clamp fitted", eight.AddHours(1))).Error.Code.Should().Be("Svc.NotAssigned");
        (await desk.AssignAsync(companyId, burst, officer.Id, eight.AddMinutes(20), "Crew 3 dispatched")).IsSuccess.Should().BeTrue();
        IReadOnlyList<NotificationEntry> inbox = await scope.ServiceProvider.GetRequiredService<INotificationService>().InboxAsync(companyId, officer.Id, unreadOnly: true);
        inbox.Should().ContainSingle().Which.Title.Should().Be($"Fault {burst} assigned to you");

        SlaBoard tense = await desk.SlaBoardAsync(companyId, eight.AddMinutes(145));
        tense.Tickets.Single(t => t.Reference == burst).Should().Be(new TicketSla(burst, "Burst pipe", TicketPriority.Critical, TicketStatus.Assigned, SlaState.Met, SlaState.AtRisk));
        tense.Tickets.Single(t => t.Reference == refuse).Should().Be(new TicketSla(refuse, "Refuse not collected", TicketPriority.Normal, TicketStatus.Open, SlaState.OnTrack, SlaState.OnTrack));

        (await desk.ResolveAsync(companyId, burst, "Clamp fitted to the main", eight.AddMinutes(150))).IsSuccess.Should().BeTrue();
        PublicTicketStatus seen = (await desk.PublicStatusAsync(companyId, burst.ToLowerInvariant())).Value;
        (seen.Category, seen.Status, seen.ResolutionDueAt, seen.ResolvedAt).Should().Be(("Burst pipe", TicketStatus.Resolved, eight.AddHours(3), (DateTimeOffset?)eight.AddMinutes(150)));

        // Reopened when the leak returns, fixed again, then closed; a closed ticket cannot be reopened.
        (await desk.ReopenAsync(companyId, burst, "Still leaking", eight.AddMinutes(160))).IsSuccess.Should().BeTrue();
        (await desk.PublicStatusAsync(companyId, burst)).Value.Status.Should().Be(TicketStatus.Assigned);
        (await desk.ResolveAsync(companyId, burst, "Section of pipe replaced", eight.AddMinutes(170))).IsSuccess.Should().BeTrue();
        (await desk.CloseAsync(companyId, burst, eight.AddHours(4))).IsSuccess.Should().BeTrue();
        (await desk.ReopenAsync(companyId, burst, "Again", eight.AddHours(5))).Error.Code.Should().Be("Svc.NotResolved");
        (await desk.AssignAsync(companyId, burst, officer.Id, eight.AddHours(5), null)).Error.Code.Should().Be("Svc.TicketDone");

        // Refuse: responded to a day late and still open three days on.
        (await desk.AssignAsync(companyId, refuse, officer.Id, eight.AddHours(25), null)).IsSuccess.Should().BeTrue();
        SlaBoard later = await desk.SlaBoardAsync(companyId, eight.AddDays(3).AddHours(1));
        later.Tickets.Select(t => (t.Reference, t.Response, t.Resolution)).Should().Equal((refuse, SlaState.Breached, SlaState.Breached), (burst, SlaState.Met, SlaState.Met));
        (later.ResolutionBreaches, later.AverageHoursToResolve).Should().Be((1, 2.8m)); // resolved 2 h 50 after the report
    }
}
