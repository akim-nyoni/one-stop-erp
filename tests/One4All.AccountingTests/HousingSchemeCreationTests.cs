using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers creating a housing scheme: a code and name are required and a code in use is refused.</summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingSchemeCreationTests
{
    private readonly AccountingFixture _fixture;

    public HousingSchemeCreationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Schemes_are_created_once_per_code()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();

        (await housing.CreateSchemeAsync(companyId, " ", "Nameless", SchemeType.CouncilDelivered)).Error.Code.Should().Be("Hse.Scheme");
        long id = (await housing.CreateSchemeAsync(companyId, "hs-east", "Eastview Phase 1", SchemeType.CouncilDelivered)).Value;
        (await housing.CreateSchemeAsync(companyId, "HS-EAST", "Again", SchemeType.Cooperative)).Error.Code.Should().Be("Hse.SchemeExists");
        HousingScheme scheme = await db.HousingSchemes.AsNoTracking().SingleAsync(s => s.Id == id);
        (scheme.Code, scheme.Name, scheme.SchemeType).Should().Be(("HS-EAST", "Eastview Phase 1", SchemeType.CouncilDelivered));
    }
}
