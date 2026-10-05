using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the variance decomposition (B35): budget lines carry the volume and, for foreign-currency lines, the planned rate; actual
/// volumes are recorded; the year-to-date variance splits into volume, price and FX components that add up to the total.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class VarianceDecompositionTests
{
    private readonly AccountingFixture _fixture;

    public VarianceDecompositionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Variance_splits_into_volume_price_and_fx()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId && y.Code == "2026").Select(y => y.Id).SingleAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();

        (await budgeting.CreateBudgetAsync(companyId, yearId, "X", BudgetType.OriginalApproved, null, [new NewBudgetLine("4000", 1, 1000m, Quantity: 0m)])).Error.Code.Should().Be("Budget.LineQuantity");
        (await budgeting.CreateBudgetAsync(companyId, yearId, "X", BudgetType.OriginalApproved, null, [new NewBudgetLine("6000", 1, 1100m, Currency: Scenario.Eur)])).Error.Code.Should().Be("Budget.PlannedRate");

        // Sales: 100 units a month at 10. Imported supplies: EUR 1,000 a month planned at 1.10.
        List<NewBudgetLine> lines = [];
        for (int period = 1; period <= 12; period++)
        {
            lines.Add(new NewBudgetLine("4000", period, 1000m, Quantity: 100m));
            lines.Add(new NewBudgetLine("6000", period, 1100m, Currency: Scenario.Eur, PlannedRate: 1.10m));
        }

        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "PLAN", BudgetType.OriginalApproved, null, lines)).Value;
        (await budgeting.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();

        // Q1 actuals: 330 units sold for 3,135 (9.50 each); EUR 3,100 bought at 1.20.
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, PostingDate = new DateOnly(2026, 3, 20), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = "VD-1", Narrative = "Q1 sales",
            Lines = [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 3135m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 3135m }],
        })).IsSuccess.Should().BeTrue();
        var supplies = await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, PostingDate = new DateOnly(2026, 3, 21), TransactionCurrency = Scenario.Eur, DocumentType = "Journal", DocumentNumber = "VD-2", Narrative = "Q1 imports",
            Lines =
            [
                new JournalLineRequest { AccountCode = "6000", Side = JournalSide.Debit, TransactionAmount = 3100m, ExchangeRate = 1.20m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 3100m, ExchangeRate = 1.20m },
            ],
        });
        supplies.IsSuccess.Should().BeTrue(supplies.IsFailure ? supplies.Error.ToString() : null);

        (await budgeting.RecordActualVolumeAsync(companyId, "9999", null, new DateOnly(2026, 1, 31), 100m, "Units")).Error.Code.Should().Be("Budget.AccountNotFound");
        (await budgeting.RecordActualVolumeAsync(companyId, "4000", null, new DateOnly(2026, 1, 31), 0m, "Units")).Error.Code.Should().Be("Budget.VolumeZero");
        foreach ((int month, decimal units) in new[] { (1, 100m), (2, 110m), (3, 120m) })
        {
            (await budgeting.RecordActualVolumeAsync(companyId, "4000", null, new DateOnly(2026, month, 28), units, "Units shipped")).IsSuccess.Should().BeTrue();
        }

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "B35", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string csv = Encoding.UTF8.GetString(rendered.Value.Content);

        // Sales: +30 units at 10 = 300 volume; 330 at 0.50 under price = −165; total +135.
        csv.Should().Contain("4000,Sales,All,\"3,000.00\",\"3,135.00\",300,330,300.00,-165.00,0.00,135");

        // Imports: EUR 3,100 at 1.20 instead of 1.10 = 310 FX; EUR 100 more than planned at 1.10 = 110 price; total +420.
        csv.Should().Contain(",All,\"3,300.00\",\"3,720.00\",-,-,0.00,110.00,310.00,420");
    }
}
