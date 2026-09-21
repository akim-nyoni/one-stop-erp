using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PaymentArrangementTests
{
    private readonly AccountingFixture _fixture;

    public PaymentArrangementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Captured_arrangement_appears_on_rc42()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer c = new(companyId, "ARR-1", "Arrangement Payer", Scenario.Usd);
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            customerId = c.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            (await rcp.CaptureArrangementAsync(companyId, customerId, 1200m, 100m, new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();

            // Guard: instalment larger than total is refused.
            (await rcp.CaptureArrangementAsync(companyId, customerId, 100m, 200m, new DateOnly(2026, 1, 1))).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var result = await runner.RenderAsync(companyId, "RC42", new Dictionary<string, object?>(), ReportFormat.Html);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string html = Encoding.UTF8.GetString(result.Value.Content);
            html.Should().Contain("ARR-1").And.Contain("Active").And.Contain("100.00");
        }
    }
}
