using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers report delivery to a printer: a subscription with only a printer is accepted, each run prints the rendered report on that printer
/// and logs it; a printer on a burst subscription is refused; a printer failure is logged and fails the run.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportPrinterDeliveryTests
{
    private readonly AccountingFixture _fixture;

    public ReportPrinterDeliveryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Subscriptions_print_on_their_printer_and_log_it()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        RecordingPrinter printer = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(services => services.AddSingleton<IReportPrinter>(printer));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

        IReportSubscriptionService subscriptions = scope.ServiceProvider.GetRequiredService<IReportSubscriptionService>();
        long owner = await ReportUsers.CreateAsync(scope.ServiceProvider, "printer");
        Dictionary<string, string> monthEnd = new() { ["AsAtDate"] = "@PrevMonthEnd" };
        (await subscriptions.SubscribeAsync(new SubscriptionRequest
        {
            CompanyId = companyId, UserId = owner, Code = "TB-BURST", ReportCode = SystemReports.TrialBalanceCode, Parameters = monthEnd, BurstKeyField = "AccountCode", BurstRecipientField = "Email",
            DeliveryPrinter = "Finance",
        })).Error.Code.Should().Be("Rpt.DeliveryPrinter");
        (await subscriptions.SubscribeAsync(new SubscriptionRequest
        {
            CompanyId = companyId, UserId = owner, Code = "TB-PRINT", ReportCode = SystemReports.TrialBalanceCode, Format = ReportFormat.Html, Parameters = monthEnd, DeliveryPrinter = "Finance Laser",
        })).IsSuccess.Should().BeTrue();

        DateTimeOffset june = new(2026, 6, 3, 6, 0, 0, TimeSpan.Zero);
        (await subscriptions.RunAsync(companyId, "TB-PRINT", june)).Value.Should().Be(1);
        printer.Printed.Should().ContainSingle().Which.Printer.Should().Be("Finance Laser");

        printer.Fail = true;
        (await subscriptions.RunAsync(companyId, "TB-PRINT", june.AddMonths(1))).IsFailure.Should().BeTrue();
        IReadOnlyList<DeliveryLogEntry> log = (await subscriptions.DeliveryLogAsync(companyId, "TB-PRINT")).Value;
        log.Should().Contain(e => e.Recipient == "printer:Finance Laser" && e.Succeeded).And.Contain(e => e.Recipient == "printer:Finance Laser" && !e.Succeeded && e.Detail == "Paper jam");
    }

    private sealed class RecordingPrinter : IReportPrinter
    {
        public List<(string Printer, string FileName)> Printed { get; } = [];

        public bool Fail { get; set; }

        public Task<Result> PrintAsync(string printerName, RenderedDocument document, CancellationToken ct = default)
        {
            if (Fail)
            {
                return Task.FromResult(Result.Failure(Error.Failure("Test.Jam", "Paper jam")));
            }

            Printed.Add((printerName, document.FileName));
            return Task.FromResult(Result.Success());
        }
    }
}
