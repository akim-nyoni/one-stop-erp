using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the partitioned line tables (brief §2): journal, sale and payslip lines live in tables partitioned by year, each line stamped with its
/// header's company and date so a posting lands in that year's partition, and a posted journal still reads back whole.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LinePartitionTests
{
    private readonly AccountingFixture _fixture;

    public LinePartitionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Journal_lines_land_in_the_posting_years_partition()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        foreach (string table in new[] { "gl.journal_line", "pos.sale_line", "pay.payslip_line" })
        {
            (await db.Database.SqlQuery<string>($"SELECT relkind::text AS \"Value\" FROM pg_class WHERE oid = {table}::regclass").SingleAsync()).Should().Be("p", $"{table} is partitioned");
        }

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        DateOnly date = new(2026, 4, 15);
        Result<PostingReceipt> posted = await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentType = "Test", DocumentNumber = "PART-1",
            Lines =
            [
                new JournalLineRequest { AccountCode = "1200", Side = JournalSide.Debit, TransactionAmount = 5m },
                new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 5m },
            ],
        });
        posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.Message : null);

        List<string> partitions = await db.Database.SqlQuery<string>($"SELECT tableoid::regclass::text AS \"Value\" FROM gl.journal_line WHERE journal_entry_id = {posted.Value.JournalId}").ToListAsync();
        partitions.Should().HaveCount(2).And.OnlyContain(p => p == "gl.journal_line_y2026");
        Core.Accounting.JournalEntry journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.Id == posted.Value.JournalId);
        journal.Lines.Should().OnlyContain(l => l.CompanyId == companyId && l.PostingDate == date);

        // Only the years in use and two ahead exist; the partitions task makes later years, and a row of a year without one goes to the default partition.
        int thisYear = DateTime.UtcNow.Year;
        (await db.Database.SqlQuery<string>($"SELECT to_regclass({"gl.journal_line_y" + (thisYear + 2)})::text AS \"Value\"").SingleAsync()).Should().NotBeNull();
        (await db.Database.SqlQuery<string>($"SELECT to_regclass({"gl.journal_line_y" + (thisYear + 5)})::text AS \"Value\"").SingleAsync()).Should().BeNull();
        Result<string> kept = await new LinePartitionsTask(db).RunAsync(companyId, new DateTimeOffset(thisYear + 4, 6, 1, 0, 0, 0, TimeSpan.Zero));
        kept.IsSuccess.Should().BeTrue();
        foreach (string table in new[] { "gl.journal_line", "pos.sale_line", "pay.payslip_line" })
        {
            (await db.Database.SqlQuery<string>($"SELECT to_regclass({table + "_y" + (thisYear + 5)})::text AS \"Value\"").SingleAsync()).Should().NotBeNull($"{table} gets the year the task asked for");
        }
    }
}
