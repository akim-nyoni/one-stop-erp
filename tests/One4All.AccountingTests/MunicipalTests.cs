using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class MunicipalTests
{
    private readonly AccountingFixture _fixture;

    public MunicipalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Metered_service_bills_ratepayer_through_ar_and_balances()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long propertyId;
        long meterId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            Customer ratepayer = new(companyId, "STAND-42", "J. Ratepayer", Scenario.Usd);
            db.Customers.Add(ratepayer);

            // Stepped domestic water tariff: 5 fixed + first 10 m³ @ 0.5 + next 20 @ 1.0 + above 30 @ 1.5.
            Tariff water = new(companyId, "WATER-DOM", "Domestic Water", ServiceType.Water, TariffType.Stepped, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 5m };
            water.AddBlock(0m, 10m, 0.5m);
            water.AddBlock(10m, 30m, 1.0m);
            water.AddBlock(30m, null, 1.5m);
            db.Tariffs.Add(water);

            Meter meter = new(companyId, "WM-0001");
            db.Meters.Add(meter);
            await db.SaveChangesAsync();

            Property property = new(companyId, "42", "Ward 3", ratepayer.Id);
            PropertyServiceLink link = property.AddService(ServiceType.Water, water.Id, new DateOnly(2026, 1, 1));
            link.MeterId = meter.Id;
            db.Properties.Add(property);
            await db.SaveChangesAsync();

            propertyId = property.Id;
            meterId = meter.Id;
        }

        decimal consumption;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
            var reading = await billing.RecordReadingAsync(companyId, meterId, new DateOnly(2026, 3, 31), 25m);
            reading.IsSuccess.Should().BeTrue(reading.IsFailure ? reading.Error.ToString() : null);
            consumption = reading.Value;
            consumption.Should().Be(25m); // 25 − 0 previous
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
            var bill = await billing.BillServiceAsync(companyId, propertyId, ServiceType.Water, consumption, "MUN-INV-1", new DateOnly(2026, 3, 31));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Charge.Should().Be(25m); // 5 + 10×0.5 + 15×1.0
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31));
            debtors.Total.Should().Be(25m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
        }
    }
}
