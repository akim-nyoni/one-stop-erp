using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>A new installation starts with US dollars, Zimbabwe Gold and South African rand, and seeding again changes nothing.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CurrencySeederTests
{
    private readonly AccountingFixture _fixture;

    public CurrencySeederTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Default_currencies_are_seeded_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        CurrencySeeder seeder = scope.ServiceProvider.GetRequiredService<CurrencySeeder>();

        await seeder.SeedAsync();
        (await seeder.SeedAsync()).Should().Be(0);
        List<string> codes = [.. (await db.Currencies.AsNoTracking().Select(c => c.Code).ToListAsync()).Select(c => c.Value)];
        codes.Should().Contain(["USD", "ZWG", "ZAR"]);
    }
}
