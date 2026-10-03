using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the audit trail service: querying a company's change log by entity and action, verifying an intact
/// chain, and detecting (and locating) a historical row altered by direct SQL.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AuditTrailTests
{
    private readonly AccountingFixture _fixture;

    public AuditTrailTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Audit_trail_queries_and_detects_tampering()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "AUD-C", "Audited Buyer", Scenario.Usd) { CreditLimit = 1_000m };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            customer.CreditLimit = 2_500m;
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAuditService audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

            IReadOnlyList<AuditEntryView> customerTrail = await audit.QueryAsync(new AuditQuery { CompanyId = companyId, EntityType = "Customer", EntityId = customerId.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            customerTrail.Should().HaveCount(2);
            customerTrail[0].Action.Should().Be(AuditAction.Update); // newest first
            customerTrail[0].ChangedFields.Should().Contain("CreditLimit");
            customerTrail[0].OldValues.Should().Contain("1000");
            customerTrail[0].NewValues.Should().Contain("2500");
            customerTrail[1].Action.Should().Be(AuditAction.Insert);

            (await audit.QueryAsync(new AuditQuery { CompanyId = companyId, EntityType = "Customer", Action = AuditAction.Update })).Should().ContainSingle();
            (await audit.QueryAsync(new AuditQuery { CompanyId = companyId, Take = 3 })).Should().HaveCount(3);

            AuditChainVerification intact = await audit.VerifyChainAsync(companyId);
            intact.IsIntact.Should().BeTrue(intact.Message);
            intact.EntriesChecked.Should().BeGreaterThan(2);
            intact.FirstBrokenEntryId.Should().BeNull();
        }

        long tamperedId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Someone with database access quietly rewrites history: the credit-limit change now says 25,000.
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            tamperedId = await db.ChangeLog
                .Where(c => c.CompanyId == companyId && c.EntityType == "Customer" && c.Action == AuditAction.Update)
                .Select(c => c.Id).SingleAsync();
            int rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE audit.change_log SET new_values = REPLACE(new_values, '2500', '25000') WHERE id = {tamperedId}");
            rows.Should().Be(1);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            AuditChainVerification broken = await scope.ServiceProvider.GetRequiredService<IAuditService>().VerifyChainAsync(companyId);
            broken.IsIntact.Should().BeFalse();
            broken.FirstBrokenEntryId.Should().Be(tamperedId);
            broken.Message.Should().Contain(tamperedId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
