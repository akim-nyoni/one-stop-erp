using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Crm;
using One4All.Core.Crm;
using One4All.Core.SalesOrders;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers CRM leads and activities: a lead is captured (a repeat flagged by its phone), worked, qualified and converted into an opportunity
/// with its customer, its activities following; the agenda shows what is due; a quotation link moves the opportunity to Proposal; the funnel
/// counts by source.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CrmLeadTests
{
    private readonly AccountingFixture _fixture;

    public CrmLeadTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Leads_become_opportunities_with_their_history()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        ICrmService crm = scope.ServiceProvider.GetRequiredService<ICrmService>();
        const long rep = 51;

        (await crm.CaptureLeadAsync(new NewLead(companyId, "Nobody", null, null, null, "web"))).Error.Code.Should().Be("Crm.LeadInvalid");
        long lead = (await crm.CaptureLeadAsync(new NewLead(companyId, "Tendai Moyo", "Moyo Builders", "Tendai@Moyo.example", "+263 77 123 4567", "Referral", rep))).Value;
        long repeat = (await crm.CaptureLeadAsync(new NewLead(companyId, "T. Moyo", null, null, "0771234567", "web"))).Value;
        long cold = (await crm.CaptureLeadAsync(new NewLead(companyId, "Cold Caller", null, "cold@example.test", null, "web"))).Value;
        (await db.Set<Lead>().AsNoTracking().SingleAsync(l => l.Id == repeat)).PossibleDuplicateOfId.Should().Be(lead);

        DateTimeOffset tomorrow = DateTimeOffset.UtcNow.AddDays(1);
        long call = (await crm.LogActivityAsync(new NewActivity(companyId, ActivityType.Call, "Discovery call", rep, tomorrow, LeadId: lead))).Value;
        (await crm.LogActivityAsync(new NewActivity(companyId, ActivityType.Note, "Orphan", rep))).Error.Code.Should().Be("Crm.ActivityInvalid");
        (await crm.AgendaAsync(companyId, rep, DateTimeOffset.UtcNow.AddDays(2))).Should().ContainSingle(a => a.Id == call);

        (await crm.ConvertLeadAsync(companyId, lead, "Depot fit-out", 50_000m, null, null)).Error.Code.Should().Be("Crm.NotQualified");
        (await crm.AdvanceLeadAsync(companyId, lead, LeadStatus.Contacted)).IsSuccess.Should().BeTrue();
        (await crm.CompleteActivityAsync(companyId, call, "Wants a quote for 3 sites")).IsSuccess.Should().BeTrue();
        (await crm.AdvanceLeadAsync(companyId, lead, LeadStatus.Qualified)).IsSuccess.Should().BeTrue();
        long opportunity = (await crm.ConvertLeadAsync(companyId, lead, "Depot fit-out", 50_000m, "CRM-MOYO", Scenario.Usd)).Value;
        (await crm.DisqualifyLeadAsync(companyId, lead, "too late")).Error.Code.Should().Be("Crm.LeadStage");
        (await crm.DisqualifyLeadAsync(companyId, cold, "No budget")).IsSuccess.Should().BeTrue();

        Opportunity opp = await db.Opportunities.AsNoTracking().SingleAsync(o => o.Id == opportunity);
        opp.Stage.Should().Be(OpportunityStage.Qualified);
        long customer = opp.CustomerId!.Value;
        (await crm.TimelineAsync(companyId, null, opportunity, null)).Should().ContainSingle(a => a.Id == call && a.Notes!.Contains("3 sites") && a.CustomerId == customer);

        // A quotation for the same customer moves it to Proposal.
        Quotation quote = new(companyId, customer, "CRM-Q1", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), Scenario.Usd);
        db.Quotations.Add(quote);
        await db.SaveChangesAsync();
        (await crm.LinkQuotationAsync(companyId, opportunity, quote.Id)).IsSuccess.Should().BeTrue();
        (await db.Opportunities.AsNoTracking().SingleAsync(o => o.Id == opportunity)).Should().Match<Opportunity>(o => o.Stage == OpportunityStage.Proposal && o.QuotationId == quote.Id);

        IReadOnlyList<LeadFunnelLine> funnel = await crm.LeadFunnelAsync(companyId);
        funnel.Should().ContainSingle(f => f.Source == "referral" && f.Leads == 1 && f.Converted == 1 && f.ConversionPercent == 100m);
        funnel.Should().ContainSingle(f => f.Source == "web" && f.Leads == 2 && f.Disqualified == 1 && f.Converted == 0);
    }
}
