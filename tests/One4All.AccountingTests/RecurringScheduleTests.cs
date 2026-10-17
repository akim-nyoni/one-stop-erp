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

/// <summary>
/// Recurring journals on a schedule (brief §3.1): quarterly from a start month for a number of times, auto-reversed on
/// the first of the next month, in a foreign currency; and a journal import whose rows carry a currency and a rate.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RecurringScheduleTests
{
    private readonly AccountingFixture _fixture;

    public RecurringScheduleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Quarterly_accrual_posts_on_its_months_reverses_next_month_and_stops_after_its_count()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long templateId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            RecurringJournalLineInput[] lines = [new("6300", true, 100m), new("2100", false, 100m)];
            (await rec.CreateAsync(companyId, "QACC", "Quarterly accrual", "GL", lines, new RecurringSchedule(5))).Error.Code.Should().Be("Gl.RecurringInterval");
            (await rec.CreateAsync(companyId, "QACC", "Quarterly accrual", "GL", lines, new RecurringSchedule(3, new DateOnly(2026, 3, 1), new DateOnly(2026, 1, 1)))).Error.Code.Should().Be("Gl.RecurringDates");
            (await rec.CreateAsync(companyId, "QACC", "Quarterly accrual", "GL", lines, new RecurringSchedule(3, Currency: CurrencyCode.Parse("XXX")))).Error.Code.Should().Be("Gl.RecurringCurrency");

            // Quarterly from March 2026, twice, auto-reversed, in EUR (1.10 to USD on the table).
            var created = await rec.CreateAsync(companyId, "QACC", "Quarterly accrual", "GL", lines,
                new RecurringSchedule(3, new DateOnly(2026, 3, 1), null, 2, AutoReverse: true, Currency: Scenario.Eur), "Quarterly audit fee accrual");
            created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.ToString() : null);
            templateId = created.Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();

            // February is before the start; April is not a quarter month; March posts and reverses on 1 April.
            (await rec.PostAsync(companyId, templateId, new DateOnly(2026, 2, 28))).Error.Code.Should().Be("Gl.RecurringNotDue");
            (await rec.PostAsync(companyId, templateId, new DateOnly(2026, 4, 30))).Error.Code.Should().Be("Gl.RecurringNotDue");
            IReadOnlyList<RecurringRunLine> february = (await rec.PostAllActiveAsync(companyId, new DateOnly(2026, 2, 28))).Value;
            february.Should().Contain(l => l.Code == "QACC" && l.Note != null && l.Note.Contains("starts 2026-03"));

            IReadOnlyList<RecurringRunLine> march = (await rec.PostAllActiveAsync(companyId, new DateOnly(2026, 3, 31))).Value;
            RecurringRunLine posted = march.Single(l => l.Code == "QACC");
            posted.JournalNumber.Should().NotBeNullOrEmpty();
            posted.ReversalJournalNumber.Should().NotBeNullOrEmpty();

            // June posts the second and last time; September is refused.
            (await rec.PostAsync(companyId, templateId, new DateOnly(2026, 6, 30))).IsSuccess.Should().BeTrue();
            var third = await rec.PostAsync(companyId, templateId, new DateOnly(2026, 9, 30));
            third.Error.Code.Should().Be("Gl.RecurringNotDue");
            third.Error.Message.Should().Contain("2 of 2");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<JournalEntry> journals = await db.Journals.AsNoTracking().Include(j => j.Lines).Where(j => j.CompanyId == companyId && (j.DocumentNumber == "QACC" || j.DocumentNumber == "QACC-REV"))
                .OrderBy(j => j.PostingDate).ToListAsync();
            journals.Select(j => (j.DocumentNumber, j.PostingDate)).Should().Equal(("QACC", new DateOnly(2026, 3, 31)), ("QACC-REV", new DateOnly(2026, 4, 1)),
                ("QACC", new DateOnly(2026, 6, 30)), ("QACC-REV", new DateOnly(2026, 7, 1)));
            journals[0].Lines.Should().Contain(l => l.AccountCode == "6300" && l.TransactionDebit == 100m && l.BaseDebit == 110m); // EUR at 1.10
            journals[1].Lines.Should().Contain(l => l.AccountCode == "6300" && l.TransactionCredit == 100m && l.BaseCredit == 110m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.Rows.Should().NotContain(r => r.AccountCode == "6300" && r.Debit - r.Credit != 0m); // March's accrual reversed in April
            (await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 6, 30))).Rows.Should().Contain(r => r.AccountCode == "6300" && r.Debit == 110m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var rendered = await runner.RenderAsync(companyId, "GL22", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 9, 30) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
            string csv = Encoding.UTF8.GetString(rendered.Value.Content);
            csv.Should().Contain("QACC,").And.Contain("Not due: posted 2 of 2 times").And.Contain("quarterly from 2026-03, 2 of 2, auto-reverse, EUR");
        }
    }

    [SkippableFact]
    public async Task Imported_journals_carry_a_currency_and_a_keyed_rate()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IJournalImportService import = scope.ServiceProvider.GetRequiredService<IJournalImportService>();
            string mixed = "Reference,Date,Account,Debit,Credit,Currency,Rate\nIMP-X,2026-05-05,6300,100,,EUR,\nIMP-X,2026-05-05,1100,,100,USD,\n";
            (await import.ImportAsync(companyId, mixed, 1)).Error.Code.Should().Be("Gl.ImportParse");

            string csv = "Reference,Date,Account,Debit,Credit,Currency,Rate\n"
                + "IMP-EUR,2026-05-05,6300,100,,EUR,1.25\nIMP-EUR,2026-05-05,1100,,100,EUR,1.25\n"   // keyed rate
                + "IMP-TBL,2026-05-06,6300,50,,EUR,\nIMP-TBL,2026-05-06,1100,,50,EUR,\n"              // table rate 1.10
                + "IMP-USD,2026-05-07,6300,10,,,\nIMP-USD,2026-05-07,1100,,10,,\n";                    // base
            var imported = await import.ImportAsync(companyId, csv, 1);
            imported.IsSuccess.Should().BeTrue(imported.IsFailure ? imported.Error.ToString() : null);
            imported.Value.Select(r => r.Reference).Should().Equal("IMP-EUR", "IMP-TBL", "IMP-USD");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<JournalEntry> journals = await db.Journals.AsNoTracking().Include(j => j.Lines).Where(j => j.CompanyId == companyId && j.DocumentNumber != null && j.DocumentNumber.StartsWith("IMP-"))
                .OrderBy(j => j.PostingDate).ToListAsync();
            journals.Should().HaveCount(3);
            journals[0].Lines.Single(l => l.AccountCode == "6300").Should().BeEquivalentTo(new { TransactionDebit = 100m, BaseDebit = 125m });
            journals[1].Lines.Single(l => l.AccountCode == "6300").Should().BeEquivalentTo(new { TransactionDebit = 50m, BaseDebit = 55m });
            journals[2].Lines.Single(l => l.AccountCode == "6300").Should().BeEquivalentTo(new { TransactionDebit = 10m, BaseDebit = 10m });
        }
    }
}
