using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Scheduling;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #16: the compliance runs are scheduled tasks - each is registered under its module and runs cleanly on a company with nothing due.</summary>
[Collection(AccountingCollection.Name)]
public sealed class ComplianceTasksTests
{
    private static readonly string[] Keys =
        ["ar.arrangements", "housing.instalment-defaults", "housing.covenant-breaches", "municipal.indigent-reviews", "municipal.licence-renewals", "schools.bursary-expiry"];

    private readonly AccountingFixture _fixture;

    public ComplianceTasksTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_compliance_runs_are_schedulable_and_run_with_nothing_due()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        long companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        scope.ServiceProvider.GetRequiredService<IJobScheduler>().TaskKeys.Should().Contain(Keys);
        foreach (IScheduledTask task in scope.ServiceProvider.GetServices<IScheduledTask>().Where(t => Keys.Contains(t.Key)))
        {
            Core.Common.Result<string> run = await task.RunAsync(companyId, new DateTimeOffset(2026, 6, 15, 2, 0, 0, TimeSpan.Zero));
            run.IsSuccess.Should().BeTrue(run.IsFailure ? $"{task.Key}: {run.Error}" : null);
            run.Value.Should().NotBeNullOrWhiteSpace();
        }
    }
}
