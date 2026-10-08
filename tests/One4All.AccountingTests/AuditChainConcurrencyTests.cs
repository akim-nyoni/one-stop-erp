using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the audit hash chain under concurrent writers (brief §6): many transactions recording events for one company at the same moment each
/// link to the entry before them, so the chain still verifies — without the chain lock, two of them would link to the same previous hash.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AuditChainConcurrencyTests
{
    private readonly AccountingFixture _fixture;

    public AuditChainConcurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Concurrent_writers_keep_the_chain_intact()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long company;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            company = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await Task.WhenAll(Enumerable.Range(0, 24).Select(async n =>
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            var recorded = await scope.ServiceProvider.GetRequiredService<IAuditService>()
                .RecordAsync(new AuditEvent(company, 1, "tester", "GL", "Report", n.ToString(System.Globalization.CultureInfo.InvariantCulture), AuditAction.Print));
            recorded.IsSuccess.Should().BeTrue();
        }));

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            AuditChainVerification verified = await scope.ServiceProvider.GetRequiredService<IAuditService>().VerifyChainAsync(company);
            verified.IsIntact.Should().BeTrue(verified.Message);
            verified.EntriesChecked.Should().BeGreaterThanOrEqualTo(24);
        }
    }
}
