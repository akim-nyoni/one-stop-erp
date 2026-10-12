using System.Security.Cryptography;
using FluentAssertions;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.System;
using One4All.Infrastructure.SystemServices;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the update channel on a file share: a signed release is offered, its installer copied and checked; a tampered installer is refused.</summary>
public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "one4all-update-" + Guid.NewGuid().ToString("N"));

    public UpdateServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_signed_release_on_the_share_is_offered_and_checked()
    {
        Ed25519KeyPair vendor = Ed25519KeyPair.Generate();
        string installer = Path.Combine(_dir, "setup.exe");
        await File.WriteAllBytesAsync(installer, [1, 2, 3, 4]);
        string channel = Path.Combine(_dir, "update.manifest");
        UpdateManifest release = new() { KeyId = "v1", Version = "2.0.0", InstallerLocation = installer, Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 })) };
        await File.WriteAllTextAsync(channel, UpdateManifestCodec.Sign(release, vendor.PrivateKeyBase64));

        UpdateService updates = new(new UpdateOptions { Channel = channel, CurrentVersion = new Version(1, 0, 0) }, new Keys(vendor.PublicKeyBase64));
        UpdateCheck check = await updates.CheckAsync();
        (check.Status, check.CurrentVersion, check.Manifest!.Version).Should().Be((UpdateStatus.Available, "1.0.0", "2.0.0"));
        string downloaded = (await updates.DownloadAsync(check.Manifest)).Value;
        (await File.ReadAllBytesAsync(downloaded)).Should().Equal(1, 2, 3, 4);

        await File.WriteAllBytesAsync(installer, [9, 9, 9]);
        (await updates.DownloadAsync(check.Manifest)).Error.Code.Should().Be("Sys.UpdateTampered");
        (await new UpdateService(new UpdateOptions { Channel = null }, new Keys(vendor.PublicKeyBase64)).CheckAsync()).Problem.Should().Contain("No update channel");
        (await new UpdateService(new UpdateOptions { Channel = channel }, new Keys(Ed25519KeyPair.Generate().PublicKeyBase64)).CheckAsync()).Manifest.Should().BeNull();
    }

    private sealed class Keys(string publicKey) : ITrustedLicenceKeys
    {
        public IReadOnlyDictionary<string, byte[]> PublicKeysByKid { get; } = new Dictionary<string, byte[]> { ["v1"] = Convert.FromBase64String(publicKey) };
    }
}
