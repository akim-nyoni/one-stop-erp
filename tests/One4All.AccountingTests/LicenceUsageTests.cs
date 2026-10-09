using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers licence usage for the status screen: every counted limit is listed, counted across all companies.</summary>
[Collection(AccountingCollection.Name)]
public sealed class LicenceUsageTests
{
    private readonly AccountingFixture _fixture;

    public LicenceUsageTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Usage_counts_every_company()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer guardian = new(companyId, "LU-G", "Guardian", Scenario.Usd);
        db.Customers.Add(guardian);
        await db.SaveChangesAsync();
        db.Students.Add(new Student(companyId, "LU-1", "Usage Student", "F1", guardian.Id));
        await db.SaveChangesAsync();

        IReadOnlyList<LicenceUsage> usage = await scope.ServiceProvider.GetRequiredService<ILicenceUsageService>().UsageAsync();
        usage.Select(u => u.Key).Should().Contain(["companies", "warehouses", "pos_terminals", "properties", "stands", "waiting_list", "students", "employees"]);
        int companies = await db.Companies.IgnoreQueryFilters([One4AllDbContext.CompanyFilter]).CountAsync();
        usage.Single(u => u.Key == "companies").Used.Should().Be(companies);
        usage.Single(u => u.Key == "students").Used.Should().BeGreaterThanOrEqualTo(1);
        new LicenceUsage("students", null, 45, 50).PercentUsed.Should().Be(90m);
        new LicenceUsage("students", null, 45, null).PercentUsed.Should().BeNull();
    }
}
