using FluentAssertions;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.UnitTests.Licensing;

public class LicensingTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private const string Kid = "key-test";
    private const string Product = "One4All ERP";

    private static (LicenseSigner signer, LicenseVerifier verifier) NewPair()
    {
        Ed25519KeyPair pair = Ed25519KeyPair.Generate();
        return (new LicenseSigner(pair.PrivateKeyBase64), LicenseVerifier.ForSingleKey(Kid, pair.PublicKeyBase64));
    }

    private static LicensePayload BasePayload(DateOnly from, DateOnly? to, bool hardStop = true, int grace = 0) => new()
    {
        KeyId = Kid,
        Product = Product,
        Customer = new LicenceCustomer { Id = "C1", Name = "Test Council" },
        Edition = LicenceEdition.Municipal,
        LicenceType = LicenceType.Subscription,
        ValidFrom = from,
        ValidTo = to,
        GraceDays = grace,
        HardStop = hardStop,
        Modules =
        [
            new LicenceModuleGrant { Code = "GL" },
            new LicenceModuleGrant { Code = "MUNI", Limits = { ["properties"] = 50000 } },
        ],
        Features = ["MULTI_CURRENCY"],
        Limits = { ["companies"] = 3 },
    };

    [Fact]
    public void Sign_and_verify_round_trip()
    {
        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        string token = signer.Sign(BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));

        SignatureVerification result = verifier.Verify(token);
        result.IsValid.Should().BeTrue();
        result.Payload!.Customer.Name.Should().Be("Test Council");
    }

    [Fact]
    public void Tampered_payload_fails_verification()
    {
        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        string token = signer.Sign(BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));

        string[] parts = token.Split('.');
        byte[] payloadBytes = Base64Url.Decode(parts[0]);
        payloadBytes[10] ^= 0xFF; // flip a byte
        string tampered = $"{Base64Url.Encode(payloadBytes)}.{parts[1]}";

        verifier.Verify(tampered).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Wrong_key_is_rejected()
    {
        (LicenseSigner signer, _) = NewPair();
        string token = signer.Sign(BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));

        LicenseVerifier otherVerifier = LicenseVerifier.ForSingleKey(Kid, Ed25519KeyPair.Generate().PublicKeyBase64);
        otherVerifier.Verify(token).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_licence_is_usable_and_gates_modules()
    {
        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        string token = signer.Sign(BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        LicenseValidationResult result = validator.Validate(token);
        result.Status.Should().Be(LicenseStatus.Valid);
        result.IsUsable.Should().BeTrue();
        result.IsModuleLicensed("GL").Should().BeTrue();
        result.IsModuleLicensed("MUNI").Should().BeTrue();
        result.IsModuleLicensed("PAY").Should().BeFalse();
        result.IsModuleLicensed("CORE").Should().BeTrue();
        result.GetModuleLimit("MUNI", "properties").Should().Be(50000);
        result.HasFeature("MULTI_CURRENCY").Should().BeTrue();
    }

    [Fact]
    public void Expired_within_grace_is_usable_but_flagged()
    {
        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        string token = signer.Sign(BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), grace: 14));
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero)), Product);

        LicenseValidationResult result = validator.Validate(token);
        result.Status.Should().Be(LicenseStatus.Grace);
        result.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void Expired_past_grace_hard_stop_is_not_usable()
    {
        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        string token = signer.Sign(BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), hardStop: true, grace: 7));
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        LicenseValidationResult result = validator.Validate(token);
        result.Status.Should().Be(LicenseStatus.Expired);
        result.IsUsable.Should().BeFalse();
        result.IsModuleLicensed("GL").Should().BeFalse();
    }

    [Fact]
    public void Expired_past_grace_soft_stop_degrades_to_read_only()
    {
        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        string token = signer.Sign(BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), hardStop: false, grace: 7));
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        LicenseValidationResult result = validator.Validate(token);
        result.Status.Should().Be(LicenseStatus.ReadOnly);
        result.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Wrong_product_is_rejected()
    {
        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        LicensePayload payload = BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        payload.Product = "Some Other ERP";
        string token = signer.Sign(payload);
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        validator.Validate(token).Status.Should().Be(LicenseStatus.WrongProduct);
    }

    [Fact]
    public void Machine_binding_matches_within_fuzzy_tolerance()
    {
        MachineFingerprint issued = MachineFingerprint.FromComponents(["mb:AAA", "guid:BBB", "mac:CCC", "vol:DDD"]);
        // One component changed (mac) — still 3 of 4 match.
        MachineFingerprint current = MachineFingerprint.FromComponents(["mb:AAA", "guid:BBB", "mac:ZZZ", "vol:DDD"]);

        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        LicensePayload payload = BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        payload.MachineBinding = new MachineBinding { Mode = MachineBindingMode.Machine, Fingerprints = [issued.ToString()] };
        string token = signer.Sign(payload);
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        validator.Validate(token, current).Status.Should().Be(LicenseStatus.Valid);
    }

    [Fact]
    public void Machine_binding_rejects_a_different_machine()
    {
        MachineFingerprint issued = MachineFingerprint.FromComponents(["mb:AAA", "guid:BBB", "mac:CCC", "vol:DDD"]);
        MachineFingerprint other = MachineFingerprint.FromComponents(["mb:111", "guid:222", "mac:333", "vol:444"]);

        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        LicensePayload payload = BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        payload.MachineBinding = new MachineBinding { Mode = MachineBindingMode.Machine, Fingerprints = [issued.ToString()] };
        string token = signer.Sign(payload);
        LicenseValidator validator = new(verifier, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), Product);

        validator.Validate(token, other).Status.Should().Be(LicenseStatus.MachineMismatch);
    }

    [Fact]
    public void Protected_private_key_round_trips_and_rejects_wrong_passphrase()
    {
        Ed25519KeyPair pair = Ed25519KeyPair.Generate();
        ProtectedPrivateKey blob = ProtectedPrivateKey.Protect(pair.PrivateKeyBase64, "correct horse");

        blob.Unprotect("correct horse").Should().Be(pair.PrivateKeyBase64);
        Action wrong = () => blob.Unprotect("wrong passphrase");
        wrong.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Clock_rollback_is_detected()
    {
        (LicenseSigner signer, LicenseVerifier verifier) = NewPair();
        string token = signer.Sign(BasePayload(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));

        InMemoryHighWater store = new() { Mark = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero) };
        LicenseValidator validator = new(
            verifier,
            new FixedClock(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)), // clock set way back
            Product,
            store);

        validator.Validate(token).Status.Should().Be(LicenseStatus.TamperDetected);
    }

    private sealed class InMemoryHighWater : ILicenseHighWaterMarkStore
    {
        public DateTimeOffset? Mark { get; set; }

        public DateTimeOffset? Read() => Mark;

        public void Advance(DateTimeOffset value)
        {
            if (Mark is null || value > Mark)
            {
                Mark = value;
            }
        }
    }
}
