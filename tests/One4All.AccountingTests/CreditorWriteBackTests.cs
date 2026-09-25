using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class CreditorWriteBackTests
{
    private readonly AccountingFixture _fixture;

    public CreditorWriteBackTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Writing_back_a_stale_creditor_clears_the_balance_to_income()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long invoiceItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "WB-SUP", "Stale Creditor", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            invoiceItemId = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "WB-P1", DocumentDate = new DateOnly(2026, 2, 1),
                Currency = Scenario.Usd, NetAmount = 150m, ExpenseAccountCode = "5000",
            })).Value.OpenItemId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();

            // Reason and account are mandatory.
            (await ap.WriteBackAsync(companyId, invoiceItemId, new DateOnly(2026, 4, 1), "4400", "  ")).IsFailure.Should().BeTrue();
            (await ap.WriteBackAsync(companyId, invoiceItemId, new DateOnly(2026, 4, 1), "", "stale")).IsFailure.Should().BeTrue();

            var written = await ap.WriteBackAsync(companyId, invoiceItemId, new DateOnly(2026, 4, 1), "4400", "Supplier ceased trading");
            written.IsSuccess.Should().BeTrue(written.IsFailure ? written.Error.ToString() : null);

            // Nothing left to write back.
            (await ap.WriteBackAsync(companyId, invoiceItemId, new DateOnly(2026, 4, 2), "4400", "again")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            OpenItem invoice = await db.OpenItems.FirstAsync(i => i.Id == invoiceItemId);
            invoice.OutstandingTransaction.Should().Be(0m); // settled by the write-back allocation

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            // The released liability lands in sundry income (4400 Cr 150); the AP control nets to zero.
            tb.Rows.Should().Contain(r => r.AccountCode == "4400" && r.Credit == 150m);

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport creditors = await reports.AgedCreditorsAsync(companyId, new DateOnly(2026, 4, 30));
            creditors.Rows.Should().NotContain(r => r.TraderCode == "WB-SUP");
        }
    }
}
