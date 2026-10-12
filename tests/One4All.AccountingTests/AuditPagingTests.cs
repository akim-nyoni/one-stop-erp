using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers keyset paging through the audit trail: pages follow one another newest first, without overlap, until the log runs out.</summary>
[Collection(AccountingCollection.Name)]
public sealed class AuditPagingTests
{
    private readonly AccountingFixture _fixture;

    public AuditPagingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_audit_trail_pages_through_the_database()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        for (int i = 0; i < 5; i++)
        {
            db.Customers.Add(new Customer(companyId, $"APG-{i}", $"Paged {i}", Scenario.Usd));
            await db.SaveChangesAsync();
        }

        IAuditService audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        AuditQuery customers = new() { CompanyId = companyId, EntityType = nameof(Customer), Take = 2 };
        List<long> seen = [];
        long? before = null;
        for (int page = 0; page < 10; page++)
        {
            IReadOnlyList<AuditEntryView> entries = await audit.QueryAsync(customers with { BeforeId = before });
            if (entries.Count == 0)
            {
                break;
            }

            entries.Count.Should().BeLessThanOrEqualTo(2);
            entries.Select(e => e.Id).Should().BeInDescendingOrder();
            seen.AddRange(entries.Select(e => e.Id));
            before = entries[^1].Id;
        }

        seen.Should().HaveCount(5).And.OnlyHaveUniqueItems().And.BeInDescendingOrder("newest first, page after page");
    }
}
