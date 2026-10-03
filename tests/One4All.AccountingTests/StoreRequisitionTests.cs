using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers store requisitions: the canteen asks for 10 cups at 2 each; the requester cannot approve, and nothing issues
/// before approval; 6 are issued (12 charged to the canteen's supplies account in its cost centre), over-issuing is
/// refused, and the last 4 complete it; reversing an issue credits the canteen, not cost of sales.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StoreRequisitionTests
{
    private readonly AccountingFixture _fixture;

    public StoreRequisitionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Requisitions_are_approved_then_issued_to_the_department()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item cup = new(companyId, "SR-CUP", "Cup") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "SRST", "Main store");
        db.AddRange(cup, store);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = cup.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 1), Quantity = 20m, UnitCost = 2m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IStoreRequisitionService requisitions = scope.ServiceProvider.GetRequiredService<IStoreRequisitionService>();
        (long id, string number) = (await requisitions.RaiseAsync(companyId, new StoreRequisitionRequest(new DateOnly(2026, 3, 5), store.Id, "6800", "CAN", 61,
            [new StoreRequisitionLineRequest(cup.Id, 10m)]))).Value;
        number.Should().StartWith("SR-");

        DateOnly date = new(2026, 3, 6);
        (await requisitions.IssueAsync(companyId, id, date, [new StoreRequisitionLineRequest(cup.Id, 6m)])).Error.Code.Should().Be("Store.NotApproved");
        (await requisitions.ApproveAsync(companyId, id, 61)).Error.Code.Should().Be("Store.CannotApprove");
        (await requisitions.ApproveAsync(companyId, id, 62)).IsSuccess.Should().BeTrue();

        StoreIssueResult first = (await requisitions.IssueAsync(companyId, id, date, [new StoreRequisitionLineRequest(cup.Id, 6m)])).Value;
        (first.Cost, first.FullyIssued).Should().Be((12m, false));
        first.IssueNoteNumber.Should().StartWith("SIN-");
        (await requisitions.IssueAsync(companyId, id, date, [new StoreRequisitionLineRequest(cup.Id, 5m)])).Error.Code.Should().Be("Store.OverIssue");
        (await requisitions.IssueAsync(companyId, id, date, [new StoreRequisitionLineRequest(cup.Id, 4m)])).Value.FullyIssued.Should().BeTrue();

        // 20 of supplies charged to the canteen cost centre; nothing to cost of sales.
        var charged = await db.Set<JournalLine>().Where(l => db.Journals.Any(j => j.Id == l.JournalEntryId && j.CompanyId == companyId) && l.AccountCode == "6800")
            .GroupBy(l => l.CostCentre).Select(g => new { CostCentre = g.Key, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToListAsync();
        charged.Should().ContainSingle().Which.Should().BeEquivalentTo(new { CostCentre = "CAN", Net = 20m });

        long issueMovement = await db.StockMovements.Where(m => m.ItemId == cup.Id && m.Type == StockMovementType.Issue && m.Reference == first.IssueNoteNumber).Select(m => m.Id).SingleAsync();
        (await inventory.ReverseIssueAsync(companyId, issueMovement, new DateOnly(2026, 3, 7))).IsSuccess.Should().BeTrue();
        decimal canteen = await db.Set<JournalLine>().Where(l => db.Journals.Any(j => j.Id == l.JournalEntryId && j.CompanyId == companyId) && l.AccountCode == "6800" && l.CostCentre == "CAN")
            .SumAsync(l => l.BaseDebit - l.BaseCredit);
        canteen.Should().Be(8m);
        (await db.Set<JournalLine>().AnyAsync(l => db.Journals.Any(j => j.Id == l.JournalEntryId && j.CompanyId == companyId) && l.AccountCode == "5000")).Should().BeFalse();
    }
}
