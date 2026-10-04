using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Communications;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers scheduled report delivery: a trial balance subscription with a relative date is rendered at each run with the
/// date resolved, attached to one email per recipient and logged; the despatch hands the attachment to the gateway;
/// bad tokens, unknown reports and recipients without an address are refused; and the scheduled task runs the active
/// subscriptions.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportSubscriptionTests
{
    private readonly AccountingFixture _fixture;

    public ReportSubscriptionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Subscribed_reports_are_rendered_attached_emailed_and_logged()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        CapturingEmailGateway email = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(services => services.AddSingleton<IMessageGateway>(email));

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

        IReportSubscriptionService subscriptions = scope.ServiceProvider.GetRequiredService<IReportSubscriptionService>();
        SubscriptionRequest Request(string code, string report, IReadOnlyDictionary<string, string> parameters, params string[] recipients) => new()
        {
            CompanyId = companyId, Code = code, ReportCode = report, Format = ReportFormat.Html, Parameters = parameters, Recipients = recipients,
        };
        Dictionary<string, string> monthEnd = new() { ["AsAtDate"] = "@PrevMonthEnd" };
        (await subscriptions.SubscribeAsync(Request("BAD", SystemReports.TrialBalanceCode, new Dictionary<string, string> { ["AsAtDate"] = "@LastTuesday" }, "cfo@council.example"))).Error.Code.Should().Be("Rpt.Token");
        (await subscriptions.SubscribeAsync(Request("NOPE", "NO-SUCH-REPORT", monthEnd, "cfo@council.example"))).Error.Code.Should().Be("Rpt.ReportNotFound");
        (await subscriptions.SubscribeAsync(Request("NOBODY", SystemReports.TrialBalanceCode, monthEnd, "not-an-address"))).Error.Code.Should().Be("Rpt.Subscription");
        (await subscriptions.SubscribeAsync(Request("tb-month", SystemReports.TrialBalanceCode, monthEnd, "cfo@council.example", "Audit@Council.example", "audit@council.example"))).IsSuccess.Should().BeTrue();

        // Run on 3 June: the trial balance as at 31 May, one email per (distinct) recipient, each carrying the document.
        DateTimeOffset june = new(2026, 6, 3, 6, 0, 0, TimeSpan.Zero);
        (await subscriptions.RunAsync(companyId, "TB-MONTH", june)).Value.Should().Be(2);
        IReadOnlyList<DeliveryLogEntry> log = (await subscriptions.DeliveryLogAsync(companyId, "tb-month")).Value;
        log.Select(l => (l.Recipient, l.Succeeded)).Should().Equal(("cfo@council.example", true), ("Audit@Council.example", true));
        List<OutboundMessage> queued = await db.Set<OutboundMessage>().AsNoTracking().Where(m => m.CompanyId == companyId).OrderBy(m => m.Id).ToListAsync();
        queued.Should().HaveCount(2).And.OnlyContain(m => m.AttachmentName == log[0].FileName && m.AttachmentContent!.Length > 0 && m.Body.Contains("AsAtDate = 2026-05-31"));
        Encoding.UTF8.GetString(queued[0].AttachmentContent!).Should().Contain("Trial Balance as at 2026-05-31");

        (await scope.ServiceProvider.GetRequiredService<INotificationService>().DespatchAsync(companyId, 3)).Sent.Should().Be(2);
        email.Sent.Should().HaveCount(2).And.OnlyContain(s => s.Attachment != null && s.Attachment.ContentType.StartsWith("text/html", StringComparison.Ordinal));

        // The scheduled task runs active subscriptions only.
        (await subscriptions.SetActiveAsync(companyId, "TB-MONTH", false)).IsSuccess.Should().BeTrue();
        IJobScheduler jobs = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        (await jobs.DefineAsync(new JobDefinition { CompanyId = companyId, Code = "REPORTS", TaskKey = "reports.subscriptions", Frequency = JobFrequency.Monthly, DayOfMonth = 3, TimeOfDay = new TimeOnly(6, 0), From = june.AddDays(-5) })).IsSuccess.Should().BeTrue();
        (await jobs.RunNowAsync(companyId, "REPORTS", june)).Value.Message.Should().Be("0 report subscriptions run, 0 failed.");
        (await subscriptions.SetActiveAsync(companyId, "TB-MONTH", true)).IsSuccess.Should().BeTrue();
        (await jobs.RunNowAsync(companyId, "REPORTS", june.AddMonths(1))).Value.Message.Should().Be("1 report subscriptions run, 0 failed.");
        (await subscriptions.DeliveryLogAsync(companyId, "TB-MONTH")).Value.Should().HaveCount(4);
    }

    private sealed class CapturingEmailGateway : IMessageGateway
    {
        public List<(string To, MessageAttachment? Attachment)> Sent { get; } = [];

        public MessageChannel Channel => MessageChannel.Email;

        public Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default)
        {
            Sent.Add((recipient, null));
            return Task.FromResult(new GatewayResult(true, $"E{Sent.Count}"));
        }

        public Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, MessageAttachment attachment, CancellationToken ct = default)
        {
            Sent.Add((recipient, attachment));
            return Task.FromResult(new GatewayResult(true, $"E{Sent.Count}"));
        }
    }
}
