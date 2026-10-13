using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Security;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the approval SLA report (PO137): a document approved three hours after it started counts against a one-hour stage as a breach,
/// and a document still waiting at that stage is counted with its wait.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ApprovalSlaReportTests
{
    private readonly AccountingFixture _fixture;

    public ApprovalSlaReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stage_turnaround_is_measured_against_its_escalation_time()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        string tag = Guid.NewGuid().ToString("N")[..8];
        User clerk = new($"slac{tag}", "Clerk"), boss = new($"slab{tag}", "Boss");
        db.AddRange(clerk, boss);
        await db.SaveChangesAsync();
        string type = $"SLA{tag}";
        WorkflowDefinition definition = new(companyId, type, type);
        WorkflowStage stage = definition.AddStage(1, "Boss");
        stage.AddEligibleUser(boss.Id);
        stage.EscalateAfterHours = 1;
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        long late = (await workflow.StartAsync(companyId, type, "SLA-1", 100m, clerk.Id)).Value.InstanceId;
        long waiting = (await workflow.StartAsync(companyId, type, "SLA-2", 100m, clerk.Id)).Value.InstanceId;
        DateTimeOffset threeHoursAgo = DateTimeOffset.UtcNow.AddHours(-3);
        await db.Set<WorkflowInstance>().Where(w => w.Id == late).ExecuteUpdateAsync(s => s.SetProperty(w => w.CreatedOnUtc, threeHoursAgo));
        db.ChangeTracker.Clear();
        (await workflow.ActAsync(late, boss.Id, WorkflowActionType.Approve, null)).IsSuccess.Should().BeTrue();

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "PO137",
            new Dictionary<string, object?> { ["FromDate"] = today.AddDays(-1), ["ToDate"] = today.AddDays(1) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string row = Encoding.UTF8.GetString(rendered.Value.Content).Split('\n').Single(l => l.StartsWith(type, StringComparison.Ordinal));
        string[] cells = row.TrimEnd('\r').Split(',');
        cells[1].Should().Be("1");
        cells[2].Should().Be("1", "one decision in the period");
        decimal.Parse(cells[3], System.Globalization.CultureInfo.InvariantCulture).Should().BeApproximately(3m, 0.2m);
        cells[5].Should().Be("1", "the stage escalates after an hour");
        cells[6].Should().Be("1", "the decision took longer than that");
        cells[7].Should().Be("1", "one document is still waiting");
        waiting.Should().BePositive();
    }
}
