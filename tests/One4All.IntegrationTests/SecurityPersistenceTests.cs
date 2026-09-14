using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Core.Audit;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class SecurityPersistenceTests
{
    private readonly DatabaseFixture _fixture;

    public SecurityPersistenceTests(DatabaseFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Permission_seeder_populates_catalogue_and_is_idempotent()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        await using One4AllDbContext db = _fixture.CreateContext();

        int firstRun = await new PermissionSeeder(db).SeedAsync();
        firstRun.Should().Be(PermissionCatalogue.All.Count);

        (await db.Permissions.CountAsync()).Should().Be(PermissionCatalogue.All.Count);

        // Running again changes nothing.
        await using One4AllDbContext db2 = _fixture.CreateContext();
        int secondRun = await new PermissionSeeder(db2).SeedAsync();
        secondRun.Should().Be(0);
    }

    [SkippableFact]
    public async Task User_with_role_round_trips_and_resolves_permissions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        long userId;
        await using (One4AllDbContext db = _fixture.CreateContext())
        {
            Role cashier = new("Cashier round-trip", "Test role");
            cashier.Grant("Pos.Sale", OperationFlags.View | OperationFlags.Create | OperationFlags.Post);
            db.Roles.Add(cashier);
            await db.SaveChangesAsync();

            User user = new("rt.user", "Round Trip");
            user.SetPasswordHash("hash", mustChange: false);
            user.AssignRole(cashier.Id);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        await using (One4AllDbContext db = _fixture.CreateContext())
        {
            User loaded = await db.Users.Include(u => u.Roles).FirstAsync(u => u.Id == userId);
            loaded.Roles.Should().HaveCount(1);

            long roleId = loaded.Roles.First().RoleId;
            Role role = await db.Roles.Include(r => r.Permissions).FirstAsync(r => r.Id == roleId);

            EffectivePermissions permissions = PermissionResolution.Resolve(
                role.Permissions.Select(p => p.ToGrant()),
                loaded.Overrides.Select(o => o.ToOverride()));

            permissions.Has("Pos.Sale", OperationFlags.Post).Should().BeTrue();
            permissions.HasAnyInModule("Pos").Should().BeTrue();
        }
    }

    [SkippableFact]
    public async Task Company_and_currency_persist_with_value_objects()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        await using (One4AllDbContext db = _fixture.CreateContext())
        {
            db.Currencies.Add(new Currency(CurrencyCode.Parse("USD"), "US Dollar", "$"));
            db.Companies.Add(new Company("ACME", "Acme Trading", CurrencyCode.Parse("USD"), "Africa/Harare"));
            await db.SaveChangesAsync();
        }

        await using (One4AllDbContext db = _fixture.CreateContext())
        {
            Company company = await db.Companies.FirstAsync(c => c.Code == "ACME");
            company.BaseCurrency.Should().Be(CurrencyCode.Parse("USD"));
        }
    }

    [SkippableFact]
    public async Task Audit_hash_chain_persists_and_verifies()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        await using One4AllDbContext db = _fixture.CreateContext();

        ChangeLogEntry first = ChangeLogEntry.Create(
            previousRowHash: string.Empty,
            occurredOnUtc: DateTimeOffset.UtcNow,
            companyId: 1, userId: 1, userName: "tester",
            module: "Security", entityType: "User", entityId: "1",
            action: AuditAction.Insert, newValues: "{\"a\":1}");

        ChangeLogEntry second = ChangeLogEntry.Create(
            previousRowHash: first.RowHash,
            occurredOnUtc: DateTimeOffset.UtcNow,
            companyId: 1, userId: 1, userName: "tester",
            module: "Security", entityType: "User", entityId: "1",
            action: AuditAction.Update, oldValues: "{\"a\":1}", newValues: "{\"a\":2}",
            changedFields: ["a"]);

        db.ChangeLog.AddRange(first, second);
        await db.SaveChangesAsync();

        second.VerifyAgainst(first.RowHash).Should().BeTrue();
        second.VerifyAgainst("tampered").Should().BeFalse();
    }
}
