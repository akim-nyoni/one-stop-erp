using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Infrastructure.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers report delivery to a folder: a subscription may save each run under the delivery root (stamped with the run, logged) instead of emailing
/// it, and a folder outside the root is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportFolderDeliveryTests
{
    private readonly AccountingFixture _fixture;

    public ReportFolderDeliveryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Subscriptions_save_runs_to_a_folder_under_the_root()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        string tag = Guid.NewGuid().ToString("N")[..8];
        string folder = Path.Combine("tests", tag);
        IReportSubscriptionService subscriptions = scope.ServiceProvider.GetRequiredService<IReportSubscriptionService>();

        SubscriptionRequest Request(string code, string target) => new()
        {
            CompanyId = companyId, Code = code, ReportCode = "AR29", Format = ReportFormat.Csv, DeliveryFolder = target,
            Parameters = new Dictionary<string, string> { ["AsAtDate"] = "2026-06-30" },
        };
        (await subscriptions.SubscribeAsync(Request($"FD-OUT-{tag}", Path.Combine("..", "..", "elsewhere")))).Error.Code.Should().Be("Rpt.DeliveryFolder");
        (await subscriptions.SubscribeAsync(Request($"FD-ABS-{tag}", Path.GetTempPath()))).Error.Code.Should().Be("Rpt.DeliveryFolder");
        (await subscriptions.SubscribeAsync(Request($"FD-{tag}", folder))).IsSuccess.Should().BeTrue();

        DateTimeOffset runAt = new(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);
        (await subscriptions.RunAsync(companyId, $"FD-{tag}", runAt)).Value.Should().Be(1);
        DeliveryLogEntry entry = (await subscriptions.DeliveryLogAsync(companyId, $"FD-{tag}")).Value.Should().ContainSingle().Subject;
        (entry.Succeeded, entry.Recipient).Should().Be((true, $"folder:{folder}"));
        entry.FileName.Should().StartWith("20260701-0600-");

        string saved = Path.Combine(scope.ServiceProvider.GetRequiredService<ReportDeliveryOptions>().Resolve(folder)!, entry.FileName);
        try
        {
            File.Exists(saved).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(saved)!, recursive: true);
        }
    }
}
