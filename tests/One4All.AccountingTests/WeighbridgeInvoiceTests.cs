using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.JobCosting;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers billing weighbridge loads: a customer's completed outbound tickets are invoiced at a price per tonne plus their haulage, with tax on
/// top; each ticket is marked with the invoice, a load still on the bridge is left out, and nothing is billed twice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WeighbridgeInvoiceTests
{
    private readonly AccountingFixture _fixture;

    public WeighbridgeInvoiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Completed_loads_are_invoiced_once_with_haulage_and_tax()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer builder = new(companyId, "WI-B", "Invoice Builder", Scenario.Usd);
        Item stone = new(companyId, "WI-STONE", "Crushed stone") { CostOfSalesAccountCode = "5000" };
        Warehouse quarry = new(companyId, "WIQ", "Quarry stockpile");
        TaxCode vat = new(companyId, "WVAT", "VAT 15%", TaxType.Standard) { OutputAccountCode = "2200" };
        vat.AddRate(new DateOnly(2020, 1, 1), 15m);
        db.AddRange(builder, stone, quarry, vat, new WeighbridgeVehicle(companyId, "WI-1") { StoredTareKg = 10_000m },
            new HaulageZone(companyId, "WZ1", "Near", 0m, 10m, 2m, 30m), new HaulageZone(companyId, "WZ2", "Far", 10m, 50m, 4m, 50m));
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = stone.Id, WarehouseId = quarry.Id, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 4m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IWeighbridgeService bridge = scope.ServiceProvider.GetRequiredService<IWeighbridgeService>();
        async Task<long> Load(decimal distance, decimal? gross)
        {
            (long ticket, _) = (await bridge.FirstWeighAsync(new FirstWeighRequest
            {
                CompanyId = companyId, Direction = WeighDirection.Outbound, Vehicle = "WI-1", ItemId = stone.Id, CubicMetresPerTonne = 0.6m, CustomerId = builder.Id,
                WarehouseId = quarry.Id, DistanceKm = distance, Date = new DateOnly(2026, 3, 10), OperatorUserId = 1,
            })).Value;
            if (gross is decimal weight)
            {
                (await bridge.SecondWeighAsync(new SecondWeighRequest { CompanyId = companyId, TicketId = ticket, WeightKg = weight, OperatorUserId = 1 })).IsSuccess.Should().BeTrue();
            }

            return ticket;
        }

        await Load(5m, 22_000m);
        await Load(20m, 18_000m);
        long onBridge = await Load(5m, null);

        WeighbridgeInvoiceRequest Invoice(string number, string tax = "wvat") => new()
        {
            CompanyId = companyId, CustomerId = builder.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 31), PricePerTonne = 10m, SalesAccountCode = "4000",
            TaxCode = tax, EnteredByUserId = 1,
        };
        (await bridge.InvoiceTicketsAsync(Invoice("WI-INV-0", "NOPE"))).Error.Code.Should().Be("Wb.TaxCode");
        (await bridge.InvoiceTicketsAsync(Invoice("WI-INV-1"))).IsSuccess.Should().BeTrue();
        List<WeighbridgeTicket> tickets = await db.WeighbridgeTickets.AsNoTracking().Where(t => t.CompanyId == companyId).OrderBy(t => t.Id).ToListAsync();
        tickets.Where(t => t.Id != onBridge).Should().OnlyContain(t => t.InvoiceNumber == "WI-INV-1");
        tickets.Single(t => t.Id == onBridge).InvoiceNumber.Should().BeNull("a load still on the bridge is not billed");

        decimal haulage = tickets.Where(t => t.Id != onBridge).Sum(t => t.HaulageCharge);
        haulage.Should().BePositive();
        List<JournalLine> lines = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).ToListAsync();
        decimal tax = decimal.Round((200m + haulage) * 0.15m, 2, MidpointRounding.AwayFromZero);
        lines.Where(l => l.AccountCode == "2200").Sum(l => l.BaseCredit).Should().Be(tax);
        lines.Where(l => l.AccountCode == "4000").Sum(l => l.BaseCredit).Should().Be(200m + haulage, "20 t at 10 a tonne plus haulage, both to sales");

        (await bridge.InvoiceTicketsAsync(Invoice("WI-INV-2"))).Error.Code.Should().Be("Wb.NothingToInvoice");
    }
}
