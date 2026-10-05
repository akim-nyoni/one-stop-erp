using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the SYS-numbered control and system reports (brief §14 P, R): a journal entered today but dated in January is
/// flagged back-dated; a new customer appears among master-data changes; the company settings and number sequences list;
/// every SYS report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ControlReportsTests
{
    private readonly AccountingFixture _fixture;

    public ControlReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Control_reports_flag_back_dating_and_list_the_system()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Customers.Add(new Customer(companyId, "SYS-CUS", "Audited Customer", Scenario.Usd));
        await db.SaveChangesAsync();
        PostingReceipt backdated = (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 1, 5), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = "SYS-BD-1",
            Narrative = "Late correction",
            Lines = [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 50m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 50m }],
        })).Value;

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = today.AddDays(-1), ["ToDate"] = today.AddDays(1), ["Days"] = 7 };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("SYS242")).Should().Contain($"{backdated.JournalNumber},2026-01-05,{today:yyyy-MM-dd}").And.Contain("SYS-BD-1").And.Contain("Late correction");
        (await Render("SYS240")).Should().Contain(",Customer,").And.Contain("Insert");
        (await Render("SYS260")).Should().Contain("JNL");
        (await Render("SYS261")).Should().Contain("BaseCurrency,USD");
        (await Render("SYS262")).Should().Contain("SYS262,Report Definition Inventory");

        foreach (string code in ControlReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
