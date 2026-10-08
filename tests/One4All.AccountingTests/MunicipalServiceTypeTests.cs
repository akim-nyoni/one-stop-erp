using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers council-defined services (brief §4.9): a service type billed like a built-in category, connected with a quantity and a deposit invoiced to the
/// ratepayer; a billing run for that service bills only its active connections at the tariff times the quantity, leaving suspended ones out.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MunicipalServiceTypeTests
{
    private readonly AccountingFixture _fixture;

    public MunicipalServiceTypeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Council_services_bill_by_quantity_take_deposits_and_skip_suspended_connections()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2491", "Service deposits", AccountType.Liability));
        Tariff bins = new(companyId, "BIN-EXTRA", "Extra bin", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 15m };
        Tariff standard = new(companyId, "REF-STD", "Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 20m };
        db.Tariffs.AddRange(bins, standard);
        Customer owner = new(companyId, "MS-1", "Owner One", Scenario.Usd);
        Customer other = new(companyId, "MS-2", "Owner Two", Scenario.Usd);
        db.Customers.AddRange(owner, other);
        await db.SaveChangesAsync();
        Property first = new(companyId, "MS-S1", "Ward 4", owner.Id);
        first.AddService(ServiceType.Refuse, standard.Id, new DateOnly(2026, 1, 1));
        Property second = new(companyId, "MS-S2", "Ward 4", other.Id);
        db.Properties.AddRange(first, second);
        await db.SaveChangesAsync();

        IMunicipalServiceTypeService services = scope.ServiceProvider.GetRequiredService<IMunicipalServiceTypeService>();
        (await services.SaveAsync(companyId, "BINS", "Extra refuse bins", ServiceType.Refuse, false, 50m, null)).Error.Code.Should().Be("Mun.ServiceType");
        (await services.SaveAsync(companyId, "bins", "Extra refuse bins", ServiceType.Refuse, false, 50m, "2491")).IsSuccess.Should().BeTrue();

        string? deposit = (await services.ConnectAsync(companyId, "ms-s1", "BINS", "BIN-EXTRA", new DateOnly(2026, 3, 1), 3m, null)).Value;
        deposit.Should().Be("DEP-MS-S1-BINS-20260301");
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.CompanyId == companyId && o.DocumentNumber == deposit)).OriginalBase.Should().Be(50m);
        (await services.ConnectAsync(companyId, "MS-S1", "BINS", "BIN-EXTRA", new DateOnly(2026, 3, 1), 1m, null)).Error.Code.Should().Be("Mun.AlreadyConnected");
        (await services.ConnectAsync(companyId, "MS-S2", "BINS", "REF-STD", new DateOnly(2026, 3, 1), 2m, 0m)).Value.Should().BeNull("no deposit asked");

        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        async Task<IReadOnlyList<BillingRunLineView>> Propose(string prefix, string? code, int month)
        {
            BillingRunSummary run = (await runs.ProposeAsync(new BillingRunProposal
            {
                CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, month, 28), Ward = "Ward 4", InvoicePrefix = prefix, UserId = 81, ServiceCode = code,
            })).Value;
            IReadOnlyList<BillingRunLineView> lines = (await runs.LinesAsync(companyId, run.RunNumber, exceptionsOnly: false)).Value;
            (await runs.CancelAsync(companyId, run.RunNumber)).IsSuccess.Should().BeTrue();
            return lines;
        }

        // The extra bins: 3 × 15 on the first stand, 2 × 20 on the second; the plain refuse run still bills only the built-in connection.
        (await Propose("MSB", "BINS", 4)).Select(l => (l.StandNumber, l.Charge)).Should().BeEquivalentTo([("MS-S1", 45m), ("MS-S2", 40m)]);
        (await Propose("MSR", null, 4)).Select(l => (l.StandNumber, l.Charge)).Should().Equal(("MS-S1", 20m));

        (await services.SetStatusAsync(companyId, "MS-S2", "BINS", ServiceLinkStatus.Suspended, new DateOnly(2026, 4, 30))).IsSuccess.Should().BeTrue();
        (await services.SetQuantityAsync(companyId, "MS-S1", "BINS", 1m)).IsSuccess.Should().BeTrue();
        (await Propose("MSB", "BINS", 5)).Select(l => (l.StandNumber, l.Charge)).Should().Equal(("MS-S1", 15m));
        (await services.ConnectionsAsync(companyId, "MS-S2")).Single(r => r.Service == "BINS").Status.Should().Be(ServiceLinkStatus.Suspended);
    }
}
