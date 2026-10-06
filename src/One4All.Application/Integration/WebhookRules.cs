using System.Security.Cryptography;
using System.Text;

namespace One4All.Application.Integration;

/// <summary>The events One4All publishes (brief §10).</summary>
public static class WebhookEvents
{
    /// <summary>A POS sale was completed.</summary>
    public const string SaleCompleted = "sale.completed";

    /// <summary>A sales invoice was posted.</summary>
    public const string InvoicePosted = "invoice.posted";

    /// <summary>A customer payment was received.</summary>
    public const string PaymentReceived = "payment.received";

    /// <summary>A municipal billing run's bills were generated.</summary>
    public const string BillsGenerated = "bill.generated";

    /// <summary>All events.</summary>
    public static IReadOnlyList<string> All { get; } = [SaleCompleted, InvoicePosted, PaymentReceived, BillsGenerated];
}

/// <summary>Signing and retry rules for webhook deliveries. Pure.</summary>
public static class WebhookRules
{
    /// <summary>The most attempts before a delivery is given up.</summary>
    public const int MaxAttempts = 6;

    /// <summary>The signature header value: <c>sha256=</c> and the hex HMAC-SHA256 of the body with the subscription's secret.</summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="body">The request body.</param>
    /// <returns>The signature.</returns>
    public static string Signature(string secret, string body)
        => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    /// <summary>When to try again after a failed attempt (1, 5, 30 minutes, 2 hours, 12 hours), or null once attempts run out.</summary>
    /// <param name="attemptsMade">Attempts made so far, including the failed one.</param>
    /// <param name="now">Now.</param>
    /// <returns>The next attempt, or null.</returns>
    public static DateTimeOffset? NextAttempt(int attemptsMade, DateTimeOffset now) => attemptsMade switch
    {
        >= MaxAttempts => null,
        1 => now.AddMinutes(1),
        2 => now.AddMinutes(5),
        3 => now.AddMinutes(30),
        4 => now.AddHours(2),
        _ => now.AddHours(12),
    };
}
