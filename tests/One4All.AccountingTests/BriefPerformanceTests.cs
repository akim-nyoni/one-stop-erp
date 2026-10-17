using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Budgeting;
using One4All.Core.Housing;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace One4All.AccountingTests;

/// <summary>
/// The rest of the brief's §15 targets: a POS sale posted and its receipt printed within 2 s; a list's first page within 1 s on a million rows; an allocation
/// run over a 100,000-row waiting list proposed within 60 s and the duplicate sweep across that list within 10 minutes; 40 departments preparing budgets at
/// once without contention; and a driver change over 5,000 lines previewed and applied within 10 s. Heavy, so it runs only when ONE4ALL_PERF=1; it cleans up
/// the bulk rows it creates.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BriefPerformanceTests
{
    private readonly AccountingFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BriefPerformanceTests(AccountingFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private static bool Enabled => Environment.GetEnvironmentVariable("ONE4ALL_PERF") == "1";

    [SkippableFact]
    public async Task A_pos_sale_is_posted_and_printed_within_2_seconds()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "PF", "Perf Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "PERF1", "Perf Till");
        Warehouse wh = new(companyId, "PFW", "Perf Shop");
        List<Item> items = [.. Enumerable.Range(1, 20).Select(i => new Item(companyId, $"PS-{i:00}", $"Perf item {i}") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" })];
        db.PosTerminals.Add(terminal);
        db.Warehouses.Add(wh);
        db.Items.AddRange(items);
        await db.SaveChangesAsync();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        IPosReceiptService receipts = scope.ServiceProvider.GetRequiredService<IPosReceiptService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 5, openingFloat: 50m)).Value;
        DateOnly day = new(2026, 3, 2);

        List<double> seconds = [];
        for (int sale = 0; sale < 6; sale++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            var completed = await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 5, SaleNumber = $"PS-{sale}", SaleDate = day, WarehouseId = wh.Id, TaxAccountCode = "2200",
                Lines = [.. items.Take(12).Select((item, i) => new PosSaleLineRequest(item.Id, item.Description, 1m + i, 10m, 10m * (1m + i), 0m, "4000"))],
                Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 10m * 78m, "1000", IsChange: false)],
            });
            completed.IsSuccess.Should().BeTrue(completed.IsFailure ? completed.Error.Message : null);
            (await receipts.RenderReceiptTextAsync(companyId, completed.Value.SaleId)).IsSuccess.Should().BeTrue();
            watch.Stop();
            seconds.Add(watch.Elapsed.TotalSeconds);
        }

        _output.WriteLine($"12-line sale posted and printed: {string.Join(", ", seconds.Select(s => s.ToString("0.00")))} s (the first warms up).");
        seconds.Skip(1).Max().Should().BeLessThan(2, "the brief's target: sale completion (post + print) under 2 s");
    }

    [SkippableFact]
    public async Task A_list_first_page_arrives_within_a_second_on_a_million_rows()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        const int rows = 1_000_000;
        try
        {
            Stopwatch seeding = Stopwatch.StartNew();
            db.Database.SetCommandTimeout(600);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ar.customer (company_id, code, name, currency, credit_limit, payment_terms_days, settlement_discount_percent, settlement_discount_days, ageing_basis, statement_type, "
                + "on_hold, is_active, interest_exempt, credit_limit_policy, created_on_utc, created_by_user_id, is_deleted) "
                + "SELECT {0}, 'LP' || lpad(n::text, 7, '0'), 'List customer ' || n, 'USD', 0, 30, 0, 0, 0, 0, false, true, false, 0, now(), 0, false FROM generate_series(1, {1}) AS s(n)",
                companyId, rows);
            await db.Database.ExecuteSqlRawAsync("ANALYZE ar.customer");
            _output.WriteLine($"{rows:N0} customers seeded (and the table analysed, as autovacuum would) in {seeding.Elapsed.TotalSeconds:0.0} s.");

            // The desktop's list: the company's rows in code order, one server-side page at a time (first page, then a page deep in the list).
            async Task<double> PageAsync(int page)
            {
                Stopwatch watch = Stopwatch.StartNew();
                List<Customer> fetched = await db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.Code).Skip(page * 500).Take(500).ToListAsync();
                watch.Stop();
                fetched.Should().HaveCount(500);
                return watch.Elapsed.TotalSeconds;
            }

            await PageAsync(0);
            double first = await PageAsync(0), deep = await PageAsync(1_000), count = 0;
            Stopwatch counting = Stopwatch.StartNew();
            (await db.Customers.AsNoTracking().CountAsync(c => c.CompanyId == companyId)).Should().BeGreaterThanOrEqualTo(rows);
            count = counting.Elapsed.TotalSeconds;
            _output.WriteLine($"First page {first:0.000} s, page 1,000 {deep:0.000} s, count {count:0.000} s.");
            first.Should().BeLessThan(1, "the brief's target: a list's first page within 1 s on 1M rows");
            deep.Should().BeLessThan(5, "paging stays server-side: a page half a million rows in still comes back in a few seconds (an offset that deep reads the index up to it)");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM ar.customer WHERE company_id = {0} AND code LIKE 'LP%'", companyId);
        }
    }

    [SkippableFact]
    public async Task An_allocation_run_and_the_duplicate_sweep_over_100k_applicants_meet_their_targets()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        int applicants = int.TryParse(Environment.GetEnvironmentVariable("ONE4ALL_PERF_APPLICANTS"), out int n) && n > 0 ? n : 100_000;
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        HousingScheme scheme = new(companyId, $"PF-{companyId}", "Perf scheme", SchemeType.CouncilDelivered);
        LandParcel parcel = new(companyId, "PF-P1", "Perf layout", 60_000m, 600_000m);
        db.HousingSchemes.Add(scheme);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        const int stands = 200;
        db.HousingStands.AddRange(Enumerable.Range(1, stands).Select(i => new HousingStand(companyId, parcel.Id, scheme.Id, $"PF-{i:000}", 300m, 3_000m, new DateOnly(2026, 1, 5))));
        await db.SaveChangesAsync();

        string[] first = ["Tendai", "Rudo", "Chipo", "Tawanda", "Nyasha", "Farai", "Tatenda", "Kudzai", "Rutendo", "Simba", "Anesu", "Vimbai"];
        string[] last = ["Moyo", "Ncube", "Sibanda", "Dube", "Chikwanda", "Mutasa", "Mpofu", "Nyoni", "Gumbo", "Chirwa", "Banda", "Mlambo", "Zhou", "Marufu", "Chari"];
        Stopwatch seeding = Stopwatch.StartNew();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        for (int start = 0; start < applicants; start += 5_000)
        {
            for (int i = start; i < Math.Min(start + 5_000, applicants); i++)
            {
                DateOnly registered = new DateOnly(2005, 1, 1).AddDays(i % 7_000);
                HousingApplicant applicant = new(companyId, $"{10 + i % 80}-{i:000000}{(char)('A' + i % 26)}{10 + i % 90}", $"{first[i % first.Length]} {last[(i / 7) % last.Length]}", registered);
                applicant.SetDetails(HousingCategory.HighDensity, new DateOnly(1950, 1, 1).AddDays(i % 18_000), registered.AddYears(-3), i % 6, i % 23 == 0, 100m + i % 900, CurrentTenure.Lodger);
                applicant.Phone = $"+2637{i % 10}{i:0000000}";
                applicant.Register($"PFWL-{i:000000}", registered);
                db.HousingApplicants.Add(applicant);
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        db.ChangeTracker.AutoDetectChangesEnabled = true;
        _output.WriteLine($"{applicants:N0} applicants seeded in {seeding.Elapsed.TotalSeconds:0.0} s.");

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        Stopwatch watch = Stopwatch.StartNew();
        var proposed = await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        });
        watch.Stop();
        proposed.IsSuccess.Should().BeTrue(proposed.IsFailure ? proposed.Error.Message : null);
        proposed.Value.Lines.Should().HaveCount(stands);
        proposed.Value.Lines[0].WaitingListNumber.Should().Be("PFWL-000000", "seniority: the earliest registration leads");
        _output.WriteLine($"Allocation run over {applicants:N0} applicants proposed {stands} stands in {watch.Elapsed.TotalSeconds:0.0} s.");
        watch.Elapsed.TotalSeconds.Should().BeLessThan(60d * Math.Max(1, applicants) / 100_000, "the brief's target: a 100,000-row waiting list proposed in under 60 s (pro rata for a smaller try)");

        IApplicantIntakeService intake = scope.ServiceProvider.GetRequiredService<IApplicantIntakeService>();
        watch.Restart();
        DuplicateScan scan = await intake.ScanAsync(companyId, new DateOnly(2026, 3, 1));
        watch.Stop();
        _output.WriteLine($"Duplicate sweep over {applicants:N0} applicants: {scan.Opened} cases opened in {watch.Elapsed.TotalSeconds:0.0} s.");
        watch.Elapsed.TotalMinutes.Should().BeLessThan(10d * Math.Max(1, applicants) / 100_000, "the brief's target: the nightly duplicate sweep within 10 minutes (pro rata for a smaller try)");
    }

    [SkippableFact]
    public async Task Forty_departments_prepare_budgets_at_once_without_contention()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId, yearId;
        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
        {
            One4AllDbContext db = setup.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            db.Companies.Single(c => c.Id == companyId).BudgetApprovalRequired = true;
            await db.SaveChangesAsync();
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        }

        string[] accounts = ["6000", "6100", "6200", "6300", "6400", "6500", "6600", "6700", "6800", "6900"];
        Stopwatch watch = Stopwatch.StartNew();
        string?[] failures = await Task.WhenAll(Enumerable.Range(1, 40).Select(async department =>
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
            IBudgetPreparationService prep = scope.ServiceProvider.GetRequiredService<IBudgetPreparationService>();
            string code = $"D{department:00}";
            List<NewBudgetLine> lines = [.. accounts.SelectMany(a => Enumerable.Range(1, 12).Select(p => new NewBudgetLine(a, p, 100m * department + p)))];
            var created = await budgeting.CreateBudgetAsync(companyId, yearId, $"2026 {code}", BudgetType.OriginalApproved, code, lines);
            if (created.IsFailure)
            {
                return $"{code}: {created.Error.Message}";
            }

            for (int round = 0; round < 3; round++)
            {
                var revised = await prep.ReviseAsync(companyId, created.Value, 100 + department, new ProposedFigure(accounts[round], 1, 50m + round));
                if (revised.IsFailure)
                {
                    return $"{code}: {revised.Error.Message}";
                }
            }

            var submitted = await prep.SubmitAsync(companyId, created.Value, 100 + department, "Ready");
            return submitted.IsFailure ? $"{code}: {submitted.Error.Message}" : null;
        }));
        watch.Stop();
        failures.Where(f => f is not null).Should().BeEmpty("40 departments prepare concurrently with no lock contention");
        _output.WriteLine($"40 departments each created a 120-line budget, revised it three times and submitted it, all at once, in {watch.Elapsed.TotalSeconds:0.0} s.");
        watch.Elapsed.TotalSeconds.Should().BeLessThan(60, "no department waits on another");
    }

    [SkippableFact]
    public async Task A_driver_change_over_5000_lines_previews_and_applies_within_10_seconds()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(Enabled, "Set ONE4ALL_PERF=1 to run the performance pack.");
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        IBudgetPlanningService planning = scope.ServiceProvider.GetRequiredService<IBudgetPlanningService>();
        (await planning.SetDriverAsync(companyId, yearId, "FEE", 100m, commit: true, "Fee per learner", "USD")).IsSuccess.Should().BeTrue();

        // 5,000 driver-based lines: ten accounts × twelve periods × forty-two cost centres, each 40 learners × FEE.
        string[] accounts = ["6000", "6100", "6200", "6300", "6400", "6500", "6600", "6700", "6800", "6900"];
        List<NewBudgetLine> lines = [.. accounts.SelectMany(a => Enumerable.Range(1, 12).SelectMany(p => Enumerable.Range(1, 42).Select(cc => new NewBudgetLine(a, p, 4_000m, $"CC{cc:00}"))))];
        lines.Count.Should().BeGreaterThanOrEqualTo(5_000);
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026 driven", BudgetType.OriginalApproved, null, lines)).Value;
        Budget budget = await db.Set<Budget>().Include(b => b.Lines).SingleAsync(b => b.Id == budgetId);
        foreach (BudgetLine line in budget.Lines)
        {
            line.DriverCode = "FEE";
            line.Quantity = 40m;
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Stopwatch watch = Stopwatch.StartNew();
        var preview = await planning.SetDriverAsync(companyId, yearId, "FEE", 110m, commit: false);
        double previewing = watch.Elapsed.TotalSeconds;
        preview.IsSuccess.Should().BeTrue(preview.IsFailure ? preview.Error.Message : null);
        preview.Value.Should().HaveCount(lines.Count).And.OnlyContain(x => x.Before == 4_000m && x.After == 4_400m);
        watch.Restart();
        (await planning.SetDriverAsync(companyId, yearId, "FEE", 110m, commit: true)).IsSuccess.Should().BeTrue();
        watch.Stop();
        (await db.Set<BudgetLine>().AsNoTracking().Where(l => l.BudgetId == budgetId).SumAsync(l => l.Amount)).Should().Be(4_400m * lines.Count);
        _output.WriteLine($"Driver change over {lines.Count:N0} lines: previewed in {previewing:0.0} s, applied in {watch.Elapsed.TotalSeconds:0.0} s.");
        previewing.Should().BeLessThan(10, "the brief's target: a driver change re-computes and previews in under 10 s");
        watch.Elapsed.TotalSeconds.Should().BeLessThan(10, "and applies as quickly");
    }
}
