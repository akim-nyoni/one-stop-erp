using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>A till sale credited to a sales rep is on the rep's commission statement; a voided one is not.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PosCommissionTests
{
    private readonly AccountingFixture _fixture;

    public PosCommissionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Till_sales_earn_the_reps_commission()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "PCB", "Shop");
        db.Branches.Add(branch);
        SalesRep agent = new(companyId, "PC-AG", "Agent", CommissionBasis.Invoiced, 5m);
        db.Add(agent);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "PCT1", "Till");
        Item item = new(companyId, "PC-ITEM", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "PC-WH", "Shop floor");
        db.AddRange(terminal, item, shop);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        DateOnly day = new(2026, 3, 2);
        CompleteSaleRequest Sale(string number, decimal amount, long? rep) => new()
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(item.Id, "Widget", 1m, amount, amount, 0m, "4000")], Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, amount, "1000", false)],
            SalesRepId = rep,
        };
        (await pos.CompleteSaleAsync(Sale("PC-0", 10m, 999_999))).Error.Code.Should().Be("Pos.SalesRep");
        (await pos.CompleteSaleAsync(Sale("PC-1", 200m, agent.Id))).IsSuccess.Should().BeTrue();
        long voided = (await pos.CompleteSaleAsync(Sale("PC-2", 50m, agent.Id))).Value.SaleId;
        (await pos.VoidSaleAsync(companyId, voided, day, "Rang up wrong")).IsSuccess.Should().BeTrue();

        CommissionStatement statement = (await scope.ServiceProvider.GetRequiredService<ICommissionService>().StatementAsync(companyId, agent.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).Value;
        statement.Lines.Should().ContainSingle(l => l.CustomerCode == "TILL").Which.Should().Match<CommissionLine>(l => l.Base == 200m && l.Commission == 10m);
    }
}
