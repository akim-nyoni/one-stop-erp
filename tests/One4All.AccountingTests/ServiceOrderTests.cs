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
/// Covers disconnection and reconnection: with a 200 threshold, a stand 300 in arrears is a candidate while one owing 100
/// is not; a disconnection order is raised (a second one is refused) and carried out, cutting the water; reconnection is
/// refused while the arrears stand; once paid, a reconnection order is raised and carried out, restoring the water and
/// invoicing a 50 fee.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ServiceOrderTests
{
    private readonly AccountingFixture _fixture;

    public ServiceOrderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Arrears_lead_to_disconnection_and_payment_to_reconnection()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff water = new(companyId, "WATER", "Water", ServiceType.Water, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 10m };
        db.Tariffs.Add(water);
        Customer late = new(companyId, "SO-LATE", "Late Payer", Scenario.Usd) { PaymentTermsDays = 0 };
        Customer good = new(companyId, "SO-GOOD", "Small Debt", Scenario.Usd) { PaymentTermsDays = 0 };
        db.Customers.AddRange(late, good);
        await db.SaveChangesAsync();
        Property lateStand = new(companyId, "201", "Ward 3", late.Id);
        lateStand.AddService(ServiceType.Water, water.Id, new DateOnly(2026, 1, 1));
        Property goodStand = new(companyId, "202", "Ward 3", good.Id);
        goodStand.AddService(ServiceType.Water, water.Id, new DateOnly(2026, 1, 1));
        db.Properties.AddRange(lateStand, goodStand);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach ((Customer customer, decimal amount) in new[] { (late, 300m), (good, 100m) })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = $"{customer.Code}-I", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        IServiceOrderService orders = scope.ServiceProvider.GetRequiredService<IServiceOrderService>();
        DateOnly march = new(2026, 3, 10);
        (await orders.CandidatesAsync(companyId, ServiceType.Water, march, 200m)).Should().Equal(new DisconnectionCandidate(lateStand.Id, "201", 300m, new DateOnly(2026, 2, 1)));
        (await orders.RaiseAsync(companyId, goodStand.Id, ServiceType.Water, ServiceOrderKind.Disconnect, march, 200m)).Error.Code.Should().Be("Mun.NotForDisconnection");

        string cut = (await orders.RaiseAsync(companyId, lateStand.Id, ServiceType.Water, ServiceOrderKind.Disconnect, march, 200m)).Value;
        (await orders.RaiseAsync(companyId, lateStand.Id, ServiceType.Water, ServiceOrderKind.Disconnect, march, 200m)).Error.Code.Should().Be("Mun.OrderOpen");
        (await orders.ExecuteAsync(companyId, cut, new DateOnly(2026, 3, 11), null)).IsSuccess.Should().BeTrue();
        (await Connected(db, lateStand.Id)).Should().BeFalse();

        (await orders.RaiseAsync(companyId, lateStand.Id, ServiceType.Water, ServiceOrderKind.Reconnect, new DateOnly(2026, 3, 12), 200m)).Error.Code.Should().Be("Mun.NotForReconnection");
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = late.Id, DocumentNumber = "SO-R1", ReceiptDate = new DateOnly(2026, 3, 13), Currency = Scenario.Usd, Amount = 300m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();

        string restore = (await orders.RaiseAsync(companyId, lateStand.Id, ServiceType.Water, ServiceOrderKind.Reconnect, new DateOnly(2026, 3, 14), 200m)).Value;
        (await orders.ExecuteAsync(companyId, restore, new DateOnly(2026, 3, 15), new ReconnectionFee(50m, "4900"))).IsSuccess.Should().BeTrue();
        (await Connected(db, lateStand.Id)).Should().BeTrue();
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == restore + "-FEE")).OriginalBase.Should().Be(50m);
        (await orders.ExecuteAsync(companyId, restore, new DateOnly(2026, 3, 15), null)).Error.Code.Should().Be("Mun.OrderNotOpen");
    }

    private static async Task<bool> Connected(One4AllDbContext db, long propertyId)
        => await db.Properties.AsNoTracking().Where(p => p.Id == propertyId).SelectMany(p => p.Services).Where(s => s.ServiceType == ServiceType.Water).Select(s => s.IsActive).SingleAsync();
}
