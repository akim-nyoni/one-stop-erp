using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Core.Accounting;
using One4All.Core.Audit;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Infrastructure.Security;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ServicesIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public ServicesIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void Argon2_hasher_round_trips()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        IPasswordHasher hasher = new Argon2PasswordHasher();

        string hash = hasher.Hash("Correct Horse 42");
        hasher.Verify(hash, "Correct Horse 42").Should().BeTrue();
        hasher.Verify(hash, "wrong").Should().BeFalse();
    }

    [SkippableFact]
    public async Task Admin_seed_then_authenticate_and_lockout()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
            bool created = await scope.ServiceProvider.GetRequiredService<AdministratorSeeder>().SeedAsync();
            created.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            AuthenticationResult ok = await auth.AuthenticateAsync(
                AdministratorSeeder.AdminUsername, AdministratorSeeder.DefaultPassword);
            ok.Succeeded.Should().BeTrue();
            ok.IsSystemAdministrator.Should().BeTrue();
            ok.MustChangePassword.Should().BeTrue();
            ok.Permissions.Has("System.Company", OperationFlags.Create).Should().BeTrue(); // super-admin bypass
        }

        // Five wrong attempts then lockout (default policy).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            AuthenticationResult last = AuthenticationResult.Failure(AuthenticationOutcome.InvalidCredentials, "seed");
            for (int i = 0; i < 5; i++)
            {
                last = await auth.AuthenticateAsync(AdministratorSeeder.AdminUsername, "definitely-wrong");
            }

            last.Outcome.Should().Be(AuthenticationOutcome.LockedOut);
        }
    }

    [SkippableFact]
    public async Task Audit_interceptor_writes_hash_chained_change_log()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        CurrentUserContext currentUser = scope.ServiceProvider.GetRequiredService<CurrentUserContext>();
        currentUser.SignIn(99, "tester", EffectivePermissions.None, companyId: null, terminal: "TILL-1");

        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        Currency currency = new(CurrencyCode.Parse("GBP"), "Pound", "£");
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();

        currency.Symbol = "GBP";
        await db.SaveChangesAsync();

        List<ChangeLogEntry> entries = await db.ChangeLog
            .Where(c => c.EntityType == "Currency")
            .OrderBy(c => c.Id)
            .ToListAsync();

        entries.Should().HaveCountGreaterThanOrEqualTo(2);
        entries.Should().Contain(e => e.Action == AuditAction.Insert);
        entries.Should().Contain(e => e.Action == AuditAction.Update && e.ChangedFields.Contains("Symbol"));
        entries.Should().OnlyContain(e => e.UserName == "tester");

        // The hash chain is maintained per company, so verify each company's chain independently.
        List<ChangeLogEntry> all = await db.ChangeLog.OrderBy(c => c.Id).ToListAsync();
        foreach (IGrouping<long?, ChangeLogEntry> companyChain in all.GroupBy(c => c.CompanyId))
        {
            string previous = string.Empty;
            foreach (ChangeLogEntry entry in companyChain.OrderBy(c => c.Id))
            {
                entry.VerifyAgainst(previous).Should().BeTrue($"entry {entry.Id} (company {entry.CompanyId}) should chain from the previous hash");
                previous = entry.RowHash;
            }
        }
    }

    [SkippableFact]
    public async Task Licence_install_gates_modules()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        Ed25519KeyPair keyPair = Ed25519KeyPair.Generate();
        LicensingOptions options = new();
        options.TrustedKeys["key-it"] = keyPair.PublicKeyBase64;

        await using ServiceProvider provider = _fixture.BuildServiceProvider(options);

        LicensePayload payload = new()
        {
            KeyId = "key-it",
            Product = LicenceService.Product,
            Customer = new LicenceCustomer { Id = "C", Name = "IT Co" },
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2027, 1, 1),
            Modules = [new LicenceModuleGrant { Code = "GL" }, new LicenceModuleGrant { Code = "MUNI" }],
        };
        string token = new LicenseSigner(keyPair.PrivateKeyBase64).Sign(payload);

        ILicenceService licence = provider.GetRequiredService<ILicenceService>();
        LicenseValidationResult result = await licence.InstallAsync(token);

        result.IsUsable.Should().BeTrue();
        licence.IsModuleLicensed("GL").Should().BeTrue();
        licence.IsModuleLicensed("MUNI").Should().BeTrue();
        licence.IsModuleLicensed("PAY").Should().BeFalse();
        licence.IsModuleLicensed("CORE").Should().BeTrue();

        // A fresh service reloads the persisted licence from the database.
        await using ServiceProvider provider2 = _fixture.BuildServiceProvider(options);
        ILicenceService licence2 = provider2.GetRequiredService<ILicenceService>();
        await licence2.ReloadAsync();
        licence2.IsModuleLicensed("MUNI").Should().BeTrue();
    }
}
