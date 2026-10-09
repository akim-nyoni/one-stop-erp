using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Municipal;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the quick search across the registers: properties (by stand or address) and students (by admission number or name) are found too, and names
/// match without their accents and with a letter wrong.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AccentInsensitiveSearchTests
{
    private readonly AccountingFixture _fixture;

    public AccentInsensitiveSearchTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Students_properties_and_traders_are_found_without_accents()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer guardian = new(companyId, "AI-G", "Chloé Moyo", Scenario.Usd);
        db.Customers.Add(guardian);
        await db.SaveChangesAsync();
        Student zoe = new(companyId, "AI-77", "Zoë Ndlovu", "F1", guardian.Id);
        Property stand = new(companyId, "AI-1042", "Ward 3", guardian.Id) { Address = "12 Rue Émile Zola" };
        db.Students.Add(zoe);
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        IReportingSnapshotService search = scope.ServiceProvider.GetRequiredService<IReportingSnapshotService>();
        (await search.SearchAsync(companyId, "zoe")).Should().Contain(h => h.Kind == "Student" && h.Id == zoe.Id);
        (await search.SearchAsync(companyId, "Ndlvu")).Should().Contain(h => h.Kind == "Student" && h.Id == zoe.Id, "a dropped letter still matches");
        (await search.SearchAsync(companyId, "AI-77")).First().Should().Match<QuickSearchHit>(h => h.Kind == "Student" && h.Score == 1.0);
        (await search.SearchAsync(companyId, "emile")).Should().Contain(h => h.Kind == "Property" && h.Id == stand.Id);
        (await search.SearchAsync(companyId, "AI-1042")).Should().Contain(h => h.Kind == "Property" && h.Name.Contains("Zola", StringComparison.Ordinal));
        (await search.SearchAsync(companyId, "chloe")).Should().Contain(h => h.Kind == "Customer" && h.Id == guardian.Id);
    }
}
