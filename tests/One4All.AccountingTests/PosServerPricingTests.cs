using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// The server holds a till sale to its items, not to what the till sends: the revenue account is the item's, the tax is worked out again from the
/// item's tax code, and a price below the list needs a supervisor other than the cashier holding Pos.PriceOverride; an offline replay is flagged, not refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosServerPricingTests
{
    private readonly AccountingFixture _fixture;

    public PosServerPricingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_sale_is_held_to_the_item_price_tax_and_revenue_account()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await scope.ServiceProvider.GetRequiredService<IMasterDataService>().CreateTaxCodeAsync(new NewTaxCode(companyId, "PSP15", "Standard 15%", TaxType.Standard, 15m,
            new DateOnly(2026, 1, 1), "2200", "2200"))).IsSuccess.Should().BeTrue();
        Branch branch = new(companyId, "PSP-BR", "Shop");
        db.Branches.Add(branch);
        Item kettle = new(companyId, "PSP-KETTLE", "Kettle") { RevenueAccountCode = "4400", CostOfSalesAccountCode = "5000", TaxCode = "PSP15" };
        Warehouse shop = new(companyId, "PSP-WH", "Shop");
        db.AddRange(kettle, shop);
        Role supervisors = new($"Price supervisors {companyId}");
        supervisors.Grant("Pos.PriceOverride", OperationFlags.OverridePrice);
        Role clerks = new($"Price clerks {companyId}");
        clerks.Grant("Pos.Sale", OperationFlags.Create);
        db.Roles.AddRange(supervisors, clerks);
        await db.SaveChangesAsync();
        User supervisor = new($"psp-super-{companyId}", "Price Supervisor");
        supervisor.AssignRole(supervisors.Id);
        User clerk = new($"psp-clerk-{companyId}", "Clerk");
        clerk.AssignRole(clerks.Id);
        db.Users.AddRange(supervisor, clerk);
        PosTerminal terminal = new(companyId, branch.Id, "PSP-TILL", "Till");
        db.Add(terminal);
        await db.SaveChangesAsync();

        IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
        long list = (await pricing.CreatePriceListAsync(companyId, "PSP-RET", "Retail", Scenario.Usd, isDefault: true)).Value;
        (await pricing.SetPriceAsync(companyId, list, kettle.Id, 1m, 20m)).IsSuccess.Should().BeTrue();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        const long cashier = 777;
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashier, openingFloat: 0m)).Value;
        CompleteSaleRequest Sale(string number, decimal unitPrice, decimal tax, long? authorisedBy = null, bool replay = false)
        {
            decimal net = 2m * unitPrice;
            return new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = cashier, SaleNumber = number, SaleDate = new DateOnly(2026, 6, 2), WarehouseId = shop.Id,
                TaxAccountCode = "2200", Lines = [new PosSaleLineRequest(kettle.Id, "Kettle", 2m, unitPrice, net, tax, "4000")],
                Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, net + tax, "1000", IsChange: false)], PriceOverrideAuthorisedByUserId = authorisedBy, OfflineReplay = replay,
            };
        }

        // At the list price the sale goes through, its revenue on the item's account whatever the till named.
        var listed = await pos.CompleteSaleAsync(Sale("PSP-1", 20m, 6m));
        listed.IsSuccess.Should().BeTrue(listed.IsFailure ? listed.Error.ToString() : null);
        (await db.Sales.AsNoTracking().Include(s => s.Lines).SingleAsync(s => s.CompanyId == companyId && s.SaleNumber == "PSP-1")).Lines.Single().RevenueAccountCode.Should().Be("4400");

        // The tax is the item's: 15% of 40 is 6, so a till claiming 0 is refused.
        (await pos.CompleteSaleAsync(Sale("PSP-2", 20m, 0m))).Error.Code.Should().Be("Pos.LineTax");

        // Below the list price: refused without a supervisor, with the cashier or with someone not allowed; through with a permitted supervisor.
        (await pos.CompleteSaleAsync(Sale("PSP-3", 15m, 4.5m))).Error.Code.Should().Be("Pos.PriceOverride");
        (await pos.CompleteSaleAsync(Sale("PSP-3", 15m, 4.5m, authorisedBy: cashier))).Error.Code.Should().Be("Pos.PriceOverride");
        (await pos.CompleteSaleAsync(Sale("PSP-3", 15m, 4.5m, authorisedBy: clerk.Id))).Error.Code.Should().Be("Pos.PriceOverride");
        var overridden = await pos.CompleteSaleAsync(Sale("PSP-3", 15m, 4.5m, authorisedBy: supervisor.Id));
        overridden.IsSuccess.Should().BeTrue(overridden.IsFailure ? overridden.Error.ToString() : null);
        overridden.Value.TotalGross.Should().Be(34.5m);

        // A sale replayed from an offline till already happened: a price that differs is flagged for review, not refused.
        var replayed = await pos.CompleteSaleAsync(Sale("PSP-4", 18m, 5.4m, replay: true));
        replayed.IsSuccess.Should().BeTrue(replayed.IsFailure ? replayed.Error.ToString() : null);
        (await db.PosEvents.AsNoTracking().Where(e => e.CompanyId == companyId && e.SaleNumber == "PSP-4").Select(e => e.Description).SingleAsync())
            .Should().Contain("18.00 where the price is 20.00");
    }
}
