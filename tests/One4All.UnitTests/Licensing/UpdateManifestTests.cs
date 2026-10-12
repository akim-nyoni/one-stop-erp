using FluentAssertions;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.UnitTests.Licensing;

/// <summary>Covers the update channel's signed manifest and the update decision.</summary>
public sealed class UpdateManifestTests
{
    [Fact]
    public void A_signed_manifest_is_trusted_and_decides_the_update()
    {
        Ed25519KeyPair vendor = Ed25519KeyPair.Generate(), stranger = Ed25519KeyPair.Generate();
        UpdateManifest release = new() { KeyId = "v1", Version = "1.4.0", InstallerLocation = "https://example.test/setup.exe", Sha256 = "AB", MinimumVersion = "1.2.0" };
        string token = UpdateManifestCodec.Sign(release, vendor.PrivateKeyBase64);
        Dictionary<string, byte[]> trusted = new() { ["v1"] = Convert.FromBase64String(vendor.PublicKeyBase64) };

        UpdateManifestCodec.Verify(token, trusted).Manifest!.Version.Should().Be("1.4.0");
        UpdateManifestCodec.Verify(UpdateManifestCodec.Sign(release, stranger.PrivateKeyBase64), trusted).Error.Should().Contain("signature");
        UpdateManifestCodec.Verify(token, new Dictionary<string, byte[]>()).Error.Should().Contain("trusted key");
        UpdateManifestCodec.Verify("rubbish", trusted).Error.Should().Contain("UP1");
        string[] parts = token.Split('.');
        UpdateManifestCodec.Verify($"{parts[0]}.{Base64Url.Encode(System.Text.Encoding.UTF8.GetBytes("{\"kid\":\"v1\",\"version\":\"9.9.9\"}"))}.{parts[2]}", trusted)
            .Manifest.Should().BeNull("an altered manifest fails its signature");

        UpdateManifestCodec.Decide(new Version(1, 4, 0), release).Should().Be(UpdateStatus.UpToDate);
        UpdateManifestCodec.Decide(new Version(1, 5), release).Should().Be(UpdateStatus.UpToDate);
        UpdateManifestCodec.Decide(new Version(1, 3, 2), release).Should().Be(UpdateStatus.Available);
        UpdateManifestCodec.Decide(new Version(1, 1, 9), release).Should().Be(UpdateStatus.Required);
    }
}
