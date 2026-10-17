using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers job cost from purchasing and overheads: a job line on a purchase order needs the job's WIP account and shows as committed; goods
/// received for it go straight to the job (out of stock to WIP) and onto its cost; an invoice priced above the order charges the
/// difference to the job (its WIP account, not price variance); a direct supplier invoice charged to the job is captured; labour carries
/// the overhead rate; the position shows actual, overheads and what is still on order.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class JobCostFromPurchasingTests
{
    private readonly AccountingFixture _fixture;

    public JobCostFromPurchasingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Purchases_for_a_job_and_overheads_reach_the_job_cost()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer client = new(companyId, "JP-CLI", "Client", Scenario.Usd);
        Supplier supplier = new(companyId, "JP-SUP", "Builders Merchant", Scenario.Usd);
        Item cement = new(companyId, "JP-CEM", "Cement") { CostOfSalesAccountCode = "5000" };
        Item soap = new(companyId, "JP-SOAP", "Soap") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "JP-WH", "Store");
        db.AddRange(client, supplier, cement, soap, store);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).InvoicePriceTolerancePercent = 10m;
        await db.SaveChangesAsync();
        IJobCostingService jobs = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
        long contract = (await jobs.CreateContractAsync(companyId, "JP-1", "Clinic", client.Id, 10_000m, 8_000m, 5m, "4300")).Value;
        (await jobs.SetOverheadRateAsync(companyId, JobCostSource.Overhead, 10m, "Overheads")).Error.Code.Should().Be("Job.OverheadRate");
        (await jobs.SetOverheadRateAsync(companyId, JobCostSource.Labour, 35m, "Site overheads")).IsSuccess.Should().BeTrue();

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        CreatePurchaseOrderRequest Order(string number, string? jobAccount) => new()
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(cement.Id, "Cement", 10m, 20m, jobAccount, contract, "Materials"), new PurchaseOrderLineRequest(soap.Id, "Soap", 5m, 10m, "6100")],
        };
        (await procurement.CreatePurchaseOrderAsync(Order("JP-PO0", null))).Error.Code.Should().Be("Pop.JobLineAccount");
        long po = (await procurement.CreatePurchaseOrderAsync(Order("JP-PO1", "5000"))).Value.PurchaseOrderId;
        (await jobs.GetProfitabilityAsync(companyId, contract)).Value.CommittedCost.Should().Be(200m);

        List<PurchaseOrderLine> lines = await db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.PurchaseOrderId == po).OrderBy(l => l.Id).ToListAsync();
        (await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = po, WarehouseId = store.Id, GrnNumber = "JP-GRN1", Date = new DateOnly(2026, 4, 2),
            Lines = [new ReceiveLine(lines[0].Id, 6m), new ReceiveLine(lines[1].Id, 5m)],
        })).IsSuccess.Should().BeTrue();
        (await db.StockBalances.AsNoTracking().Where(b => b.ItemId == cement.Id).SumAsync(b => b.QuantityOnHand)).Should().Be(0m, "delivered to site, not kept in stock");
        (await db.StockBalances.AsNoTracking().Where(b => b.ItemId == soap.Id).SumAsync(b => b.QuantityOnHand)).Should().Be(5m);

        // Invoiced at 21 against 20: the 6 difference goes to the job, so no price variance account is needed.
        MatchInvoiceResult matched = (await procurement.MatchInvoiceAsync(new MatchInvoiceRequest
        {
            CompanyId = companyId, PurchaseOrderId = po, InvoiceNumber = "JP-INV1", Date = new DateOnly(2026, 4, 3), TolerancePercent = 10m,
            Lines = [new InvoiceMatchLine(lines[0].Id, 6m, 21m), new InvoiceMatchLine(lines[1].Id, 5m, 10m)],
        })).Value;
        matched.InvoiceValue.Should().Be(176m);
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "JP-INV1").SelectMany(j => j.Lines).Where(l => l.AccountCode == "5000").SumAsync(l => l.BaseDebit))
            .Should().Be(6m);

        // A direct supplier invoice charged to the job, and labour with its 35% overhead.
        (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "JP-HIRE1", DocumentDate = new DateOnly(2026, 4, 4), Currency = Scenario.Usd, NetAmount = 50m,
            ExpenseAccountCode = "5000", JobContractId = contract, JobCostHead = "Hire",
        })).IsSuccess.Should().BeTrue();
        (await jobs.CaptureCostAsync(companyId, contract, "Labour", JobCostSource.Labour, 100m, new DateOnly(2026, 4, 5), "Week 14")).IsSuccess.Should().BeTrue();

        JobProfitability position = (await jobs.GetProfitabilityAsync(companyId, contract)).Value;
        (position.ActualCost, position.Overheads, position.CommittedCost, position.CostToDateAndCommitted).Should().Be((311m, 35m, 80m, 391m));
        List<(JobCostSource, string, decimal)> costs = [.. (await db.JobContracts.AsNoTracking().Where(j => j.Id == contract).SelectMany(j => j.Costs).OrderBy(e => e.Id).ToListAsync())
            .Select(e => (e.Source, e.CostHead, e.Amount))];
        costs.Should().Equal((JobCostSource.GoodsReceived, "Materials", 120m), (JobCostSource.PurchaseInvoice, "Materials", 6m), (JobCostSource.PurchaseInvoice, "Hire", 50m),
            (JobCostSource.Labour, "Labour", 100m), (JobCostSource.Overhead, "Site overheads", 35m));
    }
}
