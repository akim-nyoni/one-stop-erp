using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Inventory;
using One4All.Core.Payroll;
using One4All.Core.Pos;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers recoveries through payroll: staff purchases charged to an employee's staff account at the till and rent on the employee's
/// council tenancy are deducted from pay (formula variables STAFFPURCHASES and HOUSINGRENT), the rent is receipted to the tenancy
/// account, a reversal puts both balances back, and PR23 / PR24 reconcile them.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollRecoveryTests
{
    private readonly AccountingFixture _fixture;

    public PayrollRecoveryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Staff_purchases_and_housing_rent_are_recovered_through_payroll()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(
            new LedgerAccount(companyId, "1491", "Staff Purchase Accounts", AccountType.Asset),
            new LedgerAccount(companyId, "2491", "Rent Deductions Clearing", AccountType.Liability),
            new LedgerAccount(companyId, "4975", "Rental Income", AccountType.Income),
            new LedgerAccount(companyId, "2260", "Rental Deposits Held", AccountType.Liability));
        Branch branch = new(companyId, "HO", "Head Office");
        db.Branches.Add(branch);
        Customer tenantAccount = new(companyId, "RC-TEN", "Rae Tenant (rent)", Scenario.Usd) { PaymentTermsDays = 7 };
        db.Customers.Add(tenantAccount);
        User approver = new($"rc-approver-{companyId}", "Approver");
        db.Users.Add(approver);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "TILL1", "Staff Shop");
        Item item = new(companyId, "SODA", "Soda 500ml") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "WH1", "Shop Floor");
        db.AddRange(terminal, item, shop);

        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement staff = new(companyId, "STAFF", "Staff purchases", PayElementType.Deduction, CalculationBasis.Formula, 2, "1491") { Formula = "STAFFPURCHASES", Recovers = PayRecovery.StaffPurchases };
        PayElement rent = new(companyId, "RENT", "Housing rent", PayElementType.Deduction, CalculationBasis.Formula, 3, "2491") { Formula = "HOUSINGRENT", Recovers = PayRecovery.HousingRent };
        db.PayElements.AddRange(basic, staff, rent);
        await db.SaveChangesAsync();
        Employee rae = new(companyId, "RC-1", "Rae Tenant", 1000m, new DateOnly(2025, 1, 1));
        Employee sam = new(companyId, "RC-2", "Sam Other", 800m, new DateOnly(2025, 1, 1));
        foreach (Employee e in new[] { rae, sam })
        {
            e.AssignPayElement(basic.Id);
            e.AssignPayElement(staff.Id);
            e.AssignPayElement(rent.Id);
        }

        db.Employees.AddRange(rae, sam);
        await db.SaveChangesAsync();

        // Council housing: Rae rents RR-1 at 300 a month.
        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "RR-1", "Staff cottage", RentalUnitType.House, 3, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        string tenancyNumber = (await rentals.LetAsync(new LetUnitRequest
        {
            CompanyId = companyId, UnitCode = "RR-1", TenantCustomerId = tenantAccount.Id, StartDate = new DateOnly(2026, 3, 1), Deposit = 300m, BankAccountCode = "1100", DepositAccountCode = "2260",
        })).Value;
        rae.HousingTenancyId = (await db.Set<Tenancy>().SingleAsync(t => t.CompanyId == companyId && t.TenancyNumber == tenancyNumber)).Id;
        await db.SaveChangesAsync();

        // Staff purchases go on the staff account (a credit tender) of an active employee.
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        CompleteSaleRequest Sale(string number, DateOnly date, decimal quantity, PosTenderRequest tender) => new()
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = date, WarehouseId = shop.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(item.Id, "Soda 500ml", quantity, 2m, quantity * 2m, 0m, "4000")], Tenders = [tender],
        };

        (await pos.CompleteSaleAsync(Sale("RC-S0", new DateOnly(2026, 3, 2), 1m, new PosTenderRequest(TenderType.Cash, Scenario.Usd, 2m, "1000", false, StaffEmployeeId: rae.Id))))
            .Error.Code.Should().Be("Pos.StaffTender");
        (await pos.CompleteSaleAsync(Sale("RC-S0", new DateOnly(2026, 3, 2), 1m, new PosTenderRequest(TenderType.Credit, Scenario.Usd, 2m, "1491", false, StaffEmployeeId: 999_999))))
            .Error.Code.Should().Be("Pos.StaffNotFound");
        (await pos.CompleteSaleAsync(Sale("RC-S1", new DateOnly(2026, 3, 2), 11.5m, new PosTenderRequest(TenderType.Credit, Scenario.Usd, 23m, "1491", false, StaffEmployeeId: rae.Id))))
            .IsSuccess.Should().BeTrue();

        // March: rent billed, then payroll deducts the 23 of purchases and the 300 rent, and receipts the rent to the tenancy.
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 3, 1))).IsSuccess.Should().BeTrue();
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        (await payroll.CalculateAsync(companyId, rae.Id)).Value.Lines.Should().Contain([new PayLine("STAFF", 23m), new PayLine("RENT", 300m)]);
        var march = await payroll.RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", "RC-03");
        march.IsSuccess.Should().BeTrue(march.IsFailure ? march.Error.ToString() : null);
        march.Value.TotalNet.Should().Be(1000m - 323m + 800m);
        decimal Balance() => db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == tenantAccount.Id && i.Ledger == TradingLedger.AccountsReceivable)
            .AsEnumerable().Sum(i => i.Sign == LedgerSign.Debit ? i.OriginalBase : -i.OriginalBase);
        Balance().Should().Be(0m);

        // April: another purchase and month of rent - then the run is reversed, putting both balances back.
        (await pos.CompleteSaleAsync(Sale("RC-S2", new DateOnly(2026, 4, 3), 5m, new PosTenderRequest(TenderType.Credit, Scenario.Usd, 10m, "1491", false, StaffEmployeeId: rae.Id))))
            .IsSuccess.Should().BeTrue();
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 4, 1))).IsSuccess.Should().BeTrue();
        (await payroll.RunAsync(companyId, "2026-04", new DateOnly(2026, 4, 25), "2420", "RC-04")).Value.TotalNet.Should().Be(1000m - 310m + 800m);
        Balance().Should().Be(0m);
        var reversal = await scope.ServiceProvider.GetRequiredService<IPayrollAdjustmentService>().ReverseRunAsync(companyId, "RC-04", new DateOnly(2026, 4, 28), "Run in error", approver.Id);
        reversal.IsSuccess.Should().BeTrue(reversal.IsFailure ? reversal.Error.ToString() : null);
        Balance().Should().Be(300m);
        (await payroll.CalculateAsync(companyId, rae.Id)).Value.Lines.Should().Contain([new PayLine("STAFF", 10m), new PayLine("RENT", 300m)]);

        // The ledger: the staff account and the rent clearing account clear to zero.
        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Where(r => r.AccountCode == "1491").Sum(r => r.Debit - r.Credit).Should().Be(10m);
        tb.Rows.Where(r => r.AccountCode == "2491").Sum(r => r.Debit - r.Credit).Should().Be(0m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 4, 30) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("PR23")).Should().Contain("RC-1 Rae Tenant,0.00,33.00,23.00,10").And.NotContain("Sam Other");
        (await Render("PR24")).Should().Contain($"RC-1 Rae Tenant,{tenancyNumber},RR-1,300.00,600.00,300.00,300.00,300.00,0").And.NotContain("Sam Other");
    }
}
