using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Banking;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers importing bank statements as MT940 and camt.053 files (brief §4.7): both land as statement lines ready to
/// reconcile, with signs and references kept; an MT940 file with an unreadable line is refused whole.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankStatementFileImportTests
{
    private readonly AccountingFixture _fixture;

    public BankStatementFileImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Mt940_and_camt053_files_import_as_statement_lines()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        BankAccount account = new(companyId, "SWIFT-BANK", "SWIFT Current Account", Scenario.Usd, "1100");
        db.Set<BankAccount>().Add(account);
        await db.SaveChangesAsync();

        IBankReconciliationService banking = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        const string mt940 = ":20:S1\n:25:123\n:61:2603050305C250,00NTRFINV-1001//BR1\n:86:Acme Ltd\n:61:260306D80,50NCHGNONREF\n:86:Bank charges\n";
        (await banking.ImportStatementFileAsync(companyId, account.Id, mt940, StatementFileFormat.Mt940)).Value.Should().Be(2);

        const string camt = """
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02"><BkToCstmrStmt><Stmt>
              <Ntry><Amt Ccy="USD">40.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><BookgDt><Dt>2026-03-09</Dt></BookgDt><AcctSvcrRef>DD-77</AcctSvcrRef><AddtlNtryInf>Insurance debit order</AddtlNtryInf></Ntry>
            </Stmt></BkToCstmrStmt></Document>
            """;
        (await banking.ImportStatementFileAsync(companyId, account.Id, camt, StatementFileFormat.Camt053)).Value.Should().Be(1);

        (await banking.ImportStatementFileAsync(companyId, account.Id, ":61:garbage\n", StatementFileFormat.Mt940)).Error.Code.Should().Be("Cb.StatementParse");

        List<BankStatementLine> lines = await db.Set<BankStatementLine>().AsNoTracking().Where(l => l.BankAccountId == account.Id).OrderBy(l => l.StatementDate).ToListAsync();
        lines.Select(l => (l.StatementDate, l.Amount, l.Reference)).Should().Equal(
            (new DateOnly(2026, 3, 5), 250m, "INV-1001"),
            (new DateOnly(2026, 3, 6), -80.5m, (string?)null),
            (new DateOnly(2026, 3, 9), -40m, "DD-77"));
    }
}
