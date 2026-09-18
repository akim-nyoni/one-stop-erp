using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ReceiptImportTests
{
    private readonly AccountingFixture _fixture;

    public ReceiptImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Import_auto_matches_by_reference_and_parks_the_rest_in_suspense()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long aliceId;
        long bobId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer alice = new(companyId, "ACC1001", "Alice", Scenario.Usd);
            Customer bob = new(companyId, "ACC1002", "Bob", Scenario.Usd);
            db.Customers.AddRange(alice, bob);
            await db.SaveChangesAsync();
            aliceId = alice.Id;
            bobId = bob.Id;

            // Bill each: Alice 500, Bob 300.
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = aliceId, DocumentNumber = "IA", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = bobId, DocumentNumber = "IB", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 300m, SalesAccountCode = "4000" });
        }

        long batchId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptImportService import = scope.ServiceProvider.GetRequiredService<IReceiptImportService>();
            var result = await import.ImportAsync(new ImportRequest
            {
                CompanyId = companyId, FileReference = "STMT-2026-01.csv", FileHash = "HASH-A", Source = ImportSource.BankStatement,
                Date = new DateOnly(2026, 1, 25), BankAccountCode = "1100", SuspenseAccountCode = "2600",
                Lines =
                [
                    new ImportLineRequest("PAYMENT REF ACC1001 THANKS", "A. Payer", 500m, new DateOnly(2026, 1, 24)),   // → Alice
                    new ImportLineRequest("ACC1002", "B Payer", 200m, new DateOnly(2026, 1, 24)),                        // → Bob (partial)
                    new ImportLineRequest("NO ACCOUNT QUOTED", "Mystery", 150m, new DateOnly(2026, 1, 24)),             // → suspense
                ],
            });
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            result.Value.Matched.Should().Be(2);
            result.Value.Unmatched.Should().Be(1);
            result.Value.MatchedAmount.Should().Be(700m);
            result.Value.UnmatchedAmount.Should().Be(150m);
            batchId = result.Value.BatchId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptImportService import = scope.ServiceProvider.GetRequiredService<IReceiptImportService>();
            // Re-importing the same file (same hash) is rejected.
            var dup = await import.ImportAsync(new ImportRequest
            {
                CompanyId = companyId, FileReference = "STMT-2026-01.csv", FileHash = "HASH-A", Source = ImportSource.BankStatement,
                Date = new DateOnly(2026, 1, 25), BankAccountCode = "1100", SuspenseAccountCode = "2600",
                Lines = [new ImportLineRequest("ACC1001", "A", 1m, new DateOnly(2026, 1, 24))],
            });
            dup.IsFailure.Should().BeTrue();
            dup.Error.Code.Should().Be("Rcp.DuplicateImport");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31));
            // Alice 500 fully paid → 0; Bob 300 − 200 = 100. Total 100.
            debtors.Total.Should().Be(100m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "2600" && r.Credit == 150m); // unmatched parked in suspense

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            ImportBatch batch = await db.Set<ImportBatch>().Include(b => b.Lines).FirstAsync(b => b.Id == batchId);
            batch.Lines.Count(l => l.Status == ImportLineStatus.Matched).Should().Be(2);
            batch.Lines.Single(l => l.Status == ImportLineStatus.Unmatched).PayerName.Should().Be("Mystery");
        }
    }
}
