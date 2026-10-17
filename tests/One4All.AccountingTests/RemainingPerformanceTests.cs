using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Payroll;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace One4All.AccountingTests;

/// <summary>
/// The last of the brief's §15 targets: a till ready to sell within 10 s of starting on a large catalogue; twenty terminals selling at once without
/// errors; and a thousand payslips rendered and queued for e-mail within the hour (HTTP concurrency is covered by the fifty-user scenario in
/// PerformanceTests). Heavy, so it runs only when ONE4ALL_PERF=1; sizes can be lowered with ONE4ALL_PERF_ITEMS and ONE4ALL_PERF_EMPLOYEES.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RemainingPerformanceTests
{
    private readonly AccountingFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RemainingPerformanceTests(AccountingFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private static bool Enabled => Environment.GetEnvironmentVariable("ONE4ALL_PERF") == "1";

    private static int Size(string variable, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(variable), out int n) && n > 0 ? n : fallback;

    [SkippableFact]
    public async Task A_till_is_ready_to_sell_within_10_seconds_on_a_large_catalogue()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        long terminalId;
        int items = Size("ONE4ALL_PERF_ITEMS", 20_000);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Branch branch = new(companyId, "PT", "Perf Till Branch");
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            PosTerminal terminal = new(companyId, branch.Id, "PERFT", "Perf Till");
            db.PosTerminals.Add(terminal);
            db.Warehouses.Add(new Warehouse(companyId, "PTW", "Perf Till Shop"));
            foreach (int i in Enumerable.Range(0, 40))
            {
                db.ItemCategories.Add(new ItemCategory(companyId, $"PC{i:00}", $"Category {i}"));
            }

            for (int i = 0; i < items; i++)
            {
                db.Items.Add(new Item(companyId, $"PT-{i:000000}", $"Perf product {i}") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000", Category = $"PC{i % 40:00}", TaxCode = i % 3 == 0 ? null : "VAT" });
                if (i % 2_000 == 1_999)
                {
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                }
            }

            await db.SaveChangesAsync();
            terminalId = terminal.Id;
        }

        // What the till does between its window opening and the first scan: the company and terminal, tax rates, the tiles, favourites, categories and tender currencies.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            IPosCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<IPosCatalogueService>();
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            Stopwatch watch = Stopwatch.StartNew();
            _ = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
            _ = await db.PosTerminals.AsNoTracking().FirstAsync(t => t.Id == terminalId);
            _ = await db.Set<TaxCode>().AsNoTracking().Include(t => t.Rates).Where(t => t.CompanyId == companyId).ToListAsync();
            int tiles = (await catalogue.TilesAsync(companyId)).Count;
            _ = await catalogue.FavouriteTilesAsync(companyId, terminalId, null);
            _ = await db.ItemCategories.AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync();
            _ = await pos.TenderCurrenciesAsync(companyId, DateOnly.FromDateTime(DateTime.Today));
            int found = (await catalogue.SearchAsync(companyId, "PT-0001")).Count;
            watch.Stop();
            _output.WriteLine($"Till ready in {watch.Elapsed.TotalSeconds:0.00} s with {items:N0} products ({tiles:N0} tiles, {found} search hits).");
            watch.Elapsed.TotalSeconds.Should().BeLessThan(10, "the brief's target: POS start-to-ready under 10 s");
        }
    }

    [SkippableFact]
    public async Task Twenty_terminals_sell_at_once_without_errors()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        long warehouseId;
        List<long> terminals = [];
        List<(long Id, string Description)> items = [];
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Branch branch = new(companyId, "P20", "Twenty Tills");
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            List<PosTerminal> tills = [.. Enumerable.Range(1, 20).Select(i => new PosTerminal(companyId, branch.Id, $"T{i:00}", $"Till {i}"))];
            db.PosTerminals.AddRange(tills);
            Warehouse wh = new(companyId, "P20W", "Twenty Tills Shop");
            db.Warehouses.Add(wh);
            List<Item> products = [.. Enumerable.Range(1, 30).Select(i => new Item(companyId, $"T20-{i:00}", $"Till product {i}") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" })];
            db.Items.AddRange(products);
            await db.SaveChangesAsync();
            terminals = [.. tills.Select(t => t.Id)];
            items = [.. products.Select(p => (p.Id, p.Description))];
            warehouseId = wh.Id;
        }

        const int salesPerTill = 10;
        int failures = 0;
        List<string> errors = [];
        Stopwatch watch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(terminals.Select((id, index) => (Id: id, Index: index)), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (till, ct) =>
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            var opened = await pos.OpenTillAsync(companyId, till.Id, cashierUserId: 100 + till.Index, openingFloat: 50m, ct);
            if (opened.IsFailure)
            {
                Interlocked.Increment(ref failures);
                lock (errors) { errors.Add($"open {till.Index}: {opened.Error.Message}"); }
                return;
            }

            for (int sale = 0; sale < salesPerTill; sale++)
            {
                Core.Common.Result<CompleteSaleResult> completed;
                try
                {
                    completed = await pos.CompleteSaleAsync(new CompleteSaleRequest
                {
                    CompanyId = companyId, TerminalId = till.Id, TillSessionId = opened.Value, CashierUserId = 100 + till.Index, SaleNumber = $"T{till.Index:00}-{sale:000}", SaleDate = new DateOnly(2026, 3, 3),
                    WarehouseId = warehouseId, TaxAccountCode = "2200",
                    Lines = [.. items.Skip((till.Index + sale) % 10).Take(5).Select((item, i) => new PosSaleLineRequest(item.Id, item.Description, 1m + i, 4m, 4m * (1m + i), 0m, "4000"))],
                    Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 4m * 15m, "1000", IsChange: false)],
                    }, ct);
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    Interlocked.Increment(ref failures);
                    lock (errors) { errors.Add($"till {till.Index} sale {sale}: concurrency on {string.Join(", ", ex.Entries.Select(e => e.Metadata.ClrType.Name))}"); }
                    continue;
                }

                if (completed.IsFailure)
                {
                    Interlocked.Increment(ref failures);
                    lock (errors) { errors.Add($"till {till.Index} sale {sale}: {completed.Error.Message}"); }
                }
            }
        });
        watch.Stop();
        _output.WriteLine($"20 tills × {salesPerTill} sales in {watch.Elapsed.TotalSeconds:0.0} s; failures {failures}. {string.Join(" | ", errors.Take(3))}");
        failures.Should().Be(0, string.Join("; ", errors.Take(5)));
        watch.Elapsed.TotalSeconds.Should().BeLessThan(120, "twenty tills must keep selling together");
    }

    [SkippableFact]
    public async Task A_thousand_payslips_are_rendered_and_queued_for_email_within_five_minutes()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        int employees = Size("ONE4ALL_PERF_EMPLOYEES", 1_000);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 3, "2400") { Formula = "TAXABLE * 20 / 100" };
        db.PayElements.AddRange(basic, paye);
        (await db.Companies.FirstAsync(c => c.Id == companyId)).PayslipPasswordRule = PayslipPasswordRule.EmployeeSet;
        await db.SaveChangesAsync();
        for (int i = 0; i < employees; i++)
        {
            Employee employee = new(companyId, $"PE{companyId % 10000}-{i:0000}", $"Employee {i}", 800m + (i % 50) * 20m, new DateOnly(2024, 1, 1)) { Email = $"employee{i}@example.test", PayslipPassword = "slip" };
            employee.AssignPayElement(basic.Id);
            employee.AssignPayElement(paye.Id);
            db.Employees.Add(employee);
        }

        await db.SaveChangesAsync();
        string runNumber = $"PE-{companyId}";
        var run = await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-04", new DateOnly(2026, 4, 25), "2420", runNumber);
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.Message : null);

        Stopwatch watch = Stopwatch.StartNew();
        var emailed = await scope.ServiceProvider.GetRequiredService<IPayslipDistributionService>().EmailRunAsync(companyId, runNumber);
        watch.Stop();
        emailed.IsSuccess.Should().BeTrue(emailed.IsFailure ? emailed.Error.Message : null);
        _output.WriteLine($"{emailed.Value.Queued:N0} payslips rendered and queued in {watch.Elapsed.TotalSeconds:0.0} s ({emailed.Value.NoEmail.Count} without e-mail, {emailed.Value.NoPassword.Count} without a password).");
        emailed.Value.Queued.Should().Be(employees);
        watch.Elapsed.TotalMinutes.Should().BeLessThan(5, "the brief's target (§15): payslip PDFs generated and e-mailed for 1,000 employees in under 5 minutes");
    }
}
