using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #18 (brief §14 reports 1 and 9): the comparative trial balance and the direct-method cash flow statement.</summary>
[Collection(AccountingCollection.Name)]
public sealed class DirectCashFlowAndComparativeTbTests
{
    private readonly AccountingFixture _fixture;

    public DirectCashFlowAndComparativeTbTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Direct_cash_flow_classifies_cash_by_kind_and_the_trial_balance_compares_with_last_year()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Dictionary<string, CashFlowCategory> tags = new()
        {
            ["1000"] = CashFlowCategory.CashAndEquivalents, ["4000"] = CashFlowCategory.Operating, ["6800"] = CashFlowCategory.Operating,
            ["1600"] = CashFlowCategory.Investing, ["2470"] = CashFlowCategory.Financing,
        };
        foreach (LedgerAccount account in await db.Accounts.Where(a => a.CompanyId == companyId && tags.Keys.Contains(a.AccountCode)).ToListAsync())
        {
            account.SetCashFlowCategory(tags[account.AccountCode]);
        }

        await db.SaveChangesAsync();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Post(string document, string debit, string credit, decimal amount) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, DocumentNumber = document,
            Lines = [new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount }],
        })).IsSuccess.Should().BeTrue(document);
        await Post("CFD-SALE", "1000", "4000", 1000m);
        await Post("CFD-EXP", "6800", "1000", 100m);
        await Post("CFD-ASSET", "1600", "1000", 500m);
        await Post("CFD-LOAN", "1000", "2470", 300m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, Dictionary<string, object?> parameters)
        {
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string cfd = await Render("GL-CFD", new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) });
        cfd.Should().Contain("Operating,Cash sales and other income received,1000").And.Contain("Operating,Expenses paid in cash,-100")
            .And.Contain("Operating,Net cash from operating activities,900").And.Contain("Investing,Net cash from investing activities,-500")
            .And.Contain("Financing,Net cash from financing activities,300").And.Contain("Cash,Net change in cash,700");

        string tb = await Render("GL1", new() { ["AsAtDate"] = new DateOnly(2026, 3, 31) });
        tb.Should().Contain("1600,").And.Contain("Total");
        decimal[] vehicles = [.. tb.Split('\n').Single(l => l.StartsWith("1600,", StringComparison.Ordinal)).TrimEnd('\r').Split(',').Skip(3)
            .Select(v => decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture))];
        vehicles.Should().Equal(new[] { 500m, 0m, 0m, 0m, 500m }, "no balance a year earlier, so the change is the whole balance");
    }
}
