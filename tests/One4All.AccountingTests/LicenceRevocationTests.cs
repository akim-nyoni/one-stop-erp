using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Core.Licensing;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers installing the vendor's revocation list: a list naming the installed licence makes it read-only (no posting), a list from an
/// untrusted key or an older sequence is refused, and a later list without it restores the licence; the choice survives a reload.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LicenceRevocationTests
{
    private readonly AccountingFixture _fixture;

    public LicenceRevocationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_revocation_list_turns_the_licence_read_only()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Ed25519KeyPair keys = Ed25519KeyPair.Generate();
        LicensingOptions options = new() { Enforce = true };
        options.TrustedKeys["rev-test"] = keys.PublicKeyBase64;
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(options));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        ILicenceService licence = scope.ServiceProvider.GetRequiredService<ILicenceService>();

        LicensePayload payload = new()
        {
            KeyId = "rev-test", Product = LicenceService.Product, Customer = new LicenceCustomer { Id = "R", Name = "Revoked Co" }, ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = new DateOnly(2030, 1, 1), Modules = [new LicenceModuleGrant { Code = "GL" }],
        };
        (await licence.InstallAsync(new LicenseSigner(keys.PrivateKeyBase64).Sign(payload))).Status.Should().Be(LicenseStatus.Valid);
        licence.CanPost().IsSuccess.Should().BeTrue();

        int next = (await db.Set<InstalledRevocationList>().Select(r => (int?)r.Sequence).MaxAsync() ?? 0) + 1;
        string List(int sequence, params string[] revoked) => RevocationListCodec.Sign(new RevocationList { KeyId = "rev-test", Sequence = sequence, IssuedAt = DateTimeOffset.UtcNow, Revoked = [.. revoked] },
            keys.PrivateKeyBase64);

        (await licence.InstallRevocationListAsync(List(next, payload.LicenceId, "someone-else"))).Value.Should().Be(2);
        licence.Current.Status.Should().Be(LicenseStatus.Revoked);
        licence.Current.IsReadOnly.Should().BeTrue();
        licence.IsModuleLicensed("GL").Should().BeTrue("a revoked licence still lets people read their data");
        licence.CanPost().Error.Code.Should().Be("Licence.ReadOnly");
        await licence.ReloadAsync();
        licence.Current.Status.Should().Be(LicenseStatus.Revoked);

        (await licence.InstallRevocationListAsync(List(next))).Error.Code.Should().Be("Licence.RevocationListOld");
        string foreign = RevocationListCodec.Sign(new RevocationList { KeyId = "rev-test", Sequence = next + 5 }, Ed25519KeyPair.Generate().PrivateKeyBase64);
        (await licence.InstallRevocationListAsync(foreign)).Error.Code.Should().Be("Licence.RevocationListInvalid");
        (await licence.InstallRevocationListAsync("not a list")).Error.Code.Should().Be("Licence.RevocationListInvalid");

        // A later list without it (revoked in error) restores the licence.
        (await licence.InstallRevocationListAsync(List(next + 1, "someone-else"))).Value.Should().Be(1);
        licence.Current.Status.Should().Be(LicenseStatus.Valid);
        licence.CanPost().IsSuccess.Should().BeTrue();
    }
}
