using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the fixed-charge municipal billing run: every property linked to a flat service is billed its
/// tariff's fixed charge on the ratepayer's account in one cycle.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MunicipalBillingRunTests
{
    private readonly AccountingFixture _fixture;

    public MunicipalBillingRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Fixed_charge_run_bills_every_linked_property()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            // A flat refuse tariff of 15, and two properties linked to it (plus one not linked).
            Tariff refuse = new(companyId, "REFUSE", "Refuse Collection", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 15m };
            db.Tariffs.Add(refuse);
            await db.SaveChangesAsync();

            for (int i = 1; i <= 2; i++)
            {
                Customer ratepayer = new(companyId, $"STAND-{i}", $"Ratepayer {i}", Scenario.Usd);
                db.Customers.Add(ratepayer);
                await db.SaveChangesAsync();
                Property property = new(companyId, i.ToString(), "Ward 1", ratepayer.Id);
                property.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
                db.Properties.Add(property);
            }

            // A property with no refuse service - must be skipped.
            Customer other = new(companyId, "STAND-9", "No refuse", Scenario.Usd);
            db.Customers.Add(other);
            await db.SaveChangesAsync();
            db.Properties.Add(new Property(companyId, "9", "Ward 2", other.Id));
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
            var run = await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Refuse, new DateOnly(2026, 3, 31), "REF");

            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.PropertiesBilled.Should().Be(2);   // only the two linked properties
            run.Value.TotalBilled.Should().Be(30m);       // 2 × 15
            run.Value.AlreadyBilled.Should().Be(0);

            // Re-running March - same day or another day in the month - bills nobody again.
            var again = await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Refuse, new DateOnly(2026, 3, 15), "REF");
            again.Value.PropertiesBilled.Should().Be(0);
            again.Value.TotalBilled.Should().Be(0m);
            again.Value.AlreadyBilled.Should().Be(2);

            (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Refuse, new DateOnly(2026, 3, 31), " ")).Error.Code.Should().Be("Mun.PrefixRequired");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 30m); // refuse revenue
        }
    }
}
