using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Onboarding;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the module register importers: a file with a bad line writes nothing and reports the line; fixed assets come in with cost and
/// depreciation to date (posted against suspense, the next depreciation run charging only the following month); employees, then their
/// loan balances; properties with their services; students under their guardians; a second import skips what is already there; the
/// registers agree with their accounts.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class VerticalImportTests
{
    private readonly AccountingFixture _fixture;

    public VerticalImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Module_registers_import_validated_and_reconcile_to_their_accounts()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "1491", "Vehicles at cost", AccountType.Asset), new LedgerAccount(companyId, "1492", "Vehicles depreciation", AccountType.Asset),
            new LedgerAccount(companyId, "6191", "Depreciation", AccountType.Expense), new LedgerAccount(companyId, "2991", "Migration suspense", AccountType.Liability));
        db.Customers.Add(new Customer(companyId, "VI-OWN", "Owner Parent", Scenario.Usd));
        db.Tariffs.Add(new Tariff(companyId, "VI-WAT", "Water", ServiceType.Water, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 10m });
        await db.SaveChangesAsync();
        IVerticalImportService import = scope.ServiceProvider.GetRequiredService<IVerticalImportService>();
        DateOnly asAt = new(2026, 3, 31);

        // Fixed assets: a bad line stops the file; fixed, it posts cost 12,000 and depreciation 3,000 against suspense.
        const string assetHeader = "asset_number,description,class,acquired,cost,accumulated,life_months,cost_account,accum_account,expense_account\n";
        ImportReport bad = await import.ImportFixedAssetsAsync(companyId, assetHeader + "VI-T1,Truck,Vehicles,2025-04-01,12000,3000,48,1491,1492,6191\nVI-T2,Van,Vehicles,2026-01-01,5000,6000,48,1491,1492,6191\n",
            asAt, "2991", commit: true);
        (bad.Committed, bad.Errors.Single().LineNumber).Should().Be((false, 3));
        (await db.FixedAssets.CountAsync(a => a.CompanyId == companyId)).Should().Be(0);
        ImportReport assets = await import.ImportFixedAssetsAsync(companyId, assetHeader + "VI-T1,Truck,Vehicles,2025-04-01,12000,3000,48,1491,1492,6191\n", asAt, "2991", commit: true);
        (assets.Committed, assets.Created).Should().Be((true, 1));
        (await import.ImportFixedAssetsAsync(companyId, assetHeader + "VI-T1,Truck,Vehicles,2025-04-01,12000,3000,48,1491,1492,6191\n", asAt, "2991", commit: true)).Skipped.Should().Be(1);
        (await scope.ServiceProvider.GetRequiredService<IFixedAssetService>().RunDepreciationAsync(companyId, asAt, "VI-DEP-03")).Value.TotalDepreciation.Should().Be(0m, "March is already in");
        (await scope.ServiceProvider.GetRequiredService<IFixedAssetService>().RunDepreciationAsync(companyId, new DateOnly(2026, 4, 30), "VI-DEP-04")).Value.TotalDepreciation.Should().Be(250m);

        // Employees, then their loans.
        (await import.ImportEmployeeLoansAsync(companyId, "employee_number,reference,outstanding,instalment,loan_account\nVI-E1,LN-1,600,100,1350\n", asAt, "2991", commit: true))
            .Errors.Single().Message.Should().Contain("import employees first");
        (await import.ImportEmployeesAsync(companyId, "employee_number,name,basic_salary,engaged,department\nVI-E1,Ann Clerk,1500,2020-02-01,FIN\nVI-E2,Bob Driver,1200,2021-06-01,TRANSPORT\n", commit: true))
            .Errors.Single().Message.Should().Contain("three characters");
        (await import.ImportEmployeesAsync(companyId, "employee_number,name,basic_salary,engaged,department\nvi-e1,Ann Clerk,1500,2020-02-01,FIN\n", commit: true)).Created.Should().Be(1);

        // The payroll details come with the employee: tax and bank, how and how often paid, the contract.
        const string detailed = "employee_number,name,basic_salary,engaged,tax_number,date_of_birth,gender,employment_type,payment_method,bank_name,bank_account,mobile_money,pay_group,contract_end\n";
        (await import.ImportEmployeesAsync(companyId, detailed + "VI-E3,Chipo Moyo,900,2024-01-01,T-77,1990-05-04,Female,Contract,MobileMoney,,,,,2026-12-31\n", commit: true))
            .Errors.Single().Message.Should().Contain("mobile money number");
        (await import.ImportEmployeesAsync(companyId, detailed + "VI-E3,Chipo Moyo,900,2024-01-01,T-77,1990-05-04,Female,Contract,BankTransfer,CBZ,4400123,,wages,2026-12-31\n", commit: true))
            .Created.Should().Be(1);
        Core.Payroll.Employee chipo = await db.Employees.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.EmployeeNumber == "VI-E3");
        (chipo.TaxNumber, chipo.DateOfBirth, chipo.Gender, chipo.EmploymentType, chipo.BankAccountNumber, chipo.PayGroup, chipo.ContractEndDate)
            .Should().Be(("T-77", new DateOnly(1990, 5, 4), Core.Payroll.Gender.Female, Core.Payroll.EmploymentType.Contract, "4400123", "WAGES", new DateOnly(2026, 12, 31)));
        (await import.ImportEmployeeLoansAsync(companyId, "employee_number,reference,outstanding,instalment,loan_account\nVI-E1,LN-1,600,100,1350\n", asAt, "2991", commit: true)).Created.Should().Be(1);

        // Properties with their water service, and a student under the same customer.
        (await import.ImportPropertiesAsync(companyId, "stand,ward,ratepayer,valuation,size,connected,services\nVI-S1,Ward 4,VI-OWN,50000,600,2026-01-01,Water:VI-WAT\nVI-S2,Ward 4,VI-OWN,1,1,,Gas:VI-WAT\n",
            commit: true)).Errors.Single().LineNumber.Should().Be(3);
        (await import.ImportPropertiesAsync(companyId, "stand,ward,ratepayer,valuation,size,connected,services\nVI-S1,Ward 4,VI-OWN,50000,600,2026-01-01,Water:vi-wat\n", commit: true)).Created.Should().Be(1);
        Property stand = await db.Properties.AsNoTracking().Include(p => p.Services).SingleAsync(p => p.CompanyId == companyId && p.StandNumber == "VI-S1");
        (stand.Valuation, stand.Services.Single().ServiceType).Should().Be((50000m, ServiceType.Water));
        (await import.ImportStudentsAsync(companyId, "admission,name,grade,guardian,boarder\nVI-001,Kid One,G1,VI-OWN,true\n", commit: true)).Created.Should().Be(1);

        // Suspense carries the other side (cost 12,000 − depreciation 3,000 + loans 600 = 9,600 credit) until the trial balance clears it; the registers agree.
        decimal suspense = await db.Journals.Where(j => j.CompanyId == companyId && j.Status == JournalStatus.Posted).SelectMany(j => j.Lines).Where(l => l.AccountCode == "2991")
            .SumAsync(l => l.BaseCredit - l.BaseDebit);
        suspense.Should().Be(9_600m);
        IReadOnlyList<RegisterCheck> checks = (await import.ReconcileRegistersAsync(companyId)).Value;
        checks.Select(c => (c.Register, c.RegisterBalance, c.Agrees)).Should().Equal(("Fixed assets (net book value)", 8_750m, true), ("Staff loans", 600m, true));
    }
}
