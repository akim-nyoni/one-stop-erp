using FluentAssertions;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.UnitTests.Licensing;

/// <summary>
/// Covers the seam a machine-id provider plugs into: the raw components it gathers are hashed into a
/// <see cref="MachineFingerprint"/> and validated against a machine-bound licence. Uses a fake provider
/// so the assertions are deterministic on any platform (the real WMI provider is exercised separately).
/// </summary>
public sealed class MachineIdBindingTests
{
    private sealed class FakeMachineIdProvider(params string[] components) : IMachineIdProvider
    {
        public IReadOnlyList<string> GetComponents() => components;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private const string Kid = "key-test";
    private const string Product = "One4All ERP";

    private static string SignBoundLicence(MachineFingerprint boundTo, out LicenseVerifier verifier)
    {
        Ed25519KeyPair pair = Ed25519KeyPair.Generate();
        LicenseSigner signer = new(pair.PrivateKeyBase64);
        verifier = LicenseVerifier.ForSingleKey(Kid, pair.PublicKeyBase64);

        LicensePayload payload = new()
        {
            KeyId = Kid,
            Product = Product,
            Customer = new LicenceCustomer { Id = "C1", Name = "Bound Co" },
            Edition = LicenceEdition.Enterprise,
            LicenceType = LicenceType.Perpetual,
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = null,
            Modules = [new LicenceModuleGrant { Code = "GL" }],
            MachineBinding = new MachineBinding
            {
                Mode = MachineBindingMode.Machine,
                Fingerprints = [boundTo.ToString()],
            },
        };

        return signer.Sign(payload);
    }

    [Fact]
    public void Fingerprint_from_the_bound_machine_validates()
    {
        string[] components = ["machine:HOST-A", "sys-uuid:UUID-1", "mac:AA-BB-CC", "vol:1234-5678"];
        MachineFingerprint bound = MachineFingerprint.FromComponents(new FakeMachineIdProvider(components).GetComponents());

        string token = SignBoundLicence(bound, out LicenseVerifier verifier);
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        // The same machine re-fingerprinted from its provider.
        MachineFingerprint current = MachineFingerprint.FromComponents(new FakeMachineIdProvider(components).GetComponents());

        validator.Validate(token, current).Status.Should().Be(LicenseStatus.Valid);
    }

    [Fact]
    public void Fingerprint_survives_one_component_changing()
    {
        MachineFingerprint bound = MachineFingerprint.FromComponents(
            new FakeMachineIdProvider("machine:HOST-A", "sys-uuid:UUID-1", "mac:AA-BB-CC", "vol:1234-5678").GetComponents());

        string token = SignBoundLicence(bound, out LicenseVerifier verifier);
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        // NIC swapped: 3 of 4 components still match.
        MachineFingerprint afterNicSwap = MachineFingerprint.FromComponents(
            new FakeMachineIdProvider("machine:HOST-A", "sys-uuid:UUID-1", "mac:ZZ-ZZ-ZZ", "vol:1234-5678").GetComponents());

        validator.Validate(token, afterNicSwap).Status.Should().Be(LicenseStatus.Valid);
    }

    [Fact]
    public void Fingerprint_from_a_different_machine_is_rejected()
    {
        MachineFingerprint bound = MachineFingerprint.FromComponents(
            new FakeMachineIdProvider("machine:HOST-A", "sys-uuid:UUID-1", "mac:AA-BB-CC", "vol:1234-5678").GetComponents());

        string token = SignBoundLicence(bound, out LicenseVerifier verifier);
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        MachineFingerprint other = MachineFingerprint.FromComponents(
            new FakeMachineIdProvider("machine:HOST-B", "sys-uuid:UUID-9", "mac:99-88-77", "vol:9999-0000").GetComponents());

        validator.Validate(token, other).Status.Should().Be(LicenseStatus.MachineMismatch);
    }
}
