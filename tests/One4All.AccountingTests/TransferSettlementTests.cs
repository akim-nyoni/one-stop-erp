using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the pro-rata settlement on a property transfer: the month's refuse charge and the year's rates billed to the seller are shared by
/// the days each party owns the stand; the buyer's share is credited to the seller and invoiced to the buyer, once.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TransferSettlementTests
{
    private readonly AccountingFixture _fixture;

    public TransferSettlementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_transfer_settles_the_periods_charges_by_the_days_each_party_owns_the_stand()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer seller = new(companyId, "TS-SELL", "Settling Seller", Scenario.Usd), buyer = new(companyId, "TS-BUY", "Settling Buyer", Scenario.Usd);
        Tariff refuse = new(companyId, "TS-REF", "Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 300m };
        Tariff rates = new(companyId, "TS-RATES", "Rates", ServiceType.Other, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 3650m };
        rates.SetCycle(BillingCycle.Annual, 1, 1);
        db.AddRange(seller, buyer, refuse, rates);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "TS-9", "Ward 2", seller.Id);
        stand.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
        stand.AddService(ServiceType.Other, rates.Id, new DateOnly(2026, 1, 1));
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        IOwnershipService ownership = scope.ServiceProvider.GetRequiredService<IOwnershipService>();
        (await ownership.RegisterOwnersAsync(companyId, stand.Id, [new OwnerShare(seller.Id, 100m, true)], new DateOnly(2020, 1, 1), null)).IsSuccess.Should().BeTrue();
        DateOnly transfer = new(2026, 4, 11);
        (await ownership.SettleTransferAsync(companyId, stand.Id, transfer)).Error.Code.Should().Be("Mun.NoTransfer");
        string certificate = (await scope.ServiceProvider.GetRequiredService<IClearanceService>().IssueAsync(companyId, stand.Id, new DateOnly(2026, 4, 1), 0, 60, 71)).Value.CertificateNumber;
        (await ownership.TransferAsync(companyId, stand.Id, [new OwnerShare(buyer.Id, 100m, true)], transfer, certificate, null)).IsSuccess.Should().BeTrue();

        IReadOnlyList<TransferSettlementLine> settled = (await ownership.SettleTransferAsync(companyId, stand.Id, transfer)).Value;
        settled.Should().BeEquivalentTo([
            new TransferSettlementLine("TS-REF", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), 300m, 300m, 100m, 200m),
            new TransferSettlementLine("TS-RATES", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 3650m, 3650m, 1000m, 2650m),
        ], "the seller owned 10 of April's 30 days and 100 of the year's 365");

        List<string> documents = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.Narrative!.StartsWith("Transfer of stand TS-9")).Select(j => j.Narrative!).ToListAsync();
        documents.Should().HaveCount(2, "a credit note to the seller and an invoice to the buyer");
        (await ownership.SettleTransferAsync(companyId, stand.Id, transfer)).Error.Code.Should().Be("Mun.TransferSettled");
    }
}
