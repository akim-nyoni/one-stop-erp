using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ReportCatalogueSeederTests
{
    private readonly DatabaseFixture _fixture;

    public ReportCatalogueSeederTests(DatabaseFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Seeder_persists_the_rc_catalogue_and_is_idempotent()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<string> rcCodes = await db.ReportDefinitions
                .Where(r => r.Scope == ReportScope.System && r.Module == "Receipting")
                .Select(r => r.Code)
                .ToListAsync();

            rcCodes.Should().HaveCount(53);
            rcCodes.Should().Contain(["RC1", "RC12", "RC29", "RC35", "RC45", "RC53"]);
        }

        // A second run changes nothing (idempotent upsert by code).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            int changed = await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            changed.Should().Be(0);
        }
    }
}
