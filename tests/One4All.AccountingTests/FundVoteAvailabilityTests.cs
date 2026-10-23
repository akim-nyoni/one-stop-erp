using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Budgeting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers vote control on every journal under fund control: petty cash and manual journals are refused beyond what is still available on the vote
/// (appropriation less actuals and open commitments), an authorised override lets one through and is recorded, and an order's invoice - committed
/// and checked at the order - is not counted against the vote a second time while its commitment is used up.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FundVoteAvailabilityTests
{
    private readonly AccountingFixture _fixture;

    public FundVoteAvailabilityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Spending_beyond_the_available_vote_is_refused_unless_overridden()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).FundControlEnabled = true;
        (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "6300")).BudgetEnabled = true;
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgets = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budget = (await budgets.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6300", 3, 100m)])).Value;
        (await budgets.ActivateAsync(companyId, budget)).IsSuccess.Should().BeTrue();

        // Petty cash: 80 of the 100 vote fits, 30 more does not, the last 20 does.
        IPettyCashService petty = scope.ServiceProvider.GetRequiredService<IPettyCashService>();
        (await petty.ReimburseToFloatAsync(companyId, new DateOnly(2026, 3, 2), "1010", "1100", 500m, "FVA-INIT")).IsSuccess.Should().BeTrue("the float is not a vote");
        (await petty.DisburseAsync(companyId, 80m, new DateOnly(2026, 3, 5), "6300", "1010", "FVA-PC1")).IsSuccess.Should().BeTrue();
        (await petty.DisburseAsync(companyId, 30m, new DateOnly(2026, 3, 6), "6300", "1010", "FVA-PC2")).Error.Code.Should().Be("Posting.VoteExceeded");
        (await petty.DisburseAsync(companyId, 20m, new DateOnly(2026, 3, 6), "6300", "1010", "FVA-PC3")).IsSuccess.Should().BeTrue();

        // A manual journal on the spent vote is refused; with a reason and a supervisor other than the preparer it posts, and both are recorded.
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest Journal(string document, string? reason = null, long? authoriser = null) => new()
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 9), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = document,
            Narrative = "Refreshments", BudgetOverrideReason = reason, BudgetOverrideAuthorisedByUserId = authoriser,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6300", Side = JournalSide.Debit, TransactionAmount = 50m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 50m },
            ],
        };
        (await engine.PostAsync(Journal("FVA-J1"))).Error.Code.Should().Be("Posting.VoteExceeded");
        (await engine.PostAsync(Journal("FVA-J2", "Board meeting", authoriser: 9))).IsSuccess.Should().BeTrue();
        List<BudgetControlEvent> events = await db.Set<BudgetControlEvent>().AsNoTracking().Where(e => e.CompanyId == companyId).OrderBy(e => e.Id).ToListAsync();
        events.Select(e => (e.Outcome, e.Document)).Should().Equal((BudgetControlOutcome.Blocked, "PettyCash FVA-PC2"), (BudgetControlOutcome.Blocked, "Journal FVA-J1"),
            (BudgetControlOutcome.Overridden, "Journal FVA-J2"));

        // A credit back to the vote frees it again.
        (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = "FVA-J3",
            Lines =
            [
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 70m },
                new JournalLineRequest { AccountCode = "6300", Side = JournalSide.Credit, TransactionAmount = 70m },
            ],
        })).IsSuccess.Should().BeTrue();
        (await petty.DisburseAsync(companyId, 20m, new DateOnly(2026, 3, 11), "6300", "1010", "FVA-PC4")).IsSuccess.Should().BeTrue();
    }

    [SkippableFact]
    public async Task An_ordered_invoice_is_not_counted_against_the_vote_twice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier supplier = new(companyId, "FVA-SUP", "Cleaning Co", Scenario.Usd);
        Item soap = new(companyId, "FVA-SOAP", "Soap") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "FVA-WH", "Store");
        db.AddRange(supplier, soap, store);
        Core.System.Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.FundControlEnabled = true;
        company.InvoicePriceTolerancePercent = 50m;
        (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "6100")).BudgetEnabled = true;
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgets = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budget = (await budgets.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6100", 4, 1_000m)])).Value;
        (await budgets.ActivateAsync(companyId, budget)).IsSuccess.Should().BeTrue();

        // The order commits the whole vote; its invoice comes in 40 dearer, charged to the vote as a price variance. The gate checked that 40 at
        // the invoice and the order's commitment is used up by it, so the posting is not refused for the commitment still showing open.
        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        long po = (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = "FVA-PO1", OrderDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(soap.Id, "Soap", 100m, 10m, "6100")],
        })).Value.PurchaseOrderId;
        long line = db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po).Select(l => l.Id).Single();
        (await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest { CompanyId = companyId, PurchaseOrderId = po, WarehouseId = store.Id, GrnNumber = "FVA-GRN1", Date = new DateOnly(2026, 4, 2),
            Lines = [new ReceiveLine(line, 100m)] })).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Inventory.IInventoryService>().IssueAsync(new Application.Abstractions.Inventory.IssueStockRequest
        {
            CompanyId = companyId, ItemId = soap.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 4, 2), Quantity = 100m, ChargeAccountCode = "5000", Reference = "FVA-ISS1",
        })).IsSuccess.Should().BeTrue("issued before the invoice, so its price difference is charged to the vote, not to stock");
        var matched = await procurement.MatchInvoiceAsync(new MatchInvoiceRequest { CompanyId = companyId, PurchaseOrderId = po, InvoiceNumber = "FVA-INV1", Date = new DateOnly(2026, 4, 3),
            TolerancePercent = 50m, PriceVarianceAccountCode = "6100", Lines = [new InvoiceMatchLine(line, 100m, 10.40m)] });
        matched.IsSuccess.Should().BeTrue(matched.IsFailure ? matched.Error.ToString() : null);
        (await db.Set<BudgetCommitment>().AsNoTracking().Where(c => c.CompanyId == companyId && c.Reference == "FVA-PO1" && c.Status == CommitmentStatus.Open).SumAsync(c => c.Amount))
            .Should().Be(0m);

        // What is left of the vote (1,000 less the 40 spent) is still there for other spending, and no more.
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest Spend(string document, decimal amount) => new()
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 4, 5), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = document,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        };
        (await engine.PostAsync(Spend("FVA-J1", 961m))).Error.Code.Should().Be("Posting.VoteExceeded");
        (await engine.PostAsync(Spend("FVA-J2", 960m))).IsSuccess.Should().BeTrue();
    }

    [SkippableFact]
    public async Task A_till_sale_is_not_held_back_by_the_cost_of_sales_vote()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).FundControlEnabled = true;
        (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "5000")).BudgetEnabled = true;
        Core.System.Branch branch = new(companyId, "FVA-BR", "Shop");
        Item soda = new(companyId, "FVA-SODA", "Soda") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
        Warehouse shop = new(companyId, "FVA-SHOP", "Shop");
        db.AddRange(branch, soda, shop);
        await db.SaveChangesAsync();
        Core.Pos.PosTerminal terminal = new(companyId, branch.Id, "FVA-TILL", "Till");
        db.Add(terminal);
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgets = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budget = (await budgets.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("5000", 3, 1m)])).Value;
        (await budgets.ActivateAsync(companyId, budget)).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Inventory.IInventoryService>().ReceiveAsync(new Application.Abstractions.Inventory.ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = soda.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 5m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        // The vote on cost of sales is all but exhausted (1 left); a sale costing 10 still completes - its cost is not discretionary spending.
        Application.Abstractions.Pos.IPosService pos = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Pos.IPosService>();
        const long cashier = 900_801;
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashier, openingFloat: 0m)).Value;
        var sold = await pos.CompleteSaleAsync(new Application.Abstractions.Pos.CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = cashier, SaleNumber = "FVA-S1", SaleDate = new DateOnly(2026, 3, 5), WarehouseId = shop.Id,
            TaxAccountCode = "2200", Lines = [new Application.Abstractions.Pos.PosSaleLineRequest(soda.Id, "Soda", 2m, 8m, 16m, 0m, "4000")],
            Tenders = [new Application.Abstractions.Pos.PosTenderRequest(Core.Pos.TenderType.Cash, Scenario.Usd, 16m, "1000", IsChange: false)],
        });
        sold.IsSuccess.Should().BeTrue(sold.IsFailure ? sold.Error.ToString() : null);
        (await db.Set<StockMovement>().AsNoTracking().Where(m => m.CompanyId == companyId && m.ItemId == soda.Id && m.Reference == "FVA-S1").SumAsync(m => m.Value)).Should().Be(-10m);
        (await db.Set<BudgetControlEvent>().AsNoTracking().AnyAsync(e => e.CompanyId == companyId)).Should().BeFalse("a sale's cost is neither blocked nor overridden");

        // A manual journal to the same account is still held to the vote.
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 6), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = "FVA-J5",
            Lines =
            [
                new JournalLineRequest { AccountCode = "5000", Side = JournalSide.Debit, TransactionAmount = 10m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 10m },
            ],
        })).Error.Code.Should().Be("Posting.VoteExceeded");
    }
}
