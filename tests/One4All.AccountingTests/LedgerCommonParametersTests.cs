using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #21: general ledger listings take the common parameters - a cost centre narrows the postings, a reporting currency translates base amounts only.</summary>
[Collection(AccountingCollection.Name)]
public sealed class LedgerCommonParametersTests
{
    private readonly AccountingFixture _fixture;

    public LedgerCommonParametersTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_ledger_listing_narrows_by_cost_centre_and_translates_base_amounts()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.CostCentres.Add(new CostCentre(companyId, "HQ", "Head Office"));
        await db.SaveChangesAsync();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Spend(string number, decimal amount, string? costCentre) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, DocumentNumber = number,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = amount, CostCentre = costCentre },
                new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = amount, CostCentre = costCentre },
            ],
        })).IsSuccess.Should().BeTrue();
        await Spend("LCP-1", 100m, "HQ");
        await Spend("LCP-2", 40m, null);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Gl2(Dictionary<string, object?> extra)
        {
            Dictionary<string, object?> parameters = new(extra) { ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, "GL2", parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string all = await Gl2(new Dictionary<string, object?>());
        all.Should().Contain("6800,").And.Contain("140.00,140");
        string headOffice = await Gl2(new() { ["CostCentre"] = "hq" });
        headOffice.Should().Contain("100.00,100").And.NotContain("140");

        // Keyed at 0.5: the base balance halves; the transaction balance, in its own currency, does not.
        string translated = await Gl2(new() { ["ReportingCurrency"] = "EUR", ["RateBasis"] = "Keyed", ["Rate"] = 0.5m });
        translated.Should().Contain("140.00,70");
    }
}
