using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the reviewed billing run for Ward 1 refuse at 30 a month: stands billed 30 last month, never billed, already
/// billed this month, last billed 10 (a large change) and on a zero tariff are proposed without posting; the exception
/// report lists the last three; the already-billed and zero lines are left out and cannot be put back; a second proposal
/// for the month is refused; the large-change bill is left out by the reviewer; the proposer cannot approve; another
/// user approves and two bills of 30 post; a posted run cannot be cancelled; a cancelled May run can be proposed again.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReviewedBillingRunTests
{
    private readonly AccountingFixture _fixture;

    public ReviewedBillingRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bills_are_proposed_reviewed_and_posted_on_approval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff refuse = new(companyId, "REF-RUN", "Refuse Removal", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 30m };
        Tariff free = new(companyId, "REF-FREE", "Refuse Exempt", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 0m };
        db.Tariffs.AddRange(refuse, free);
        Customer owner = new(companyId, "BR-OWN", "Stand Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        Property Stand(string number, string ward, Tariff tariff)
        {
            Property p = new(companyId, number, ward, owner.Id);
            p.AddService(ServiceType.Refuse, tariff.Id, new DateOnly(2026, 1, 1));
            return p;
        }

        Property regular = Stand("B1", "Ward 1", refuse);
        Property newcomer = Stand("B2", "Ward 1", refuse);
        Property billed = Stand("B3", "Ward 1", refuse);
        Property jumped = Stand("B4", "Ward 1", refuse);
        Property exempt = Stand("B5", "Ward 1", free);
        Property elsewhere = Stand("B6", "Ward 2", refuse);
        db.Properties.AddRange(regular, newcomer, billed, jumped, exempt, elsewhere);
        await db.SaveChangesAsync();

        IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
        (await billing.BillServiceAsync(companyId, regular.Id, ServiceType.Refuse, 0m, "MAR-B1", new DateOnly(2026, 3, 15))).IsSuccess.Should().BeTrue();
        (await billing.BillServiceAsync(companyId, billed.Id, ServiceType.Refuse, 0m, $"RB-{billed.Id}-202604", new DateOnly(2026, 4, 2))).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = owner.Id, DocumentNumber = "MAR-B4", DocumentDate = new DateOnly(2026, 3, 15), Currency = Scenario.Usd, NetAmount = 10m,
            SalesAccountCode = "4000", Narrative = "Refuse Removal — stand B4",
        })).IsSuccess.Should().BeTrue();

        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        BillingRunProposal April(long user) => new() { CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 4, 30), Ward = "Ward 1", InvoicePrefix = "RB", UserId = user };
        BillingRunSummary proposed = (await runs.ProposeAsync(April(71))).Value;
        proposed.Should().Be(new BillingRunSummary(proposed.RunNumber, BillingRunStatus.Proposed, 5, 3, 90m, 3));
        (await db.OpenItems.AsNoTracking().CountAsync(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("RB-") && i.DocumentNumber.EndsWith("-202604"))).Should().Be(1);
        (await runs.ProposeAsync(April(71))).Error.Code.Should().Be("Mun.RunPending");

        IReadOnlyList<BillingRunLineView> exceptions = (await runs.LinesAsync(companyId, proposed.RunNumber, exceptionsOnly: true)).Value;
        exceptions.Select(l => (l.StandNumber, l.Exceptions, l.Included)).Should().Equal(
            ("B3", BillingExceptions.AlreadyBilled, false), ("B4", BillingExceptions.LargeChange, true), ("B5", BillingExceptions.NothingToBill, false));
        exceptions.Single(l => l.StandNumber == "B4").PreviousCharge.Should().Be(10m);

        (await runs.SetIncludedAsync(companyId, proposed.RunNumber, exempt.Id, true)).Error.Code.Should().Be("Mun.LineBlocked");
        (await runs.SetIncludedAsync(companyId, proposed.RunNumber, jumped.Id, false)).Value.Total.Should().Be(60m);

        (await runs.ApproveAsync(companyId, proposed.RunNumber, 71)).Error.Code.Should().Be("Mun.SelfApproval");
        BillingRunSummary posted = (await runs.ApproveAsync(companyId, proposed.RunNumber, 72)).Value;
        posted.Status.Should().Be(BillingRunStatus.Posted);
        posted.Included.Should().Be(2);
        foreach (Property stand in new[] { regular, newcomer })
        {
            (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == $"RB-{stand.Id}-202604")).OriginalBase.Should().Be(30m);
        }

        (await db.OpenItems.AsNoTracking().AnyAsync(i => i.CompanyId == companyId && i.DocumentNumber == $"RB-{jumped.Id}-202604")).Should().BeFalse();
        (await runs.CancelAsync(companyId, proposed.RunNumber)).Error.Code.Should().Be("Mun.RunNotProposed");

        BillingRunProposal May = new() { CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 5, 31), Ward = "Ward 1", InvoicePrefix = "RB", UserId = 71 };
        string first = (await runs.ProposeAsync(May)).Value.RunNumber;
        (await runs.CancelAsync(companyId, first)).IsSuccess.Should().BeTrue();
        (await runs.ProposeAsync(May)).Value.RunNumber.Should().NotBe(first);
    }
}
