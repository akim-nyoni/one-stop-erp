using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Workflow;
using One4All.Application.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the till's server search, favourites and category drill-down, the refund window, exchange-rate policy by document type or ledger, and the
/// machine and snapshot hash kept on each approval.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TillCatalogueAndPolicyTests
{
    private readonly AccountingFixture _fixture;

    public TillCatalogueAndPolicyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_till_finds_by_alias_shows_favourites_and_drills_into_categories()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "TC", "Till Branch");
        db.Branches.Add(branch);
        db.ItemCategories.AddRange(new ItemCategory(companyId, "DRINKS", "Drinks"), new ItemCategory(companyId, "SODA", "Soda") { ParentCode = "DRINKS" },
            new ItemCategory(companyId, "SNACKS", "Snacks"));
        Item cola = new(companyId, "TC-COLA", "Cola 330ml") { RevenueAccountCode = "4000", Category = "SODA" };
        Item water = new(companyId, "TC-WATER", "Still water") { RevenueAccountCode = "4000", Category = "Drinks" };
        Item crisps = new(companyId, "TC-CRISPS", "Crisps") { RevenueAccountCode = "4000", Category = "SNACKS" };
        db.Items.AddRange(cola, water, crisps);
        await db.SaveChangesAsync();
        PosTerminal till = new(companyId, branch.Id, "TCT1", "Front till");
        db.PosTerminals.Add(till);
        db.ItemAliases.Add(new ItemAlias(companyId, cola.Id, ItemAliasKind.Synonym, "fizzy pop"));
        PriceList list = new(companyId, "TC-LIST", "Retail", Scenario.Usd) { IsDefault = true };
        list.SetPrice(cola.Id, 1m, 1.5m);
        list.SetPrice(water.Id, 1m, 1m);
        list.SetPrice(crisps.Id, 1m, 2m);
        db.PriceLists.Add(list);
        await db.SaveChangesAsync();

        IPosCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<IPosCatalogueService>();
        (await catalogue.SearchAsync(companyId, "fizzy")).Select(t => t.Code).Should().Equal("TC-COLA");

        (await catalogue.SetFavouriteAsync(companyId, null, "TC-CRISPS", 1, false)).IsSuccess.Should().BeTrue();
        (await catalogue.SetFavouriteAsync(companyId, till.Id, "TC-WATER", 5, false)).IsSuccess.Should().BeTrue();
        (await catalogue.SetFavouriteAsync(companyId, till.Id, "TC-NONE", 5, false)).Error.Code.Should().Be("Pos.ItemNotFound");
        (await catalogue.FavouritesAsync(companyId, till.Id)).Select(t => t.Code).Should().Equal("TC-WATER", "TC-CRISPS");
        (await catalogue.FavouritesAsync(companyId, null)).Select(t => t.Code).Should().Equal("TC-CRISPS");
        (await catalogue.SetFavouriteAsync(companyId, null, "TC-CRISPS", 0, true)).IsSuccess.Should().BeTrue();
        (await catalogue.FavouritesAsync(companyId, till.Id)).Select(t => t.Code).Should().Equal("TC-WATER");

        (await catalogue.CategoriesAsync(companyId, null)).Select(c => (c.Code, c.HasChildren)).Should().Equal(("DRINKS", true), ("SNACKS", false));
        (await catalogue.CategoriesAsync(companyId, "drinks")).Select(c => c.Code).Should().Equal("SODA");
        (await catalogue.CategoryTilesAsync(companyId, "DRINKS")).Select(t => t.Code).Should().BeEquivalentTo(["TC-COLA", "TC-WATER"], "a category shows what sits under it");
        (await catalogue.CategoryTilesAsync(companyId, "SODA")).Select(t => t.Code).Should().Equal("TC-COLA");
    }

    [SkippableFact]
    public async Task Old_sales_need_a_supervisor_to_refund()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "RW", "Refund Branch");
        db.Branches.Add(branch);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.RefundWindowDays = 30;
        await db.SaveChangesAsync();
        PosTerminal till = new(companyId, branch.Id, "RWT1", "Refund Till");
        Item soda = new(companyId, "RW-SODA", "Soda") { RevenueAccountCode = "4000" };
        db.PosTerminals.Add(till);
        db.Items.Add(soda);
        Warehouse wh = new(companyId, "RWW", "Shop");
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, till.Id, cashierUserId: 5, openingFloat: 100m)).Value;
        DateOnly sold = new(2026, 3, 2);
        long sale = (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = till.Id, TillSessionId = session, CashierUserId = 5, SaleNumber = "RW-1", SaleDate = sold, WarehouseId = wh.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(soda.Id, "Soda", 3m, 10m, 30m, 0m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 30m, "1000", IsChange: false)],
        })).Value.SaleId;
        long line = await db.Set<SaleLine>().Where(l => l.SaleId == sale).Select(l => l.Id).SingleAsync();
        IPosEventService events = scope.ServiceProvider.GetRequiredService<IPosEventService>();
        PosRefundRequest Refund(DateOnly date, long? supervisor) => new()
        {
            CompanyId = companyId, SaleId = sale, TerminalId = till.Id, CashierUserId = 5, AuthorisedByUserId = supervisor, Date = date, RefundAccountCode = "1000", Reason = "Flat",
            Lines = [new PosRefundLine(line, 1m)], TillSessionId = session,
        };

        Core.Security.User Supervisor(string name, Core.Security.OperationFlags flags)
        {
            Core.Security.Role role = new($"Refunds {name} {companyId}");
            role.Grant("Pos.Refund", flags);
            db.Roles.Add(role);
            Core.Security.User user = new($"rw-{name}-{companyId}", name);
            db.Users.Add(user);
            db.SaveChanges();
            user.AssignRole(role.Id);
            db.SaveChanges();
            return user;
        }

        Core.Security.User shiftLead = Supervisor("lead", Core.Security.OperationFlags.AuthoriseLevel1);
        Core.Security.User manager = Supervisor("manager", Core.Security.OperationFlags.AuthoriseLevel1 | Core.Security.OperationFlags.AuthoriseLevel2);
        (await events.RefundAsync(Refund(sold.AddDays(30), shiftLead.Id))).IsSuccess.Should().BeTrue("on the last day of the window");
        (await events.RefundAsync(Refund(sold.AddDays(31), shiftLead.Id))).Error.Code.Should().Be("Pos.RefundWindow", "past it, a first-level supervisor is not enough");
        (await events.RefundAsync(Refund(sold.AddDays(31), manager.Id))).IsSuccess.Should().BeTrue();
    }

    [SkippableFact]
    public async Task A_rate_policy_sets_the_rate_type_and_approvals_keep_machine_and_hash()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Scenario.Eur && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Fixed))
        {
            db.ExchangeRates.Add(new ExchangeRate(Scenario.Eur, Scenario.Usd, ExchangeRateType.Fixed, new DateOnly(2026, 2, 1), 1.25m));
        }

        Supplier supplier = new(companyId, "RP-SUP", "Euro Supplier", Scenario.Eur);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        async Task<decimal> Invoice(string number) => (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Eur, NetAmount = 100m, ExpenseAccountCode = "5000",
        })).Value.BaseAmount;

        (await Invoice("RP-1")).Should().Be(110m, "no policy: the daily rate");
        db.ExchangeRatePolicies.Add(new ExchangeRatePolicy(companyId, "AP", ExchangeRateType.Fixed));
        await db.SaveChangesAsync();
        (await Invoice("RP-2")).Should().Be(125m, "the payables ledger uses the fixed rate");
        db.ExchangeRatePolicies.Add(new ExchangeRatePolicy(companyId, "PurchaseInvoice", ExchangeRateType.Daily));
        await db.SaveChangesAsync();
        (await Invoice("RP-3")).Should().Be(110m, "a document type's policy comes before its ledger's");

        // An approval records where it was given and a hash of what was approved.
        WorkflowDefinition definition = new(companyId, "RpDocument", "One step");
        definition.AddStage(1, "Buyer").AddEligibleUser(77);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<One4All.Infrastructure.Security.CurrentUserContext>().SetClientAddress("10.1.2.3");
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        long instance = (await workflow.StartAsync(companyId, "RpDocument", "RP-DOC-1", 50m, 3)).Value.InstanceId;
        (await workflow.ActAsync(instance, 77, WorkflowActionType.Approve, "fine")).IsSuccess.Should().BeTrue();
        WorkflowApproval decision = (await db.WorkflowInstances.AsNoTracking().Include(i => i.Approvals).SingleAsync(i => i.Id == instance)).Approvals.Single();
        decision.Machine.Should().Be("10.1.2.3");
        decision.SnapshotHash.Should().Be(WorkflowActionToken.Hash(decision.Snapshot!));
    }
}
