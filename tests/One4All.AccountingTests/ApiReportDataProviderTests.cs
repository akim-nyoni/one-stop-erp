using System.Net;
using FluentAssertions;
using One4All.Infrastructure.Reporting;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the web API report data source without a database: the command names a configured endpoint, parameters fill the path, the endpoint's
/// key is sent, rows are read from a bare array or a wrapped one, and an endpoint nobody configured is refused rather than called.
/// </summary>
public sealed class ApiReportDataProviderTests
{
    [Fact]
    public async Task Reports_read_rows_from_configured_web_apis_only()
    {
        List<HttpRequestMessage> calls = [];
        string reply = "[{\"station\":\"HRE\",\"rain\":12.5,\"wet\":true,\"note\":null},{\"station\":\"BYO\",\"rain\":0,\"wet\":false,\"extra\":{\"a\":1}}]";
        StubHandler handler = new(request =>
        {
            calls.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) });
        });
        ReportApiOptions options = new();
        options.Endpoints["weather"] = new ReportApiEndpoint { BaseUrl = "https://met.example.test/api/", AuthHeader = "X-Key", AuthValue = "k1" };
        ApiReportDataProvider provider = new(new HttpClient(handler), options);

        ReportDefinition definition = new() { Code = "RAIN", DataSources = [new ReportDataSource { Kind = DataSourceKind.Api, Command = "WEATHER/daily?from={FromDate}&to={ToDate}" }] };
        string command = ReportCommands.For(definition);
        provider.CanHandle(command).Should().BeTrue();
        provider.CanHandle("TrialBalance").Should().BeFalse();

        var rows = await provider.GetRowsAsync(1, command, new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) });
        calls.Single().RequestUri!.AbsoluteUri.Should().Be("https://met.example.test/api/daily?from=2026-03-01&to=2026-03-31");
        calls.Single().Headers.GetValues("X-Key").Should().Equal("k1");
        rows.Should().HaveCount(2);
        rows[0]["station"].Should().Be("HRE");
        rows[0]["rain"].Should().Be(12.5m);
        rows[0]["wet"].Should().Be(true);
        rows[0]["note"].Should().BeNull();
        rows[1]["extra"].Should().Be("{\"a\":1}");

        reply = "{\"value\":[{\"station\":\"MUT\"}]}";
        (await provider.GetRowsAsync(1, "api:weather/today", new Dictionary<string, object?>())).Single()["station"].Should().Be("MUT");

        await FluentActions.Invoking(() => provider.GetRowsAsync(1, "api:intranet/secrets", new Dictionary<string, object?>())).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*intranet*");
        calls.Should().HaveCount(2, "an endpoint nobody configured is never called");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
