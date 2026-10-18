using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// The common parameters on sales, purchasing, cash book and tax listings (brief §9): a reporting currency translated at a named rate
/// basis shown in the header, and a branch, cost centre or department narrowing the postings read.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportCommonParametersTests
{
    private readonly AccountingFixture _fixture;

    public ReportCommonParametersTests(AccountingFixture fixture) => _fixture = fixture;

    [Fact]
    public void Listings_in_the_finance_modules_declare_the_common_parameters()
    {
        foreach (string code in new[] { "AR29", "AP64", "CB68", "TX216", "GL22" })
        {
            ReportDefinition? definition = SystemReportCatalogue.All().FirstOrDefault(d => d.Code == code);
            definition.Should().NotBeNull(code);
            definition!.Parameters.Select(p => p.Name).Should().Contain(ReportCommonParameters.Names, code);
        }

        SystemReportCatalogue.All().First(d => d.Module == "Payroll").Parameters.Select(p => p.Name).Should().NotContain("RateBasis", "payroll listings keep their own parameter lists");
    }

    [SkippableFact]
    public async Task Cash_book_listing_translates_to_the_reporting_currency_and_narrows_to_a_branch()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            db.BankAccounts.Add(new Core.Banking.BankAccount(companyId, "MAINB", "Main bank", Scenario.Usd, "1100"));
            db.Branches.AddRange(new Core.System.Branch(companyId, "EAST", "East"), new Core.System.Branch(companyId, "WEST", "West"));
            await db.SaveChangesAsync();

            // Two bank receipts, one on the EAST branch, one on WEST.
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            foreach ((string branch, decimal amount, string doc) in new[] { ("EAST", 100m, "CP-E"), ("WEST", 50m, "CP-W") })
            {
                var posted = await engine.PostAsync(new JournalRequest
                {
                    CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 8, 5), TransactionCurrency = Scenario.Usd, DocumentType = "Test", DocumentNumber = doc, Narrative = $"Receipt {branch}",
                    Lines =
                    [
                        new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = amount, Branch = branch },
                        new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount, Branch = branch },
                    ],
                });
                posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);
            }
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            async Task<string> Render(Dictionary<string, object?> parameters)
            {
                parameters["FromDate"] = new DateOnly(2026, 8, 1);
                parameters["ToDate"] = new DateOnly(2026, 8, 31);
                parameters["BankCode"] = "MAINB";
                var rendered = await runner.RenderAsync(companyId, "CB68", parameters, ReportFormat.Csv);
                rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
                return Encoding.UTF8.GetString(rendered.Value.Content);
            }

            string all = await Render([]);
            all.Should().Contain("CP-E").And.Contain("CP-W").And.Contain("100.00").And.Contain("50.00");

            string east = await Render(new Dictionary<string, object?> { ["Branch"] = "east" });
            east.Should().Contain("CP-E").And.NotContain("CP-W");

            // EUR at the closing rate on the table (1.20 USD per EUR at 30 June, so 0.8333 EUR per USD): 100 becomes 83.33.
            var header = await runner.RenderAsync(companyId, "CB68", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 8, 1), ["ToDate"] = new DateOnly(2026, 8, 31), ["ReportingCurrency"] = "EUR", ["RateBasis"] = "Closing" }, ReportFormat.Html);
            string html = Encoding.UTF8.GetString(header.Value.Content);
            int at = html.IndexOf("Amounts in", StringComparison.Ordinal);
            string basisLine = at < 0 ? html[..Math.Min(html.Length, 2000)] : html[at..Math.Min(html.Length, at + 160)];
            basisLine.Should().Contain("Amounts in EUR at", basisLine);
            string euros = await Render(new Dictionary<string, object?> { ["ReportingCurrency"] = "EUR", ["RateBasis"] = "Closing" });
            euros.Should().Contain("83.33").And.NotContain("100.00");

            string keyed = await Render(new Dictionary<string, object?> { ["ReportingCurrency"] = "EUR", ["Rate"] = 2m, ["Branch"] = "WEST" });
            keyed.Should().Contain("CP-W").And.Contain("100.00").And.NotContain("CP-E");

            basisLine.Should().Contain("closing rate at 2026-08-31");
        }
    }
}
