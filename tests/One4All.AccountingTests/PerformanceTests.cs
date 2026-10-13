using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Core.Municipal;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using One4All.Reporting.Model;
using Xunit;
using Xunit.Abstractions;

namespace One4All.AccountingTests;

/// <summary>
/// The performance pack (brief §15 non-functional targets): item lookup under 100 ms on 100,000 SKUs, a 100,000-row report rendered within 5 s, and a
/// payroll of 1,000 employees calculated in under 60 s, and a billing run of 50,000 municipal accounts proposed and posted within 15 minutes
/// (ONE4ALL_PERF_BILLING sets a smaller count to try it).
/// Heavy, so it runs only when ONE4ALL_PERF=1; it cleans up the items it creates.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PerformanceTests
{
    private const int Skus = 100_000;
    private readonly AccountingFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PerformanceTests(AccountingFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private static bool Enabled => Environment.GetEnvironmentVariable("ONE4ALL_PERF") == "1";

    [SkippableFact]
    public async Task Item_lookup_on_100k_skus_is_under_100_ms()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string[] words = ["Cola", "Bread", "Milk", "Soap", "Rice", "Sugar", "Flour", "Tea", "Coffee", "Juice", "Beans", "Salt", "Oil", "Maize", "Eggs"];
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        Stopwatch seeding = Stopwatch.StartNew();
        for (int start = 0; start < Skus; start += 5000)
        {
            for (int i = start; i < start + 5000; i++)
            {
                db.Items.Add(new Item(companyId, $"PF{i:000000}", $"{words[i % words.Length]} {i % 977}g pack {i}") { RevenueAccountCode = "4000", Brand = words[(i / 7) % words.Length] });
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        _output.WriteLine($"Seeded {Skus:N0} items in {seeding.Elapsed.TotalSeconds:0.0} s.");
        try
        {
            IItemCatalogueService items = scope.ServiceProvider.GetRequiredService<IItemCatalogueService>();
            await items.SearchAsync(companyId, "coffee 500", 30);                       // warm-up
            List<double> timings = [];
            foreach (string term in new[] { "cola 12", "PF054321", "sugar pack 9", "bre", "juice 300g" })
            {
                Stopwatch watch = Stopwatch.StartNew();
                IReadOnlyList<ItemSearchHit> hits = await items.SearchAsync(companyId, term, 30);
                timings.Add(watch.Elapsed.TotalMilliseconds);
                hits.Should().NotBeEmpty($"\"{term}\" matches items");
            }

            double median = timings.Order().ElementAt(timings.Count / 2);
            _output.WriteLine($"Item lookup on {Skus:N0} SKUs: {string.Join(", ", timings.Select(t => $"{t:0} ms"))}; median {median:0} ms.");
            median.Should().BeLessThan(100, "the brief's target is under 100 ms on 100k SKUs");
        }
        finally
        {
            await db.Items.Where(i => i.CompanyId == companyId).ExecuteDeleteAsync();
        }
    }

    [SkippableFact]
    public async Task A_100k_row_report_has_its_first_page_within_5_seconds()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

        // The trial balance layout over a generated 100,000-row source.
        ReportDefinition big = SystemReports.TrialBalance();
        big.DataSources.Clear();
        big.DataSources.Add(new ReportDataSource
        {
            Name = "Main", Kind = DataSourceKind.Sql,
            Command = "SELECT 'A' || g AS \"AccountCode\", 'Account ' || g AS \"AccountName\", (g % 97) * 10.5 AS \"Debit\", (g % 89) * 7.25 AS \"Credit\" FROM generate_series(1, 100000) g WHERE @CompanyId > 0",
        });
        (await scope.ServiceProvider.GetRequiredService<IReportDefinitionService>().SaveAsync(companyId, big.Code, big.Name, big.Module, ReportDefinitionSerializer.Serialize(big), null, "perf"))
            .IsSuccess.Should().BeTrue();

        // Where the time goes: fetching the rows, laying out the pages, writing the PDF.
        Stopwatch stage = Stopwatch.StartNew();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = await scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("sql:" + big.DataSources[0].Command))
            .GetRowsAsync(companyId, "sql:" + big.DataSources[0].Command, new Dictionary<string, object?>());
        double fetch = stage.Elapsed.TotalSeconds;
        stage.Restart();
        One4All.Reporting.Engine.RenderModel model = new One4All.Reporting.Engine.ReportEngine().Build(new One4All.Reporting.Engine.ReportInput
        {
            Definition = big, Rows = rows, CompanyName = "Perf", Parameters = new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) },
        });
        double layout = stage.Elapsed.TotalSeconds;
        stage.Restart();
        byte[] pdf = new One4All.Reporting.Engine.PdfReportRenderer().Render(model);
        _output.WriteLine($"Stages: fetch {fetch:0.00} s, layout {layout:0.00} s, PDF {stage.Elapsed.TotalSeconds:0.00} s ({pdf.Length / 1024:N0} KB).");

        Stopwatch watch = Stopwatch.StartNew();
        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, big.Code,
            new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Pdf);
        watch.Stop();
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : string.Empty);
        _output.WriteLine($"100,000-row report rendered to PDF ({rendered.Value.Content.Length / 1024:N0} KB) in {watch.Elapsed.TotalSeconds:0.00} s.");
        (fetch + layout).Should().BeLessThan(5, "the brief's target: the first page within 5 s (rows fetched and every page laid out)");
        watch.Elapsed.TotalSeconds.Should().BeLessThan(30, "the whole 100,000-row PDF (some 2,000 pages) is written within half a minute");
    }

    [SkippableFact]
    public async Task A_payroll_of_1000_employees_is_calculated_within_a_minute()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement housing = new(companyId, "HOUSING", "Housing", PayElementType.Earning, CalculationBasis.PercentOfBasic, 2, "6100") { Rate = 10m, Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 3, "2400") { Formula = "TAXABLE * 20 / 100" };
        db.PayElements.AddRange(basic, housing, paye);
        await db.SaveChangesAsync();
        for (int i = 0; i < 1_000; i++)
        {
            Employee employee = new(companyId, $"PERF{companyId % 10000}-{i:0000}", $"Employee {i}", 800m + (i % 50) * 20m, new DateOnly(2024, 1, 1));
            employee.AssignPayElement(basic.Id);
            employee.AssignPayElement(housing.Id);
            employee.AssignPayElement(paye.Id);
            db.Employees.Add(employee);
        }

        await db.SaveChangesAsync();
        Stopwatch watch = Stopwatch.StartNew();
        var run = await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", $"PERF-{companyId}");
        watch.Stop();
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.Message : null);
        _output.WriteLine($"Payroll of 1,000 employees calculated and posted in {watch.Elapsed.TotalSeconds:0.0} s.");
        watch.Elapsed.TotalSeconds.Should().BeLessThan(60, "the brief's target: 1,000 employees in under a minute");
    }

    [SkippableFact]
    public async Task A_billing_run_of_50000_accounts_posts_within_15_minutes()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        int accounts = int.TryParse(Environment.GetEnvironmentVariable("ONE4ALL_PERF_BILLING"), out int n) && n > 0 ? n : 50_000;
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff refuse = new(companyId, "REF-PERF", "Refuse Removal", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 20m };
        db.Tariffs.Add(refuse);
        await db.SaveChangesAsync();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        for (int start = 0; start < accounts; start += 2_000)
        {
            List<Customer> owners = [.. Enumerable.Range(start, Math.Min(2_000, accounts - start)).Select(i => new Customer(companyId, $"BP{i:000000}", $"Ratepayer {i}", Scenario.Usd))];
            db.Customers.AddRange(owners);
            await db.SaveChangesAsync();
            foreach (Customer owner in owners)
            {
                Property stand = new(companyId, "S" + owner.Code, "Ward P", owner.Id);
                stand.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
                db.Properties.Add(stand);
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        db.ChangeTracker.AutoDetectChangesEnabled = true;
        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        Stopwatch watch = Stopwatch.StartNew();
        var proposed = await runs.ProposeAsync(new BillingRunProposal
        {
            CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 5, 31), Ward = "Ward P", InvoicePrefix = "BP", UserId = 81,
        });
        proposed.IsSuccess.Should().BeTrue(proposed.IsFailure ? proposed.Error.Message : null);
        double proposing = watch.Elapsed.TotalSeconds;
        var posted = await runs.ApproveAsync(companyId, proposed.Value.RunNumber, 82, new BillingPostOptions { ChunkSize = 1_000, Parallelism = 6 });
        watch.Stop();
        posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.Message : null);
        string? why = (await runs.LinesAsync(companyId, proposed.Value.RunNumber, exceptionsOnly: true)).Value.Select(l => l.Message).FirstOrDefault();
        posted.Value.Posted.Should().Be(accounts, why);
        _output.WriteLine($"Billing run of {accounts:N0} accounts: proposed in {proposing:0.0} s, posted in {watch.Elapsed.TotalSeconds - proposing:0.0} s.");
        watch.Elapsed.TotalMinutes.Should().BeLessThan(15d * accounts / 50_000, "the brief's target: 50,000 accounts in under 15 minutes (pro rata for a smaller try)");
    }
}
