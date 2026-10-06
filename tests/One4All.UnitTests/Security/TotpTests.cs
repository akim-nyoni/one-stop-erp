using FluentAssertions;
using One4All.Application.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers authenticator codes against the RFC 6238 test vectors.</summary>
public sealed class TotpTests
{
    // The RFC's SHA-1 key "12345678901234567890" in Base32.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Codes_match_the_rfc_vectors(long unixSeconds, string expected) => Totp.Code(RfcSecret, DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).Should().Be(expected);

    [Fact]
    public void A_code_is_accepted_one_step_either_side_and_secrets_round_trip()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        string code = Totp.Code(RfcSecret, now);
        Totp.Verify(RfcSecret, code, now.AddSeconds(30)).Should().BeTrue();
        Totp.Verify(RfcSecret, code, now.AddSeconds(90)).Should().BeFalse();
        Totp.Verify(RfcSecret, "12 34", now).Should().BeFalse();

        string secret = Totp.NewSecret();
        secret.Should().HaveLength(32);
        Totp.Verify(secret, Totp.Code(secret, now), now).Should().BeTrue();
        Totp.ProvisioningUri("One4All", "jane", secret).Should().StartWith("otpauth://totp/One4All:jane?secret=" + secret);
    }
}
