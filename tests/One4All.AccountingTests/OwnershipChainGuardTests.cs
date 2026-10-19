using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Application.Abstractions.Security;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #20: the database keeps the chain of ownership append-only and records each link added.</summary>
[Collection(AccountingCollection.Name)]
public sealed class OwnershipChainGuardTests
{
    private readonly AccountingFixture _fixture;

    public OwnershipChainGuardTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Ownership_links_cannot_be_changed_or_removed_in_the_database()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        LandParcel parcel = new(companyId, $"OCG-{companyId}", "Township", 10_000m, 50_000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand stand = new(companyId, parcel.Id, null, $"OCG-S-{companyId}", 300m, 2_000m, new DateOnly(2026, 1, 1));
        db.HousingStands.Add(stand);
        await db.SaveChangesAsync();
        long link = await db.Set<OwnershipRecord>().AsNoTracking().Where(o => o.HousingStandId == stand.Id).Select(o => o.Id).SingleAsync();

        NpgsqlConnectionStringBuilder direct = new(_fixture.ConnectionString) { ApplicationName = "psql" };
        await using (NpgsqlConnection connection = new(direct.ConnectionString))
        {
            await connection.OpenAsync();
            foreach (string sql in new[] { $"UPDATE hse.ownership_record SET holder_name = 'Mallory' WHERE id = {link}", $"DELETE FROM hse.ownership_record WHERE id = {link}" })
            {
                await using NpgsqlCommand tamper = new(sql, connection);
                Func<Task> act = () => tamper.ExecuteNonQueryAsync();
                (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");
            }
        }

        // The application adds links as before, and each is in the database change log.
        HousingStand tracked = await db.HousingStands.Include(s => s.Ownership).SingleAsync(s => s.Id == stand.Id);
        tracked.ImportOwnership(OwnershipChangeType.Sale, null, "Buyer", new DateOnly(2026, 2, 1), 2_500m);
        await db.SaveChangesAsync();
        (await db.Set<OwnershipRecord>().AsNoTracking().CountAsync(o => o.HousingStandId == stand.Id)).Should().Be(2);
        IReadOnlyList<DatabaseChangeView> changes = await scope.ServiceProvider.GetRequiredService<IAuditService>().DatabaseChangesAsync(start, DateTimeOffset.UtcNow.AddMinutes(1), false, 5000);
        changes.Count(c => c.TableName == "hse.ownership_record" && c.Operation == "INSERT").Should().BeGreaterThanOrEqualTo(2);
    }
}
