using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using One4All.Application.Abstractions.Communications;
using One4All.Infrastructure.Communications;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the outbound gateways without a database: SMS posts the filled template with the provider's auth header and reports refusals;
/// email speaks SMTP to a local test server, attachment included; unconfigured or malformed recipients are refused without sending.
/// </summary>
public sealed class MessagingGatewayTests
{
    [Fact]
    public async Task Sms_posts_the_filled_template_and_reports_refusals()
    {
        HttpRequestMessage? seen = null;
        string? sentBody = null;
        HttpStatusCode status = HttpStatusCode.OK;
        StubHandler handler = new(async request =>
        {
            seen = request;
            sentBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? "{\"id\":\"msg-1\"}" : "bad key") };
        });
        MessagingOptions options = new() { Sms = new SmsOptions { Url = "https://sms.example.test/send", AuthHeader = "X-Api-Key", AuthValue = "k1", From = "ONE4ALL" } };
        HttpSmsGateway gateway = new(new HttpClient(handler), options);

        GatewayResult sent = await gateway.SendAsync(1, "+263 77 000 0001", string.Empty, "Fees due");
        (sent.Accepted, sent.Reference).Should().Be((true, "{\"id\":\"msg-1\"}"));
        seen!.Headers.GetValues("X-Api-Key").Should().Equal("k1");
        sentBody.Should().Be("{\"to\":\"+263770000001\",\"from\":\"ONE4ALL\",\"text\":\"Fees due\"}");

        status = HttpStatusCode.Unauthorized;
        (await gateway.SendAsync(1, "+263770000001", string.Empty, "x")).Should().Be(new GatewayResult(false, "Provider refused (401): bad key"));
        (await gateway.SendAsync(1, "not a number", string.Empty, "x")).Accepted.Should().BeFalse();
        (await new HttpSmsGateway(new HttpClient(handler), new MessagingOptions()).SendAsync(1, "+263770000001", string.Empty, "x")).Reference.Should().Contain("not configured");
    }

    [Fact]
    public async Task Email_is_delivered_over_smtp_with_its_attachment()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<string> server = ServeOneMessageAsync(listener);

        SmtpEmailGateway gateway = new(new MessagingOptions { Smtp = new SmtpOptions { Host = "127.0.0.1", Port = port, UseTls = false, From = "erp@example.test", FromName = "One4All" } });
        GatewayResult sent = await gateway.SendAsync(1, "parent@example.test", "Fee statement", "Please find your statement attached.",
            new MessageAttachment("statement.txt", "text/plain", Encoding.UTF8.GetBytes("Balance 50.00")));
        string data = await server;

        sent.Accepted.Should().BeTrue(sent.Reference);
        data.Should().Contain("Subject: Fee statement").And.Contain("parent@example.test").And.Contain("statement.txt");
        (await gateway.SendAsync(1, "nobody", "s", "b")).Accepted.Should().BeFalse();
        (await new SmtpEmailGateway(new MessagingOptions()).SendAsync(1, "a@b.test", "s", "b")).Reference.Should().Contain("not configured");
    }

    // A minimal SMTP server: accepts one message and returns its DATA section.
    private static async Task<string> ServeOneMessageAsync(TcpListener listener)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        using NetworkStream stream = client.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII);
        using StreamWriter writer = new(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 test ready");
        StringBuilder data = new();
        while (await reader.ReadLineAsync() is string line)
        {
            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 test");
            }
            else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("354 go ahead");
                while (await reader.ReadLineAsync() is string body && body != ".")
                {
                    data.AppendLine(body);
                }

                await writer.WriteLineAsync("250 queued");
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 bye");
                break;
            }
            else
            {
                await writer.WriteLineAsync("250 ok");
            }
        }

        return data.ToString();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
