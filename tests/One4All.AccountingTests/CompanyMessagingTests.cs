using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Application.Abstractions.Communications;
using One4All.Core.Communications;
using One4All.Infrastructure.Communications;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers per-company messaging: a company's own SMS provider is used for its messages while another company falls back to the server's
/// (here none); a company on Microsoft Graph gets an app token and a sendMail call on its mailbox; secrets are stored encrypted and kept when
/// left blank, and never shown back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CompanyMessagingTests
{
    private readonly AccountingFixture _fixture;

    public CompanyMessagingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Companies_send_through_their_own_sms_provider_and_graph_mailbox()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long own = await Scenario.SeedAsync(db), other = await Scenario.SeedAsync(db);
        ICompanyMessagingService messaging = scope.ServiceProvider.GetRequiredService<ICompanyMessagingService>();

        (await messaging.SaveSmsAsync(own, new CompanySmsSettings("not a url"))).Error.Code.Should().Be("Msg.SmsUrl");
        (await messaging.SaveSmsAsync(own, new CompanySmsSettings("https://sms.council.test/send", AuthHeader: "X-Key", AuthValue: "council-key", From: "COUNCIL"))).IsSuccess.Should().BeTrue();
        (await messaging.SaveEmailAsync(own, new CompanyEmailSettings(EmailProvider.MicrosoftGraph, "rates@council.test"))).Error.Code.Should().Be("Msg.Graph");
        (await messaging.SaveEmailAsync(own, new CompanyEmailSettings(EmailProvider.MicrosoftGraph, "rates@council.test", GraphTenantId: "tenant-1", GraphClientId: "app-1",
            GraphClientSecret: "s3cret"))).IsSuccess.Should().BeTrue();
        (await messaging.SaveEmailAsync(own, new CompanyEmailSettings(EmailProvider.MicrosoftGraph, "rates@council.test", GraphTenantId: "tenant-1", GraphClientId: "app-1")))
            .IsSuccess.Should().BeTrue("a blank secret keeps the stored one");

        List<string> calls = [];
        StubHandler handler = new(async request =>
        {
            calls.Add($"{request.Method} {request.RequestUri!.AbsoluteUri} {request.Headers.Authorization} {(request.Headers.TryGetValues("X-Key", out var key) ? key.Single() : string.Empty)}");
            string content = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            if (request.RequestUri!.AbsoluteUri.Contains("/oauth2/v2.0/token", StringComparison.Ordinal))
            {
                content.Should().Contain("client_secret=s3cret").And.Contain("client_id=app-1");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok-1\"}") };
            }

            return new HttpResponseMessage(request.RequestUri.AbsoluteUri.Contains("sendMail", StringComparison.Ordinal) ? HttpStatusCode.Accepted : HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };
        });
        HttpClient http = new(handler);

        HttpSmsGateway sms = new(http, new MessagingOptions(), messaging);
        (await sms.SendAsync(own, "+263770000001", string.Empty, "Rates due")).Accepted.Should().BeTrue();
        calls.Last().Should().StartWith("POST https://sms.council.test/send").And.EndWith("council-key");
        (await sms.SendAsync(other, "+263770000001", string.Empty, "Rates due")).Reference.Should().Contain("not configured");

        SmtpEmailGateway email = new(new MessagingOptions(), messaging, http);
        GatewayResult mailed = await email.SendAsync(own, "owner@example.test", "Statement", "Attached.", new MessageAttachment("s.txt", "text/plain", [1, 2, 3]));
        mailed.Accepted.Should().BeTrue(mailed.Reference);
        calls.Should().Contain(c => c.StartsWith("POST https://login.microsoftonline.com/tenant-1/oauth2/v2.0/token", StringComparison.Ordinal));
        calls.Should().Contain(c => c.StartsWith("POST https://graph.microsoft.com/v1.0/users/rates%40council.test/sendMail Bearer tok-1", StringComparison.Ordinal));
        (await email.SendAsync(other, "owner@example.test", "s", "b")).Reference.Should().Contain("not configured");

        (await messaging.DescribeAsync(own)).Should().Contain("Microsoft Graph").And.Contain("secret set").And.NotContain("s3cret").And.NotContain("council-key");
        long row = await db.Set<CompanyMessagingSettings>().Where(s => s.CompanyId == own).Select(s => s.Id).SingleAsync();
        await using NpgsqlConnection connection = new(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand read = new($"SELECT graph_client_secret FROM sys.company_messaging WHERE id = {row}", connection);
        ((string)(await read.ExecuteScalarAsync())!).Should().NotBe("s3cret", "secrets are encrypted at rest");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
