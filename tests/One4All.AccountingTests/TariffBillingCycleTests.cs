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
/// Covers tariff billing cycles: an annual July rates charge is raised once by the first run in the year (later monthly runs skip it), and a
/// stand connected part-way through the year pays its share; a quarterly charge billed in two instalments raises half in each of the
/// first two months of the quarter through the reviewed billing run and nothing in the third.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TariffBillingCycleTests
{
    private readonly AccountingFixture _fixture;

    public TariffBillingCycleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Annual_and_instalment_charges_are_billed_once_per_period()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff rates = new(companyId, "RATES-CY", "Annual Rates", ServiceType.Other, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 1200m };
        Tariff refuse = new(companyId, "REF-CY", "Refuse (quarterly)", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 300m };
        db.Tariffs.AddRange(rates, refuse);
        Customer owner = new(companyId, "CY-OWN", "Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();
        Property early = new(companyId, "CY1", "Ward 3", owner.Id), late = new(companyId, "CY2", "Ward 3", owner.Id);
        early.AddService(ServiceType.Other, rates.Id, new DateOnly(2026, 1, 1));
        late.AddService(ServiceType.Other, rates.Id, new DateOnly(2026, 10, 1));
        early.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
        db.Properties.AddRange(early, late);
        await db.SaveChangesAsync();

        IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
        (await billing.SetBillingCycleAsync(companyId, "REF-CY", BillingCycle.Quarterly, 1, 4)).Error.Code.Should().Be("Mun.Cycle");
        (await billing.SetBillingCycleAsync(companyId, "rates-cy", BillingCycle.Annual, 7, 1)).IsSuccess.Should().BeTrue();
        (await billing.SetBillingCycleAsync(companyId, "REF-CY", BillingCycle.Quarterly, 1, 2)).IsSuccess.Should().BeTrue();

        // Annual July rates: August raises the year's bill (numbered for July); September finds it raised.
        MunicipalBillingRunResult august = (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Other, new DateOnly(2026, 8, 31), "CY")).Value;
        (august.PropertiesBilled, august.TotalBilled).Should().Be((1, 1200m));
        (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Other, new DateOnly(2026, 9, 30), "CY")).Value.PropertiesBilled.Should().Be(0);

        // Connected on 1 October: 273 of the year's 365 days.
        MunicipalBillingRunResult november = (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Other, new DateOnly(2026, 11, 30), "CY")).Value;
        (november.PropertiesBilled, november.TotalBilled).Should().Be((1, 897.53m));
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("CY-")).Select(i => i.DocumentNumber).ToListAsync())
            .Should().BeEquivalentTo([$"CY-{early.Id}-202607", $"CY-{late.Id}-202607"]);

        // Quarterly refuse in two instalments through the reviewed run: 150 in January and February, nothing in March.
        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        foreach ((int month, decimal expected) in new[] { (1, 150m), (2, 150m) })
        {
            BillingRunSummary run = (await runs.ProposeAsync(new BillingRunProposal
            {
                CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, month, 28), InvoicePrefix = "RQ", UserId = 61,
            })).Value;
            (run.Included, run.Total).Should().Be((1, expected));
            (await runs.ApproveAsync(companyId, run.RunNumber, 62)).IsSuccess.Should().BeTrue();
        }

        var march = await runs.ProposeAsync(new BillingRunProposal { CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 3, 28), InvoicePrefix = "RQ", UserId = 61 });
        (march.IsFailure || march.Value.Included == 0).Should().BeTrue();
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("RQ-")).SumAsync(i => i.OriginalBase)).Should().Be(300m);
    }
}
