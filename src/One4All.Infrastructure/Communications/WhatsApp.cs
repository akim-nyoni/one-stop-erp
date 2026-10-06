using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Communications;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Communications;

/// <summary>WhatsApp Cloud API settings (<c>Messaging:WhatsApp</c>).</summary>
public sealed class WhatsAppOptions
{
    /// <summary>Gets or sets the Graph API base URL.</summary>
    public string ApiBaseUrl { get; set; } = "https://graph.facebook.com/v21.0";

    /// <summary>Gets or sets the sending phone number id (null: WhatsApp is not configured).</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Gets or sets the access token.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Gets or sets the app secret that signs webhook deliveries.</summary>
    public string? AppSecret { get; set; }

    /// <summary>Gets or sets the token Meta echoes when subscribing the webhook.</summary>
    public string? VerifyToken { get; set; }

    /// <summary>
    /// Gets or sets an approved template to send messages through (one body parameter carrying the text): WhatsApp only lets a business start a
    /// conversation, or speak after 24 hours of silence, with a template. Null sends plain text (replies within the 24-hour window).
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>Gets or sets the template's language code.</summary>
    public string TemplateLanguage { get; set; } = "en";

    /// <summary>Gets or sets the company replies belong to (null: the company that last messaged the number, else the first company).</summary>
    public long? CompanyId { get; set; }
}

/// <summary>Sends messages through the WhatsApp Cloud API; the provider id it returns is how receipts find the message later.</summary>
public sealed class WhatsAppCloudGateway : IMessageGateway
{
    private readonly HttpClient _http;
    private readonly WhatsAppOptions _options;

    /// <summary>Creates the gateway.</summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="options">The messaging settings.</param>
    public WhatsAppCloudGateway(HttpClient http, MessagingOptions options)
    {
        _http = http;
        _options = options.WhatsApp;
    }

    /// <inheritdoc />
    public MessageChannel Channel => MessageChannel.WhatsApp;

    /// <summary>Builds the Cloud API request body (text, or the configured template with the text as its parameter).</summary>
    /// <param name="number">The recipient, digits with country code.</param>
    /// <param name="body">The text.</param>
    /// <param name="options">The settings.</param>
    /// <returns>The JSON.</returns>
    public static string RequestBody(string number, string body, WhatsAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        JsonObject request = new() { ["messaging_product"] = "whatsapp", ["recipient_type"] = "individual", ["to"] = number };
        if (string.IsNullOrWhiteSpace(options.TemplateName))
        {
            request["type"] = "text";
            request["text"] = new JsonObject { ["preview_url"] = false, ["body"] = body.Length > 4096 ? body[..4096] : body };
        }
        else
        {
            request["type"] = "template";
            request["template"] = new JsonObject
            {
                ["name"] = options.TemplateName,
                ["language"] = new JsonObject { ["code"] = options.TemplateLanguage },
                ["components"] = new JsonArray(new JsonObject
                {
                    ["type"] = "body", ["parameters"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = body.Length > 1024 ? body[..1024] : body }),
                }),
            };
        }

        return request.ToJsonString();
    }

    /// <inheritdoc />
    public async Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.PhoneNumberId) || string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            return new GatewayResult(false, "WhatsApp is not configured (Messaging:WhatsApp).");
        }

        if (SmsRequest.Number(recipient)?.TrimStart('+') is not string number)
        {
            return new GatewayResult(false, $"\"{recipient}\" is not a phone number.");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, $"{_options.ApiBaseUrl.TrimEnd('/')}/{_options.PhoneNumberId}/messages")
        {
            Content = new StringContent(RequestBody(number, body, _options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            string reply = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new GatewayResult(false, $"WhatsApp refused ({(int)response.StatusCode}): {(reply.Length > 300 ? reply[..300] : reply)}");
            }

            string? id = JsonNode.Parse(reply)?["messages"]?[0]?["id"]?.GetValue<string>();
            return id is null ? new GatewayResult(false, "WhatsApp accepted the request but returned no message id.") : new GatewayResult(true, id);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new GatewayResult(false, $"WhatsApp unreachable: {ex.Message}");
        }
    }
}

/// <summary>The WhatsApp webhook: receipts and replies.</summary>
public sealed class WhatsAppWebhookService : IWhatsAppWebhookService
{
    private readonly One4AllDbContext _db;
    private readonly WhatsAppOptions _options;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="options">The messaging settings.</param>
    public WhatsAppWebhookService(One4AllDbContext db, MessagingOptions options)
    {
        _db = db;
        _options = options.WhatsApp;
    }

    /// <summary>Computes the signature header value Meta sends for a body.</summary>
    /// <param name="body">The raw body.</param>
    /// <param name="appSecret">The app secret.</param>
    /// <returns>The <c>sha256=…</c> value.</returns>
    public static string Sign(byte[] body, string appSecret) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));

    /// <inheritdoc />
    public string? VerifySubscription(string? mode, string? verifyToken, string? challenge)
        => mode == "subscribe" && !string.IsNullOrEmpty(_options.VerifyToken) && verifyToken is not null
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(verifyToken), Encoding.UTF8.GetBytes(_options.VerifyToken)) ? challenge : null;

    /// <inheritdoc />
    public async Task<Result<WhatsAppWebhookResult>> ProcessAsync(byte[] body, string? signature, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrEmpty(_options.AppSecret))
        {
            return Error.Rule("WhatsApp.NotConfigured", "The WhatsApp app secret is not configured, so deliveries cannot be trusted.");
        }

        if (signature is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(body, _options.AppSecret)), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant())))
        {
            return Error.Forbidden("WhatsApp.Signature", "The delivery's signature does not match.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return Error.Validation("WhatsApp.Payload", "The delivery is not JSON.");
        }

        int receipts = 0, received = 0, ignored = 0;
        IEnumerable<JsonNode> values = (root?["entry"]?.AsArray() ?? []).SelectMany(e => e?["changes"]?.AsArray() ?? []).Select(c => c?["value"]).OfType<JsonNode>();
        foreach (JsonNode value in values)
        {
            foreach (JsonNode status in (value["statuses"]?.AsArray() ?? []).OfType<JsonNode>())
            {
                string? id = status["id"]?.GetValue<string>();
                OutboundMessage? message = id is null ? null
                    : await _db.Set<OutboundMessage>().FirstOrDefaultAsync(m => m.Channel == MessageChannel.WhatsApp && m.ProviderReference == id, ct).ConfigureAwait(false);
                string? error = status["errors"]?[0] is JsonNode e ? $"{e["code"]}: {e["title"]?.GetValue<string>()}" : null;
                if (message is not null && message.RecordReceipt(status["status"]?.GetValue<string>() ?? string.Empty, Time(status["timestamp"]), error))
                {
                    receipts++;
                }
                else
                {
                    ignored++;
                }
            }

            Dictionary<string, string?> names = (value["contacts"]?.AsArray() ?? []).OfType<JsonNode>()
                .Where(c => c["wa_id"] is not null).GroupBy(c => c["wa_id"]!.GetValue<string>()).ToDictionary(g => g.Key, g => g.First()["profile"]?["name"]?.GetValue<string>());
            foreach (JsonNode incoming in (value["messages"]?.AsArray() ?? []).OfType<JsonNode>())
            {
                string? id = incoming["id"]?.GetValue<string>();
                string from = incoming["from"]?.GetValue<string>() ?? string.Empty;
                if (id is null || from.Length == 0 || await _db.Set<InboundMessage>().AnyAsync(m => m.ProviderReference == id, ct).ConfigureAwait(false)
                    || _db.Set<InboundMessage>().Local.Any(m => m.ProviderReference == id))
                {
                    ignored++;
                    continue;
                }

                long companyId = await CompanyForAsync(from, ct).ConfigureAwait(false);
                InboundMessage message = new(companyId, MessageChannel.WhatsApp, from, names.GetValueOrDefault(from), Text(incoming), Time(incoming["timestamp"]), id);
                (message.CustomerId, message.LeadId) = await MatchAsync(companyId, from, ct).ConfigureAwait(false);
                _db.Add(message);
                received++;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WhatsAppWebhookResult(receipts, received, ignored);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboundMessageLine>> UnhandledAsync(long companyId, CancellationToken ct = default)
        => await _db.Set<InboundMessage>().AsNoTracking().Where(m => m.CompanyId == companyId && m.HandledAt == null).OrderBy(m => m.ReceivedAt)
            .Select(m => new InboundMessageLine(m.Id, m.From, m.SenderName, m.Body, m.ReceivedAt, m.CustomerId, m.LeadId)).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> MarkHandledAsync(long companyId, long inboundMessageId, long userId, CancellationToken ct = default)
    {
        InboundMessage? message = await _db.Set<InboundMessage>().FirstOrDefaultAsync(m => m.Id == inboundMessageId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (message is null || message.HandledAt is not null)
        {
            return Result.Failure(Error.Rule("WhatsApp.NotOpen", "No unhandled message with that id."));
        }

        message.HandledAt = DateTimeOffset.UtcNow;
        message.HandledByUserId = userId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static DateTimeOffset Time(JsonNode? timestamp)
        => long.TryParse(timestamp?.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UtcNow;

    // Text as sent; other content described so the person reading knows something arrived.
    private static string Text(JsonNode incoming)
    {
        string type = incoming["type"]?.GetValue<string>() ?? "unknown";
        string text = type switch
        {
            "text" => incoming["text"]?["body"]?.GetValue<string>() ?? string.Empty,
            "button" => incoming["button"]?["text"]?.GetValue<string>() ?? "[button]",
            "interactive" => incoming["interactive"]?["button_reply"]?["title"]?.GetValue<string>() ?? incoming["interactive"]?["list_reply"]?["title"]?.GetValue<string>() ?? "[interactive reply]",
            _ => $"[{type}{(incoming[type]?["caption"]?.GetValue<string>() is string caption ? $": {caption}" : string.Empty)}]",
        };
        return text.Length > 4096 ? text[..4096] : text;
    }

    private async Task<long> CompanyForAsync(string from, CancellationToken ct)
    {
        if (_options.CompanyId is long configured)
        {
            return configured;
        }

        string tail = Tail(from);
        List<(long CompanyId, string Recipient)> recent = [.. (await _db.Set<OutboundMessage>().AsNoTracking().Where(m => m.Channel == MessageChannel.WhatsApp && m.Recipient.EndsWith(tail))
            .OrderByDescending(m => m.Id).Select(m => new { m.CompanyId, m.Recipient }).Take(20).ToListAsync(ct).ConfigureAwait(false)).Select(m => (m.CompanyId, m.Recipient))];
        return recent.Where(m => Tail(m.Recipient) == tail).Select(m => (long?)m.CompanyId).FirstOrDefault()
            ?? await _db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync(ct).ConfigureAwait(false);
    }

    // The customer or lead whose number ends in the same nine digits (country codes and leading zeros differ).
    private async Task<(long? CustomerId, long? LeadId)> MatchAsync(long companyId, string from, CancellationToken ct)
    {
        string tail = Tail(from);
        string last4 = tail[^Math.Min(4, tail.Length)..];
        long? customer = (await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.MobileNumber != null && c.MobileNumber.EndsWith(last4))
                .Select(c => new { c.Id, c.MobileNumber }).ToListAsync(ct).ConfigureAwait(false))
            .Where(c => Tail(c.MobileNumber!) == tail).Select(c => (long?)c.Id).FirstOrDefault();
        long? lead = (await _db.Set<Core.Crm.Lead>().AsNoTracking().Where(l => l.CompanyId == companyId && l.Phone != null && l.Phone.EndsWith(last4)).OrderByDescending(l => l.Id)
                .Select(l => new { l.Id, l.Phone }).ToListAsync(ct).ConfigureAwait(false))
            .Where(l => Tail(l.Phone!) == tail).Select(l => (long?)l.Id).FirstOrDefault();
        return (customer, lead);
    }

    private static string Tail(string number)
    {
        string digits = new([.. number.Where(char.IsDigit)]);
        return digits.Length > 9 ? digits[^9..] : digits;
    }
}
