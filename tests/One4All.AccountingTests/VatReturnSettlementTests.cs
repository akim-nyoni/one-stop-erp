using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers posting (settling) a VAT return: the output and input control accounts are cleared for the period
/// and the net is posted to a VAT payable account; re-running a settled period is a no-op.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class VatReturnSettlementTests
{
    private readonly AccountingFixture _fixture;

    public VatReturnSettlementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Settling_clears_the_vat_control_accounts_and_posts_the_net_payable()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            db.Accounts.Add(new LedgerAccount(companyId, "2250", "VAT Payable", AccountType.Liability));
            await db.SaveChangesAsync();
        }

        // Output tax 150 (a sale) and input tax 60 (a purchase) accrue on the control accounts.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "SALE",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 1150m },
                    new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 1000m },
                    new JournalLineRequest { AccountCode = "2200", Side = JournalSide.Credit, TransactionAmount = 150m },
                ],
            });
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 12), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "PURCH",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 400m },
                    new JournalLineRequest { AccountCode = "2210", Side = JournalSide.Debit, TransactionAmount = 60m },
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 460m },
                ],
            });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IVatReturnService vat = scope.ServiceProvider.GetRequiredService<IVatReturnService>();
            var settle = await vat.SettleAsync(new SettleVatRequest
            {
                CompanyId = companyId, FromDate = new DateOnly(2026, 3, 1), ToDate = new DateOnly(2026, 3, 31),
                VatPayableAccountCode = "2250", DocumentNumber = "VAT-2026-Q1",
            });

            settle.IsSuccess.Should().BeTrue(settle.IsFailure ? settle.Error.ToString() : null);
            settle.Value.OutputTax.Should().Be(150m);
            settle.Value.InputTax.Should().Be(60m);
            settle.Value.NetPayable.Should().Be(90m);
            settle.Value.JournalNumber.Should().NotBeNull();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "2250" && r.Credit == 90m); // VAT payable to the authority
            // The VAT control accounts are cleared (net zero → absent from, or zero on, the trial balance).
            tb.Rows.Should().NotContain(r => r.AccountCode == "2200" && r.Credit > 0m);
            tb.Rows.Should().NotContain(r => r.AccountCode == "2210" && r.Debit > 0m);
        }

        // Re-running the settled period nets to zero - nothing to post.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IVatReturnService vat = scope.ServiceProvider.GetRequiredService<IVatReturnService>();
            var again = await vat.SettleAsync(new SettleVatRequest
            {
                CompanyId = companyId, FromDate = new DateOnly(2026, 3, 1), ToDate = new DateOnly(2026, 3, 31),
                VatPayableAccountCode = "2250", DocumentNumber = "VAT-2026-Q1-B",
            });
            again.Value.JournalNumber.Should().BeNull();
            again.Value.NetPayable.Should().Be(0m);
        }
    }
}
