using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.UnitTests.Licensing;

/// <summary>Covers the online activation client: the request sent, the answer read, and failures turned into refusals.</summary>
public sealed class OnlineActivationClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return answer(request);
        }
    }

    private static HttpOnlineActivationClient Client(StubHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://activate.example.com/") });

    [Fact]
    public async Task A_granted_activation_returns_the_licence()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { granted = true, licenceToken = "LIC.abc", message = "Welcome" }), Encoding.UTF8, "application/json"),
        });
        OnlineActivationResponse answer = await Client(handler).ActivateAsync(new OnlineActivationRequest("RC1.xyz", "CUST-1", null));
        (answer.Granted, answer.LicenceToken, answer.Message).Should().Be((true, "LIC.abc", "Welcome"));
        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/activate");
        handler.LastBody.Should().Contain("\"requestCode\":\"RC1.xyz\"").And.Contain("\"customerKey\":\"CUST-1\"");
    }

    [Fact]
    public async Task Service_errors_and_outages_come_back_as_refusals()
    {
        (await Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))).RenewAsync(new OnlineActivationRequest("RC1", null, "L-1")))
            .Should().Match<OnlineActivationResponse>(r => !r.Granted && r.Message!.Contains("403"));
        (await Client(new StubHandler(_ => throw new HttpRequestException("no route"))).ActivateAsync(new OnlineActivationRequest("RC1", null, null)))
            .Should().Match<OnlineActivationResponse>(r => !r.Granted && r.Message!.Contains("could not be reached"));
        (await new OfflineOnlyActivationClient().ActivateAsync(new OnlineActivationRequest("RC1", null, null))).Granted.Should().BeFalse();
    }

    [Fact]
    public void Only_https_services_are_accepted()
        => FluentActions.Invoking(() => new HttpOnlineActivationClient(new HttpClient { BaseAddress = new Uri("http://activate.example.com/") }))
            .Should().Throw<ArgumentException>();
}
