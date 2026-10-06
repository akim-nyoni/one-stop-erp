using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers customer contracts and call-offs: each call-off is a sales order at the contract price, drawing the contract down, refused
/// beyond what remains, for items not on the contract, or outside its dates.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CustomerContractTests
{
    private readonly AccountingFixture _fixture;

    public CustomerContractTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Contracts_are_drawn_down_by_call_offs()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer council = new(companyId, "CC-C", "Water Works", Scenario.Usd);
        Item pipe = new(companyId, "CC-PIPE", "Pipe 110mm") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
        Item valve = new(companyId, "CC-VALVE", "Gate valve") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
        db.AddRange(council, pipe, valve);
        await db.SaveChangesAsync();

        ICustomerContractService contracts = scope.ServiceProvider.GetRequiredService<ICustomerContractService>();
        (await contracts.CreateAsync(companyId, council.Id, "CC-2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [new ContractLineRequest(pipe.Id, 0m, 10m)]))
            .Error.Code.Should().Be("Sop.ContractInvalid");
        (await contracts.CreateAsync(companyId, council.Id, "cc-2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [new ContractLineRequest(pipe.Id, 1000m, 12.5m)])).IsSuccess.Should().BeTrue();
        (await contracts.CreateAsync(companyId, council.Id, "CC-2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [new ContractLineRequest(pipe.Id, 5m, 1m)]))
            .Error.Code.Should().Be("Sop.ContractDuplicate");

        SalesOrderResult first = (await contracts.CallOffAsync(companyId, "CC-2026", "CC-SO-1", new DateOnly(2026, 3, 1), [new CallOffLine(pipe.Id, 400m)])).Value;
        var order = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == first.SalesOrderId);
        order.CustomerId.Should().Be(council.Id);
        order.Lines.Should().ContainSingle(l => l.ItemId == pipe.Id && l.OrderedQuantity == 400m && l.UnitPrice == 12.5m);

        (await contracts.CallOffAsync(companyId, "CC-2026", "CC-SO-2", new DateOnly(2026, 3, 2), [new CallOffLine(pipe.Id, 700m)])).Error.Code.Should().Be("Sop.ContractExceeded");
        (await contracts.CallOffAsync(companyId, "CC-2026", "CC-SO-2", new DateOnly(2026, 3, 2), [new CallOffLine(valve.Id, 1m)])).Error.Code.Should().Be("Sop.NotOnContract");
        (await contracts.CallOffAsync(companyId, "CC-2026", "CC-SO-2", new DateOnly(2027, 1, 2), [new CallOffLine(pipe.Id, 1m)])).Error.Code.Should().Be("Sop.ContractNotActive");
        (await contracts.CallOffAsync(companyId, "CC-2026", "CC-SO-2", new DateOnly(2026, 4, 1), [new CallOffLine(pipe.Id, 600m)])).IsSuccess.Should().BeTrue();

        (await contracts.PositionAsync(companyId, "CC-2026")).Value.Should().Equal(new ContractPosition("CC-PIPE", 1000m, 1000m, 0m, 12.5m));
    }
}
