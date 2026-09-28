using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// End-to-end dunning assessment: overdue customers are assigned escalating reminder levels from the age of
/// their oldest overdue item, and a customer inside the first threshold is not dunned.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DunningAccountingTests
{
    private readonly AccountingFixture _fixture;

    public DunningAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Overdue_customers_are_assigned_escalating_reminder_levels()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long finalId;
        long firstId;
        long recentId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            Customer final = new(companyId, "C-FINAL", "Final Co", Scenario.Usd) { PaymentTermsDays = 0 };
            Customer first = new(companyId, "C-FIRST", "First Co", Scenario.Usd) { PaymentTermsDays = 0 };
            Customer recent = new(companyId, "C-RECENT", "Recent Co", Scenario.Usd) { PaymentTermsDays = 0 };
            db.Customers.AddRange(final, first, recent);
            await db.SaveChangesAsync();
            finalId = final.Id;
            firstId = first.Id;
            recentId = recent.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            // As at 2026-04-01: Final due 2026-01-01 (90d → final), First due 2026-03-15 (17d → first), Recent due 2026-03-28 (4d → none).
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = finalId, DocumentNumber = "INV-F", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 1000m, TaxAmount = 0m, SalesAccountCode = "4000" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = firstId, DocumentNumber = "INV-1", DocumentDate = new DateOnly(2026, 3, 15), Currency = Scenario.Usd, NetAmount = 500m, TaxAmount = 0m, SalesAccountCode = "4000" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = recentId, DocumentNumber = "INV-R", DocumentDate = new DateOnly(2026, 3, 28), Currency = Scenario.Usd, NetAmount = 200m, TaxAmount = 0m, SalesAccountCode = "4000" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IDunningService dunning = scope.ServiceProvider.GetRequiredService<IDunningService>();
            var result = await dunning.AssessAsync(companyId, new DateOnly(2026, 4, 1), DunningStages.Default);

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            IReadOnlyList<DunningNotice> notices = result.Value;

            notices.Should().HaveCount(2); // recent (4 days) is below the 7-day first threshold
            notices[0].Level.Should().Be(3); // ordered by level desc → final notice first
            notices[0].CustomerId.Should().Be(finalId);
            notices[0].OldestDaysOverdue.Should().Be(90);
            notices[0].OverdueAmount.Should().Be(1000m);
            notices[0].LevelLabel.Should().Be("Final notice");

            notices[1].Level.Should().Be(1);
            notices[1].CustomerId.Should().Be(firstId);

            notices.Should().NotContain(n => n.CustomerId == recentId);
        }
    }
}
