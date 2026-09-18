using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Budgeting;
using One4All.Core.FixedAssets;
using One4All.Core.Inventory;
using One4All.Core.Municipal;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class VerticalReportRenderTests
{
    private readonly AccountingFixture _fixture;

    public VerticalReportRenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Vertical_reports_render_live_figures()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long fiscalYearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            fiscalYearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

            // Inventory: receive 100 @ 2.00 (value 200).
            Item item = new(companyId, "WIDGET", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "WH1", "Main");
            db.Warehouses.Add(wh);

            // Municipal: a property with a ratepayer.
            Customer ratepayer = new(companyId, "RATE1", "Stand Owner", Scenario.Usd);
            db.Customers.Add(ratepayer);

            // Payroll: an employee and a payslip.
            Employee emp = new(companyId, "E1", "Ann Employee", 2000m, new DateOnly(2025, 1, 1));
            db.Employees.Add(emp);
            await db.SaveChangesAsync();

            Property property = new(companyId, "STAND-X", "Ward 1", ratepayer.Id) { Valuation = 100000m };
            db.Properties.Add(property);

            Payslip slip = new(companyId, emp.Id, "2026-03", new DateOnly(2026, 3, 25));
            slip.AddLine("BASIC", "Basic salary", PayElementType.Earning, 2000m, true);
            slip.AddLine("PAYE", "PAYE", PayElementType.Deduction, 200m, false);
            db.Payslips.Add(slip);
            await db.SaveChangesAsync();

            long itemId = item.Id;
            long warehouseId = wh.Id;
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 2.00m, CreditAccountCode = "1100" });

            // Fixed asset.
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            await fa.RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = "VH-9", Description = "Truck", AssetClass = "Vehicles", Cost = 24000m, ResidualValue = 0m,
                AcquisitionDate = new DateOnly(2026, 1, 1), Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 48,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "FA-9",
            });

            // Budget: 1000 budget on 6100, 500 actual.
            IPostingEngine posting = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 1, 15), TransactionCurrency = Scenario.Usd,
                Lines = [new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 500m }, new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 500m }],
            });
            IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
            long budgetId = (await budgeting.CreateBudgetAsync(companyId, fiscalYearId, "2026", BudgetType.OriginalApproved, "ADMIN", [new NewBudgetLine("6100", 1, 1000m)])).Value;
            await budgeting.ActivateAsync(companyId, budgetId);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();

            (await Html(runner, companyId, "INV-VAL", new())).Should().Contain("WIDGET").And.Contain("200.00");
            (await Html(runner, companyId, "FA-REG", new())).Should().Contain("VH-9").And.Contain("24,000.00");
            (await Html(runner, companyId, "PAY-SUM", new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) })).Should().Contain("Ann Employee").And.Contain("1,800.00");
            (await Html(runner, companyId, "MUN-PROP", new())).Should().Contain("STAND-X").And.Contain("100,000.00");
            (await Html(runner, companyId, "BUD-BVA", new() { ["FiscalYearId"] = fiscalYearId, ["Department"] = "ADMIN", ["Period"] = 1 })).Should().Contain("6100").And.Contain("1,000.00");
        }
    }

    private static async Task<string> Html(IReportRunner runner, long companyId, string code, Dictionary<string, object?> parameters)
    {
        var result = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? $"{code}: {result.Error}" : null);
        return Encoding.UTF8.GetString(result.Value.Content);
    }
}
