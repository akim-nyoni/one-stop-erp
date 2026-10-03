using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the promise-to-pay diary: two overdue customers each promise 500 by 15 May. While the promises are open
/// neither is chased by dunning. One pays 500 on 10 May and the review marks that promise kept; the other pays nothing,
/// so a review on 16 May marks it broken and dunning chases them again. A second open promise for a customer is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PromiseToPayTests
{
    private readonly AccountingFixture _fixture;

    public PromiseToPayTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Promises_hold_dunning_and_are_reviewed_against_receipts()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer keeper = new(companyId, "PTP-K", "Keeps Promises", Scenario.Usd) { PaymentTermsDays = 0 };
        Customer breaker = new(companyId, "PTP-B", "Breaks Promises", Scenario.Usd) { PaymentTermsDays = 0 };
        db.Customers.AddRange(keeper, breaker);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach (Customer customer in new[] { keeper, breaker })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = $"{customer.Code}-I", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 800m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        IPromiseToPayService promises = scope.ServiceProvider.GetRequiredService<IPromiseToPayService>();
        DateOnly madeOn = new(2026, 5, 1);
        DateOnly payBy = new(2026, 5, 15);
        (await promises.RecordAsync(companyId, keeper.Id, madeOn, payBy, 500m, "Will pay from month-end receipts", 5)).IsSuccess.Should().BeTrue();
        (await promises.RecordAsync(companyId, breaker.Id, madeOn, payBy, 500m, null, 5)).IsSuccess.Should().BeTrue();
        (await promises.RecordAsync(companyId, breaker.Id, madeOn, payBy, 100m, null, 5)).Error.Code.Should().Be("Ar.PromiseOpen");
        (await promises.RecordAsync(companyId, breaker.Id, payBy, madeOn, 100m, null, 5)).Error.Code.Should().Be("Ar.PromiseInvalid");

        IDunningService dunning = scope.ServiceProvider.GetRequiredService<IDunningService>();
        (await dunning.AssessAsync(companyId, new DateOnly(2026, 5, 5), DunningStages.Default)).Value.Should().BeEmpty();

        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = keeper.Id, DocumentNumber = "PTP-R1", ReceiptDate = new DateOnly(2026, 5, 10), Currency = Scenario.Usd, Amount = 500m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        PromiseReview midway = await promises.ReviewAsync(companyId, new DateOnly(2026, 5, 12));
        midway.Kept.Should().ContainSingle().Which.CustomerCode.Should().Be("PTP-K");
        (midway.Broken.Count, midway.StillOpen).Should().Be((0, 1));

        PromiseReview after = await promises.ReviewAsync(companyId, new DateOnly(2026, 5, 16));
        after.Broken.Should().ContainSingle().Which.CustomerCode.Should().Be("PTP-B");
        (await promises.DiaryAsync(companyId, PromiseStatus.Open)).Should().BeEmpty();

        IReadOnlyList<DunningNotice> chased = (await dunning.AssessAsync(companyId, new DateOnly(2026, 5, 16), DunningStages.Default)).Value;
        chased.Select(n => n.CustomerName).Should().Contain("Breaks Promises");
    }
}
