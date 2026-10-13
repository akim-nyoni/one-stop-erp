using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using One4All.Application.Abstractions.Communications;
using One4All.Core.Communications;

namespace One4All.Infrastructure.Communications;

/// <summary>
/// Sends one email through Microsoft Graph (brief §8 "SMTP/Graph email"): an app-only token from the tenant (client credentials, scope
/// graph.microsoft.com/.default) and a sendMail call on the company's mailbox, with any attachment as a file attachment.
/// </summary>
internal static class GraphMailSender
{
    /// <summary>Gets or sets the token endpoint's base (overridable for tests).</summary>
    public static string LoginBase { get; set; } = "https://login.microsoftonline.com";

    /// <summary>Gets or sets the Graph API's base (overridable for tests).</summary>
    public static string GraphBase { get; set; } = "https://graph.microsoft.com/v1.0";

    public static async Task<GatewayResult> SendAsync(HttpClient http, CompanyMessagingSettings settings, string recipient, string subject, string body, MessageAttachment? attachment,
        CancellationToken ct)
    {
        try
        {
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["client_id"] = settings.GraphClientId ?? string.Empty, ["client_secret"] = settings.GraphClientSecret ?? string.Empty,
                ["scope"] = "https://graph.microsoft.com/.default", ["grant_type"] = "client_credentials",
            });
            using HttpResponseMessage tokenResponse = await http.PostAsync($"{LoginBase}/{Uri.EscapeDataString(settings.GraphTenantId ?? string.Empty)}/oauth2/v2.0/token", form, ct)
                .ConfigureAwait(false);
            string tokenJson = await tokenResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return new GatewayResult(false, $"Microsoft sign-in refused ({(int)tokenResponse.StatusCode}).");
            }

            string token = JsonDocument.Parse(tokenJson).RootElement.GetProperty("access_token").GetString() ?? string.Empty;
            Dictionary<string, object?> message = new()
            {
                ["subject"] = subject,
                ["body"] = new { contentType = "Text", content = body },
                ["toRecipients"] = new[] { new { emailAddress = new { address = recipient.Trim() } } },
            };
            if (attachment is not null)
            {
                message["attachments"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["@odata.type"] = "#microsoft.graph.fileAttachment", ["name"] = attachment.Name, ["contentType"] = attachment.ContentType,
                        ["contentBytes"] = Convert.ToBase64String(attachment.Content),
                    },
                };
            }

            using HttpRequestMessage send = new(HttpMethod.Post, $"{GraphBase}/users/{Uri.EscapeDataString(settings.FromAddress ?? string.Empty)}/sendMail")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { message, saveToSentItems = true }), Encoding.UTF8, "application/json"),
            };
            send.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage sent = await http.SendAsync(send, ct).ConfigureAwait(false);
            if (!sent.IsSuccessStatusCode)
            {
                string reply = await sent.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new GatewayResult(false, $"Microsoft Graph refused ({(int)sent.StatusCode}): {(reply.Length > 200 ? reply[..200] : reply)}");
            }

            return new GatewayResult(true, sent.Headers.TryGetValues("request-id", out IEnumerable<string>? ids) ? ids.First() : $"graph-{Guid.NewGuid():N}");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new GatewayResult(false, $"Microsoft Graph unreachable: {ex.Message}");
        }
    }
}
