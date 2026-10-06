using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the budget's Excel round trip: export (identity row, P1–P12, a total formula), edits previewed then applied as revised figures
/// with a trail entry, a workbook with bad rows refused whole, a workbook from another budget or not a workbook at all refused, and no
/// import once the budget has been submitted.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetWorkbookTests
{
    private readonly AccountingFixture _fixture;

    public BudgetWorkbookTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_budget_goes_out_to_Excel_and_comes_back_checked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.CostCentres.Add(new CostCentre(companyId, "HQ", "Head Office"));
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "Admin 2026", BudgetType.OriginalApproved, "ADM",
            [new NewBudgetLine("6000", 1, 1_000m), new NewBudgetLine("6100", 1, 500m)])).Value;
        IBudgetWorkbookService workbooks = scope.ServiceProvider.GetRequiredService<IBudgetWorkbookService>();
        const long user = 21;

        (byte[] exported, string file) = (await workbooks.ExportAsync(companyId, budgetId, includeBudgetAccounts: false)).Value;
        file.Should().EndWith(".xlsx").And.Contain(budgetId.ToString());
        List<List<object?>> rows = [.. XlsxGrid.Read(exported).Select(r => r.Cast<object?>().ToList())];
        rows[0].Take(2).Should().Equal("One4All budget", budgetId.ToString());
        rows[1][3].Should().Be("P1");
        rows[2].Take(4).Should().Equal("6000", rows[2][1], null, "1000");
        rows.Should().HaveCount(4);

        // Edits made in Excel: 6000 gets a P2, rent is trimmed, and a new HQ row appears.
        static List<object?> Line(string account, string? cc, params decimal[] amounts) => [account, "name", cc, .. amounts.Select(a => (object?)a)];
        List<IReadOnlyList<object?>> edited = [rows[0], rows[1], Line("6000", null, 1_000m, 250m), Line("6100", null, 450m), Line("6000", "hq", 0m, 0m, 100m)];
        BudgetWorkbookImport preview = (await workbooks.ImportAsync(companyId, budgetId, XlsxGrid.Write("Budget", edited), user, commit: false)).Value;
        preview.Applied.Should().BeFalse();
        preview.Problems.Should().BeEmpty();
        preview.Changes.Should().BeEquivalentTo(new[]
        {
            new BudgetWorkbookChange("6000", null, 2, 0m, 250m), new BudgetWorkbookChange("6100", null, 1, 500m, 450m), new BudgetWorkbookChange("6000", "HQ", 3, 0m, 100m),
        });
        (preview.AnnualTotalBefore, preview.AnnualTotalAfter).Should().Be((1_500m, 1_800m));
        (await db.Set<Budget>().AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Id == budgetId)).AnnualTotal.Should().Be(1_500m);

        BudgetWorkbookImport applied = (await workbooks.ImportAsync(companyId, budgetId, XlsxGrid.Write("Budget", edited), user, commit: true)).Value;
        applied.Applied.Should().BeTrue();
        Budget budget = await db.Set<Budget>().AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Id == budgetId);
        budget.AnnualTotal.Should().Be(1_800m);
        budget.Lines.Should().ContainSingle(l => l.AccountCode == "6000" && l.CostCentre == "HQ" && l.Period == 3 && l.Amount == 100m);
        (await db.Set<BudgetReviewEvent>().AsNoTracking().Where(e => e.BudgetId == budgetId).Select(e => e.Comment).ToListAsync()).Should().ContainSingle(c => c!.Contains("Imported from Excel: 3"));

        // The same file again changes nothing.
        (await workbooks.ImportAsync(companyId, budgetId, XlsxGrid.Write("Budget", edited), user, commit: true)).Value.Should().Match<BudgetWorkbookImport>(i => i.Changes.Count == 0 && !i.Applied);

        // Bad rows: the whole workbook waits.
        List<IReadOnlyList<object?>> bad = [rows[0], rows[1], Line("9999", null, 1m), Line("6100", "NOPE", 1m), ["6100", "x", null, "lots"], Line("6000", null, -5m), Line("6000", null, 1m),
            [null, null, null, 12m]];
        BudgetWorkbookImport refused = (await workbooks.ImportAsync(companyId, budgetId, XlsxGrid.Write("Budget", bad), user, commit: true)).Value;
        refused.Applied.Should().BeFalse();
        refused.Problems.Select(p => p.Row).Should().Equal(3, 4, 5, 6, 7, 8);
        refused.Problems[5].Message.Should().Contain("without an account");
        (await db.Set<Budget>().AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Id == budgetId)).AnnualTotal.Should().Be(1_800m);

        // Another budget's workbook, or not a workbook.
        List<IReadOnlyList<object?>> foreign = [["One4All budget", budgetId + 1000], rows[1]];
        (await workbooks.ImportAsync(companyId, budgetId, XlsxGrid.Write("Budget", foreign), user, commit: false)).Error.Code.Should().Be("Budget.WorkbookMismatch");
        (await workbooks.ImportAsync(companyId, budgetId, [1, 2, 3], user, commit: false)).Error.Code.Should().Be("Budget.WorkbookUnreadable");

        // Once submitted, figures change through review, not imports.
        (await scope.ServiceProvider.GetRequiredService<IBudgetPreparationService>().SubmitAsync(companyId, budgetId, user)).IsSuccess.Should().BeTrue();
        edited[2] = Line("6000", null, 1_000m, 300m);
        (await workbooks.ImportAsync(companyId, budgetId, XlsxGrid.Write("Budget", edited), user, commit: true)).Error.Code.Should().Be("Budget.WrongStage");

        // Exporting with every budget account adds zero rows for the rest.
        (byte[] full, _) = (await workbooks.ExportAsync(companyId, budgetId, includeBudgetAccounts: true)).Value;
        XlsxGrid.Read(full).Count.Should().BeGreaterThanOrEqualTo(5);
    }
}
