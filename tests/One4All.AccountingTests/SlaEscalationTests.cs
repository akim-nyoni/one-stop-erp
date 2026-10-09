using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Municipal;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the SLA escalation job: tickets past their resolution deadline go up a priority once, with a note; resolved ones are left alone.</summary>
[Collection(AccountingCollection.Name)]
public sealed class SlaEscalationTests
{
    private readonly AccountingFixture _fixture;

    public SlaEscalationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Overdue_tickets_are_escalated_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        User officer = new($"sla-officer-{companyId}", "Foreman");
        db.Users.Add(officer);
        await db.SaveChangesAsync();
        IServiceDeskService desk = scope.ServiceProvider.GetRequiredService<IServiceDeskService>();
        (await desk.DefineCategoryAsync(companyId, "SLA-LEAK", "Leak", ServiceType.Water, 2m, 12m)).IsSuccess.Should().BeTrue();
        DateTimeOffset eight = new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);
        async Task<string> Log(TicketPriority priority, string what) => (await desk.LogAsync(new FaultReport
        {
            CompanyId = companyId, CategoryCode = "SLA-LEAK", Priority = priority, ReporterName = "Mr Moyo", ReporterContact = "+263771000000", Description = what, ReportedAt = eight,
        })).Value;
        string slow = await Log(TicketPriority.Normal, "Dripping standpipe"), fixedQuickly = await Log(TicketPriority.High, "Leak at the clinic"), top = await Log(TicketPriority.Critical, "Main burst");
        (await desk.AssignAsync(companyId, slow, officer.Id, eight.AddHours(1), null)).IsSuccess.Should().BeTrue();
        (await desk.AssignAsync(companyId, fixedQuickly, officer.Id, eight.AddHours(1), null)).IsSuccess.Should().BeTrue();
        (await desk.ResolveAsync(companyId, fixedQuickly, "Washer replaced", eight.AddHours(3))).IsSuccess.Should().BeTrue();

        IScheduledTask job = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Key == "servicedesk.escalate");
        (await job.RunAsync(companyId, eight.AddHours(2))).Value.Should().Contain("No fault tickets");
        (await job.RunAsync(companyId, eight.AddHours(11))).Value.Should().Contain($"1 ticket(s) escalated: {top}", "a critical fault has a quarter of the category's 12 hours");
        (await job.RunAsync(companyId, eight.AddHours(13))).Value.Should().Contain($"1 ticket(s) escalated: {slow}");
        (await job.RunAsync(companyId, eight.AddHours(14))).Value.Should().Contain("No fault tickets", "each ticket is escalated once");

        Dictionary<string, ServiceTicket> tickets = await db.Set<ServiceTicket>().AsNoTracking().Include(t => t.Events).Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Reference);
        (tickets[slow].Priority, tickets[slow].EscalatedAt).Should().Be((TicketPriority.High, (DateTimeOffset?)eight.AddHours(13)));
        tickets[slow].Events.OrderBy(e => e.At).ThenBy(e => e.Id).Last().Note.Should().StartWith("Escalated: resolution overdue");
        tickets[top].Priority.Should().Be(TicketPriority.Critical, "there is nothing above critical");
        tickets[fixedQuickly].EscalatedAt.Should().BeNull();
    }
}
