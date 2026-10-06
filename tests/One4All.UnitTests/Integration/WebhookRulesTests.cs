using FluentAssertions;
using One4All.Application.Integration;
using Xunit;

namespace One4All.UnitTests.Integration;

/// <summary>Covers webhook signatures and the retry schedule.</summary>
public sealed class WebhookRulesTests
{
    [Fact]
    public void Signatures_are_hmac_sha256_of_the_body()
        => WebhookRules.Signature("key", "The quick brown fox jumps over the lazy dog")
            .Should().Be("sha256=f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8");

    [Fact]
    public void Retries_back_off_then_stop()
    {
        DateTimeOffset now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        WebhookRules.NextAttempt(1, now).Should().Be(now.AddMinutes(1));
        WebhookRules.NextAttempt(3, now).Should().Be(now.AddMinutes(30));
        WebhookRules.NextAttempt(5, now).Should().Be(now.AddHours(12));
        WebhookRules.NextAttempt(WebhookRules.MaxAttempts, now).Should().BeNull();
    }
}
