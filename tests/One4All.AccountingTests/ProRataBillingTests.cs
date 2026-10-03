using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers pro-rata billing in the fixed-charge run: a 30 refuse charge bills in full for a stand served all April, 20 for
/// one connected on 11 April and 15 for one whose service ended on 15 April; a stand that ended in March and one
/// connected in May are not billed; in May the ended stand drops out and the new stand bills in full.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ProRataBillingTests
{
    private readonly AccountingFixture _fixture;

    public ProRataBillingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Part_months_are_billed_pro_rata()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff refuse = new(companyId, "REFUSE", "Refuse Removal", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 30m };
        db.Tariffs.Add(refuse);
        Customer owner = new(companyId, "PR-OWN", "Stand Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        Property Stand(string number, DateOnly start, DateOnly? end)
        {
            Property p = new(companyId, number, "Ward 9", owner.Id);
            PropertyServiceLink link = p.AddService(ServiceType.Refuse, refuse.Id, start);
            if (end is DateOnly last)
            {
                link.EndDate = last;
                link.IsActive = false;
            }

            return p;
        }

        Property whole = Stand("801", new DateOnly(2026, 1, 1), null);
        Property joined = Stand("802", new DateOnly(2026, 4, 11), null);
        Property left = Stand("803", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 15));
        Property gone = Stand("804", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        Property future = Stand("805", new DateOnly(2026, 5, 1), null);
        db.Properties.AddRange(whole, joined, left, gone, future);
        await db.SaveChangesAsync();

        IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
        MunicipalBillingRunResult april = (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Refuse, new DateOnly(2026, 4, 30), "RF")).Value;
        april.PropertiesBilled.Should().Be(3);
        april.TotalBilled.Should().Be(65m);
        (await Billed($"RF-{joined.Id}-202604")).Should().Be(20m);
        (await Billed($"RF-{left.Id}-202604")).Should().Be(15m);

        MunicipalBillingRunResult may = (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Refuse, new DateOnly(2026, 5, 31), "RF")).Value;
        may.PropertiesBilled.Should().Be(3);
        may.TotalBilled.Should().Be(90m);

        async Task<decimal> Billed(string number)
            => (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == number)).OriginalBase;
    }
}
