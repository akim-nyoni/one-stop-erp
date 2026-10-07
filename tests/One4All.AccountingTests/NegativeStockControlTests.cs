using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers negative stock by exception: under the company's control an issue taking stock below zero needs a reason and a supervisor with the
/// stock-issue authority; a till sale goes through but is recorded; when a receipt covers the shortfall its cost is corrected to the receipt
/// cost (cost of sales against stock) so what is left carries the receipt cost, and the events are marked corrected.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class NegativeStockControlTests
{
    private readonly AccountingFixture _fixture;

    public NegativeStockControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Negative_stock_needs_authority_and_is_corrected_on_receipt()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).NegativeStockControl = true;
        Role stores = new($"Stores lead {companyId}");
        stores.Grant("Inventory.Issue", OperationFlags.View | OperationFlags.Create | OperationFlags.AuthoriseLevel1);
        db.Roles.Add(stores);
        User clerk = new($"ns-clerk-{companyId}", "Clerk"), lead = new($"ns-lead-{companyId}", "Lead");
        db.Users.AddRange(clerk, lead);
        Item cement = new(companyId, "NS-CEM", "Cement") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "NS-WH", "Store");
        db.AddRange(cement, store);
        await db.SaveChangesAsync();
        lead.AssignRole(stores.Id);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        DateOnly day = new(2026, 4, 1);
        (await inventory.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = cement.Id, WarehouseId = store.Id, Date = day, Quantity = 2m, UnitCost = 10m, CreditAccountCode = "1100", Reference = "NS-R1" }))
            .IsSuccess.Should().BeTrue();
        IssueStockRequest Issue(string reference, decimal quantity, string? reason, long? by, bool till = false) => new()
        {
            CompanyId = companyId, ItemId = cement.Id, WarehouseId = store.Id, Date = day, Quantity = quantity, Reference = reference, AllowNegative = true, NegativeReason = reason,
            NegativeAuthorisedByUserId = by, FromTill = till,
        };
        (await inventory.IssueAsync(Issue("NS-I1", 5m, null, null))).Error.Code.Should().Be("Inv.NegativeNeedsAuthority");
        (await inventory.IssueAsync(Issue("NS-I1", 5m, "Urgent site order", clerk.Id))).Error.Code.Should().Be("Inv.NegativeNotAllowed");
        (await inventory.IssueAsync(Issue("NS-I1", 5m, "Urgent site order", lead.Id))).Value.Value.Should().Be(50m);
        (await inventory.IssueAsync(Issue("NS-POS", 1m, null, null, till: true))).IsSuccess.Should().BeTrue();
        (await db.Set<NegativeStockEvent>().AsNoTracking().Where(e => e.CompanyId == companyId).OrderBy(e => e.Id).Select(e => new { e.ShortQuantity, e.FromTill, e.AuthorisedByUserId }).ToListAsync())
            .Select(e => (e.ShortQuantity, e.FromTill, e.AuthorisedByUserId)).Should().Equal((3m, false, (long?)lead.Id), (1m, true, (long?)null));

        // 10 arrive at 12: the 4 sold short at 10 are corrected by 8, and the 6 left carry 12.
        StockMovementResult received = (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = cement.Id, WarehouseId = store.Id, Date = day.AddDays(1), Quantity = 10m, UnitCost = 12m, CreditAccountCode = "1100", Reference = "NS-R2",
        })).Value;
        received.AverageCostAfter.Should().Be(12m);
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentType == "NegativeStockCorrection").SelectMany(j => j.Lines).Where(l => l.AccountCode == "5000")
            .SumAsync(l => l.BaseDebit)).Should().Be(8m);
        (await db.Set<NegativeStockEvent>().AsNoTracking().Where(e => e.CompanyId == companyId).AllAsync(e => e.CorrectionJournalNumber != null)).Should().BeTrue();
    }
}
