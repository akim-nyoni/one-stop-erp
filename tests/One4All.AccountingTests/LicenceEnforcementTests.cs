using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Security;
using One4All.Core.Accounting;
using One4All.Core.Pos;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers enforcing the licence: unlicensed API modules are refused, terminals beyond the licensed number cannot open a till, an
/// expiring licence warns, and a read-only licence refuses posting and API changes.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LicenceEnforcementTests
{
    private readonly AccountingFixture _fixture;

    public LicenceEnforcementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Enforced_licence_gates_modules_terminals_and_posting()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Ed25519KeyPair keys = Ed25519KeyPair.Generate();
        LicensingOptions options = new() { Enforce = true };
        options.TrustedKeys["enf-test"] = keys.PublicKeyBase64;
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(options));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "LE", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal first = new(companyId, branch.Id, "LE-T1", "Till 1"), second = new(companyId, branch.Id, "LE-T2", "Till 2");
        db.PosTerminals.Add(first);
        await db.SaveChangesAsync();
        db.PosTerminals.Add(second);
        await db.SaveChangesAsync();
        int terminalsUpToFirst = await db.PosTerminals.CountAsync(t => t.IsActive && t.Id <= first.Id);

        string tag = Guid.NewGuid().ToString("N")[..8];
        Role keeper = new($"Bookkeeper {tag}");
        keeper.Grant("GeneralLedger.Journal", OperationFlags.View | OperationFlags.Create | OperationFlags.Post);
        keeper.Grant("Municipal.Property", OperationFlags.View);
        db.Roles.Add(keeper);
        await db.SaveChangesAsync();
        User user = new($"keeper.{tag}", "Keeper");
        user.SetPasswordHash("hash", mustChange: false);
        user.AssignRole(keeper.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ILicenceService licence = scope.ServiceProvider.GetRequiredService<ILicenceService>();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        string Sign(DateOnly validTo, int terminals) => new LicenseSigner(keys.PrivateKeyBase64).Sign(new LicensePayload
        {
            KeyId = "enf-test", Product = LicenceService.Product, Customer = new LicenceCustomer { Id = "E", Name = "Enforced Co" }, ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = validTo, GraceDays = 0, HardStop = false, Modules = [new LicenceModuleGrant { Code = "GL" }, new LicenceModuleGrant { Code = "POS" }],
            Limits = new Dictionary<string, int?> { ["pos_terminals"] = terminals },
        });

        // A licence expiring in ten days with room for terminals up to the first of ours.
        (await licence.InstallAsync(Sign(today.AddDays(10), terminalsUpToFirst))).IsUsable.Should().BeTrue();
        licence.Warning.Should().Contain("expires in 10 day(s)");
        IApiAccessService access = scope.ServiceProvider.GetRequiredService<IApiAccessService>();
        Task<ApiAccessDecision> Ask(string method, string path) => access.AuthoriseAsync(new ApiAccessRequest(user.Id, method, path, new Dictionary<string, string>()));
        (await Ask("GET", "/api/municipal/properties")).Reason.Should().StartWith("Not licensed");
        (await Ask("POST", "/api/gl/journals/submit")).Allowed.Should().BeTrue();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        (await pos.OpenTillAsync(companyId, first.Id, 1, 0m)).IsSuccess.Should().BeTrue();
        (await pos.OpenTillAsync(companyId, second.Id, 1, 0m)).Error.Code.Should().Be("Licence.LimitReached");

        // The POS module's own "terminals" limit, as the brief's payload writes it, counts too (thirteenth audit #15).
        string moduleToken = new LicenseSigner(keys.PrivateKeyBase64).Sign(new LicensePayload
        {
            KeyId = "enf-test", Product = LicenceService.Product, Customer = new LicenceCustomer { Id = "E", Name = "Enforced Co" }, ValidFrom = new DateOnly(2025, 1, 1), ValidTo = today.AddDays(10), GraceDays = 0, HardStop = false,
            Modules = [new LicenceModuleGrant { Code = "GL" }, new LicenceModuleGrant { Code = "POS", Limits = new Dictionary<string, int?> { ["terminals"] = terminalsUpToFirst } }],
        });
        (await licence.InstallAsync(moduleToken)).IsUsable.Should().BeTrue();
        (await pos.OpenTillAsync(companyId, second.Id, 1, 0m)).Error.Code.Should().Be("Licence.LimitReached", "the module's terminals limit applies without a global pos_terminals");

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest journal = new()
        {
            CompanyId = companyId, SourceModule = "CB", PostingDate = new DateOnly(2026, 3, 2), TransactionCurrency = Scenario.Usd,
            Lines = [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 5m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 5m }],
        };
        (await engine.PostAsync(journal)).IsSuccess.Should().BeTrue();

        // Past expiry with no grace on a soft-stop licence: read-only.
        (await licence.InstallAsync(Sign(today.AddDays(-1), 99))).IsReadOnly.Should().BeTrue();
        licence.Warning.Should().Contain("read-only");
        (await engine.PostAsync(journal)).Error.Code.Should().Be("Licence.ReadOnly");
        (await Ask("POST", "/api/gl/journals/submit")).StatusCode.Should().Be(403);
        (await Ask("GET", "/api/gl/journals/1")).Allowed.Should().BeTrue();
    }
}
