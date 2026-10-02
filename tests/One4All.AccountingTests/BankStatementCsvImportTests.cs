using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Banking;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers CSV statement import: a clean file lands as statement lines, a file with a bad line is refused whole.</summary>
[Collection(AccountingCollection.Name)]
public sealed class BankStatementCsvImportTests
{
    private readonly AccountingFixture _fixture;

    public BankStatementCsvImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Clean_csv_imports_and_a_bad_line_rejects_the_whole_file()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long bankAccountId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount account = new(companyId, "CSV-BANK", "CSV Current Account", Scenario.Usd, "1100");
            db.Set<BankAccount>().Add(account);
            await db.SaveChangesAsync();
            bankAccountId = account.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankReconciliationService banking = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();

            const string good = "Date,Description,Amount,Reference\n2026-03-01,Customer receipt,\"1,250.00\",RCT-1\n2026-03-02,Bank charges,-15.00,\n";
            var imported = await banking.ImportStatementCsvAsync(companyId, bankAccountId, good, BankCsvLayout.Simple);
            imported.IsSuccess.Should().BeTrue(imported.IsFailure ? imported.Error.ToString() : null);
            imported.Value.Should().Be(2);

            const string bad = "Date,Description,Amount,Reference\n2026-03-03,Fine,10.00,\n2026-03-04,Broken,abc,\n";
            var refused = await banking.ImportStatementCsvAsync(companyId, bankAccountId, bad, BankCsvLayout.Simple);
            refused.Error.Code.Should().Be("Cb.StatementParse");
            refused.Error.Message.Should().Contain("line 3");

            (await banking.ImportStatementCsvAsync(companyId, bankAccountId, "   ", BankCsvLayout.Simple)).Error.Code.Should().Be("Cb.EmptyStatement");
            (await banking.ImportStatementCsvAsync(companyId, bankAccountId, "Date,Description,Amount,Reference\n", BankCsvLayout.Simple)).Error.Code.Should().Be("Cb.NoStatementLines");
            (await banking.ImportStatementCsvAsync(companyId, 999_999, good, BankCsvLayout.Simple)).Error.Code.Should().Be("Cb.BankNotFound");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<BankStatementLine> lines = await db.Set<BankStatementLine>().Where(l => l.BankAccountId == bankAccountId).OrderBy(l => l.StatementDate).ToListAsync();
            lines.Should().HaveCount(2); // the refused file contributed nothing
            lines[0].Amount.Should().Be(1250.00m);
            lines[0].Reference.Should().Be("RCT-1");
            lines[1].Amount.Should().Be(-15.00m);
        }
    }
}
