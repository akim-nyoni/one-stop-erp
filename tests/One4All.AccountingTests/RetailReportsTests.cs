using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the POS-numbered retail reports (brief §14 F): one cash sale with change and a short cash-up; the daily summary,
/// cash-up variance, product mix and tender analysis read it back; every POS report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RetailReportsTests
{
    private readonly AccountingFixture _fixture;

    public RetailReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Retail_reports_reflect_sales_and_cash_ups()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Branch branch = new(companyId, "RHO", "Head Office");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "RTILL", "Front Till");
        Item soda = new(companyId, "RSODA", "Soda 500ml") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse floor = new(companyId, "RWH", "Shop Floor");
        db.AddRange(terminal, soda, floor);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 100m)).Value;
        (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = "RS-1", SaleDate = new DateOnly(2026, 3, 2), WarehouseId = floor.Id,
            TaxAccountCode = "2200", Lines = [new PosSaleLineRequest(soda.Id, "Soda 500ml", 10m, 2.00m, 20m, 3m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 25m, "1000", IsChange: false), new PosTenderRequest(TenderType.Cash, Scenario.Usd, 2m, "1000", IsChange: true)],
        })).IsSuccess.Should().BeTrue();
        (await pos.CashUpAndCloseAsync(new CashUpRequest
        {
            TillSessionId = session, CountedCash = 120m, Date = new DateOnly(2026, 3, 2), CashAccountCode = "1000", OverShortAccountCode = "7500",
        })).Value.Variance.Should().Be(-3m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("POS103")).Should().Contain("2026-03-02,RTILL,").And.Contain(",1,0,20.00,3.00,Cash 23.00,23");
        (await Render("POS105")).Should().Contain(",RTILL,").And.Contain("100.00,25.00,2.00,123.00,120.00,-3");
        (await Render("POS113")).Should().Contain("1,RSODA Soda 500ml,10,100.0%,100.0%,20");
        (await Render("POS117")).Should().Contain("Cash,USD,1,23.00,100.0%,23");

        // Vouchers (thirteenth audit #13): the gift-card liability aged from issue, and allowance vouchers outstanding per holder.
        (await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Vouchers.IVoucherService>().IssueGiftCardAsync(new One4All.Application.Abstractions.Vouchers.IssueGiftCardRequest
        {
            CompanyId = companyId, CardNumber = $"GC-RR-{companyId}", Currency = Scenario.Usd, Value = 80m, IssueDate = new DateOnly(2026, 3, 2), CashAccountCode = "1100", LiabilityAccountCode = "2300",
        })).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Vouchers.IAllowanceVoucherService>().IssueAsync(companyId,
            new One4All.Application.Abstractions.Vouchers.AllowanceIssueRequest("RR", 1, 3, One4All.Core.Vouchers.AllowanceKind.Fuel, 40m, "Driver R. Dube", new DateOnly(2026, 3, 2), new DateOnly(2026, 12, 31)))).IsSuccess.Should().BeTrue();
        (await Render("POS122")).Should().Contain($"GC-RR-{companyId},USD,2026-03-02,").And.Contain(",80.00,80");
        (await Render("POS123")).Should().Contain("Driver R. Dube,Fuel,3,0,").And.Contain(",120");

        foreach (string code in RetailReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
