using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers reporting hierarchies: a statutory tree started from the chart maps every account once and rolls posted amounts up by type and
/// category (credit balances shown positive where the heading says); a management tree with its own headings and ranges, its mapping
/// gaps and overlaps found, cycles refused; and the statement report laid out by a tree.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportingTreeTests
{
    private readonly AccountingFixture _fixture;

    public ReportingTreeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Trees_lay_out_the_statements_independently_of_account_codes()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 5), TransactionCurrency = Scenario.Usd, Narrative = "Tree test",
            Lines = [new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 500m }, new JournalLineRequest { AccountCode = "2410", Side = JournalSide.Credit, TransactionAmount = 500m }],
        })).IsSuccess.Should().BeTrue();

        IReportingTreeService trees = scope.ServiceProvider.GetRequiredService<IReportingTreeService>();
        (await trees.CreateTreeAsync(companyId, "stat", "Statutory statements", ReportingTreePurpose.Statutory)).IsSuccess.Should().BeTrue();
        (await trees.CreateTreeAsync(companyId, "STAT", "Again", ReportingTreePurpose.Statutory)).Error.Code.Should().Be("Tree.Duplicate");
        (await trees.SeedFromChartAsync(companyId, "STAT")).Value.Should().BeGreaterThan(5);
        (await trees.SeedFromChartAsync(companyId, "STAT")).Error.Code.Should().Be("Tree.NotEmpty");
        (await trees.CheckAsync(companyId, "STAT")).Value.IsComplete.Should().BeTrue("every account is mapped to its category");

        IReadOnlyList<ReportingTreeLine> statutory = (await trees.RollupAsync(companyId, "STAT", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).Value;
        statutory.Should().ContainSingle(l => l.Code == "EXPENSE" && l.Level == 0).Which.Amount.Should().Be(500m);
        statutory.Should().ContainSingle(l => l.Code == "LIABILITY" && l.Level == 0).Which.Amount.Should().Be(500m, "liabilities are shown credit-positive");
        statutory.Where(l => l.Level == 0).Select(l => l.Code).Should().ContainInOrder("ASSET", "LIABILITY", "EQUITY", "INCOME", "EXPENSE");
        statutory.SkipWhile(l => l.Code != "EXPENSE").Skip(1).TakeWhile(l => l.Level > 0).Sum(l => l.Amount).Should().Be(500m, "the expense categories add up to the heading");
        (await trees.RollupAsync(companyId, "STAT", new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31))).Value.Single(l => l.Code == "EXPENSE").Amount.Should().Be(0m);

        // A management tree with its own headings and ranges.
        (await trees.CreateTreeAsync(companyId, "MGMT", "Management pack", ReportingTreePurpose.Management)).IsSuccess.Should().BeTrue();
        (await trees.SaveNodeAsync(companyId, "MGMT", "OPEX", "Operating costs", null, 10, false)).IsSuccess.Should().BeTrue();
        (await trees.SaveNodeAsync(companyId, "MGMT", "PEOPLE", "People costs", "opex", 10, false)).IsSuccess.Should().BeTrue();
        (await trees.SaveNodeAsync(companyId, "MGMT", "X", "Lost", "NOWHERE", 10, false)).Error.Code.Should().Be("Tree.ParentNotFound");
        (await trees.SaveNodeAsync(companyId, "MGMT", "OPEX", "Operating costs", "PEOPLE", 10, false)).Error.Code.Should().Be("Tree.Cycle");
        (await trees.MapAsync(companyId, "MGMT", "PEOPLE", "6000", "6199")).IsSuccess.Should().BeTrue();
        (await trees.MapAsync(companyId, "MGMT", "PEOPLE", "6199", "6000")).Error.Code.Should().Be("Tree.RangeInvalid");
        (await trees.MapAsync(companyId, "MGMT", "OPEX", "6100", null)).IsSuccess.Should().BeTrue();

        ReportingTreeCheck check = (await trees.CheckAsync(companyId, "MGMT")).Value;
        check.IsComplete.Should().BeFalse();
        check.Unmapped.Should().Contain("1100").And.NotContain("6100");
        check.MappedTwice.Should().ContainSingle(m => m.Account == "6100").Which.Nodes.Should().BeEquivalentTo(["OPEX", "PEOPLE"]);

        IReadOnlyList<ReportingTreeLine> management = (await trees.RollupAsync(companyId, "MGMT", null, new DateOnly(2026, 12, 31))).Value;
        management.Select(l => (l.Code, l.Level, l.IsLeaf)).Should().Equal(("OPEX", 0, false), ("PEOPLE", 1, true));
        management.Single(l => l.Code == "PEOPLE").Amount.Should().Be(500m);
        management.Single(l => l.Code == "OPEX").Amount.Should().Be(1_000m, "the double mapping the check reports shows up as double counting");

        // The statement report lays any tree out.
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        AuditedReport report = (await scope.ServiceProvider.GetRequiredService<IAuditedReportRunner>().RunAsync(companyId, SystemReports.TreeStatementCode,
            new Dictionary<string, object?> { ["TreeCode"] = "STAT", ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) },
            ReportFormat.Layout, ReportRunPurpose.View, null)).Value;
        RenderModel model = ReportLayout.Deserialize(report.Document.Content);
        model.Body.SelectMany(b => b.Cells).Should().Contain(c => c.Text == "Expenses" && c.Bold).And.Contain(c => c.Text == "500.00");
    }
}
