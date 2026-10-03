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
/// Covers effective-dated tariff versions: a stand linked to the January version of a refuse tariff (10 a month) bills 10
/// in June and 12 in July once the July version takes effect, without relinking; a version that is switched off is
/// passed over.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TariffVersionTests
{
    private readonly AccountingFixture _fixture;

    public TariffVersionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bills_use_the_tariff_version_in_force_on_the_bill_date()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff january = new(companyId, "REF-DOM", "Domestic Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 10m };
        Tariff july = new(companyId, "REF-DOM", "Domestic Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 7, 1), "4000") { FixedCharge = 12m };
        Tariff withdrawn = new(companyId, "REF-DOM", "Domestic Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 8, 1), "4000") { FixedCharge = 99m, IsActive = false };
        db.Tariffs.AddRange(january, july, withdrawn);
        Customer owner = new(companyId, "TV-OWN", "Stand Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "1001", "Ward 11", owner.Id);
        stand.AddService(ServiceType.Refuse, january.Id, new DateOnly(2026, 1, 1));
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
        foreach ((int month, decimal expected) in new[] { (6, 10m), (7, 12m), (8, 12m) })
        {
            (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Refuse, new DateOnly(2026, month, 15), "TV")).Value.TotalBilled.Should().Be(expected);
        }

        (await db.OpenItems.AsNoTracking().CountAsync(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("TV-"))).Should().Be(3);
    }
}
