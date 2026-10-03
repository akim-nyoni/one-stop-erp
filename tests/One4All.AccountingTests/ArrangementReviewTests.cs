using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Receipting;
using ReceiptRequest = One4All.Application.Abstractions.Trading.ReceiptRequest;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers payment arrangement default tracking: a debtor paying 200 a month on a 600 arrangement is honoured; one who paid
/// only the first 300 of 900 has missed two instalments by 31 March and defaults; one whose first instalment of 100 is
/// unpaid stays active with one missed; a second review sees only the arrangement still active.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ArrangementReviewTests
{
    private readonly AccountingFixture _fixture;

    public ArrangementReviewTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Arrangements_are_honoured_or_defaulted_against_their_schedules()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer keeper = new(companyId, "AR-KEEP", "Keeps Promises", Scenario.Usd);
        Customer lapsed = new(companyId, "AR-LAPSE", "Lapsed Payer", Scenario.Usd);
        Customer fresh = new(companyId, "AR-NEW", "New Arrangement", Scenario.Usd);
        db.Customers.AddRange(keeper, lapsed, fresh);
        await db.SaveChangesAsync();

        IReceiptingService receipting = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
        long kept = (await receipting.CaptureArrangementAsync(companyId, keeper.Id, 600m, 200m, new DateOnly(2026, 1, 10))).Value;
        long missed = (await receipting.CaptureArrangementAsync(companyId, lapsed.Id, 900m, 300m, new DateOnly(2026, 1, 10))).Value;
        long started = (await receipting.CaptureArrangementAsync(companyId, fresh.Id, 400m, 100m, new DateOnly(2026, 3, 1))).Value;

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach ((Customer customer, DateOnly date, decimal amount) in new[]
        {
            (keeper, new DateOnly(2026, 1, 12), 200m), (keeper, new DateOnly(2026, 2, 8), 200m), (keeper, new DateOnly(2026, 3, 9), 200m), (lapsed, new DateOnly(2026, 1, 11), 300m),
        })
        {
            (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = $"{customer.Code}-{date:MMdd}", ReceiptDate = date, Currency = Scenario.Usd, Amount = amount, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        }

        IArrangementMonitor monitor = scope.ServiceProvider.GetRequiredService<IArrangementMonitor>();
        (await monitor.ScheduleAsync(companyId, missed)).Value.Should().HaveCount(3);
        (await monitor.ScheduleAsync(companyId, -1)).Error.Code.Should().Be("Rcp.ArrangementNotFound");

        IReadOnlyList<ArrangementReview> review = await monitor.ReviewAsync(companyId, new DateOnly(2026, 3, 31), 7, 2);
        review.Select(r => (r.ArrangementId, r.Status)).Should().Equal((kept, ArrangementStatus.Honoured), (missed, ArrangementStatus.Defaulted), (started, ArrangementStatus.Active));
        review.Single(r => r.ArrangementId == missed).Standing.Should().Match<Application.Trading.ArrangementStanding>(s => s.InstalmentsMissed == 2 && s.Shortfall == 600m);
        review.Single(r => r.ArrangementId == started).Standing.InstalmentsMissed.Should().Be(1);

        (await monitor.ReviewAsync(companyId, new DateOnly(2026, 3, 31), 7, 2)).Select(r => r.ArrangementId).Should().Equal(started);
    }
}
