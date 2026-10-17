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
/// Covers the petty-cash imprest system: establish a float, disburse an expense, then reimburse the float
/// back to its set amount (only the shortfall is drawn from the bank).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PettyCashTests
{
    private const string PettyCash = "1010"; // Cash in Safe
    private const string Bank = "1100";
    private const string Expense = "6300";   // Petty Cash Expense

    private readonly AccountingFixture _fixture;

    public PettyCashTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Imprest_float_is_established_spent_and_reimbursed()
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
            IPettyCashService petty = scope.ServiceProvider.GetRequiredService<IPettyCashService>();
            DateOnly date = new(2026, 3, 5);

            // Establish the 100 float from an empty balance.
            var establish = await petty.ReimburseToFloatAsync(companyId, date, PettyCash, Bank, 100m, "PC-INIT");
            establish.IsSuccess.Should().BeTrue(establish.IsFailure ? establish.Error.ToString() : null);
            establish.Value.ReimbursedAmount.Should().Be(100m);
            establish.Value.FloatBalanceAfter.Should().Be(100m);

            // Spend 30 on an expense.
            (await petty.DisburseAsync(companyId, 30m, new DateOnly(2026, 3, 10), Expense, PettyCash, "PC-1")).IsSuccess.Should().BeTrue();

            // Reimburse back to the 100 float - only the 30 shortfall is drawn.
            var top = await petty.ReimburseToFloatAsync(companyId, new DateOnly(2026, 3, 31), PettyCash, Bank, 100m, "PC-TOP");
            top.Value.ReimbursedAmount.Should().Be(30m);
            top.Value.FloatBalanceAfter.Should().Be(100m);

            // A second reimbursement when the float is full is a no-op.
            var again = await petty.ReimburseToFloatAsync(companyId, new DateOnly(2026, 3, 31), PettyCash, Bank, 100m, "PC-TOP2");
            again.Value.ReimbursedAmount.Should().Be(0m);
            again.Value.JournalNumber.Should().BeNull();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == PettyCash && r.Debit == 100m); // float restored
            tb.Rows.Should().Contain(r => r.AccountCode == Expense && r.Debit == 30m);    // expense recognised
        }
    }

    [SkippableFact]
    public async Task Fund_is_counted_reconciled_and_claims_input_tax()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        const string OverShort = "7500"; // Cash Shortages & Overages
        const string VatInput = "2210";

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            TaxCode vat = new(companyId, "PVAT", "VAT 15%", TaxType.Standard) { OutputAccountCode = "2200", InputAccountCode = VatInput };
            vat.AddRate(new DateOnly(2020, 1, 1), 15m);
            db.TaxCodes.Add(vat);
            await db.SaveChangesAsync();
        }

        long countId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPettyCashService petty = scope.ServiceProvider.GetRequiredService<IPettyCashService>();

            // The fund: a 200 float in the safe account, reimbursed from the bank; variances to over/short.
            (await petty.SaveFundAsync(companyId, new PettyCashFundInput("MAIN", "Head office", PettyCash, Bank, 0m))).Error.Code.Should().Be("PettyCash.Float");
            (await petty.SaveFundAsync(companyId, new PettyCashFundInput("MAIN", "Head office", "9999", Bank, 200m))).Error.Code.Should().Be("PettyCash.Account");
            var fund = await petty.SaveFundAsync(companyId, new PettyCashFundInput("MAIN", "Head office", PettyCash, Bank, 200m, OverShort));
            fund.IsSuccess.Should().BeTrue(fund.IsFailure ? fund.Error.ToString() : null);

            // Paying from an empty fund is refused; establishing the float fills it.
            (await petty.DisburseFromFundAsync(companyId, new PettyCashDisbursement("MAIN", 10m, new DateOnly(2026, 4, 1), Expense, "PC-0"))).Error.Code.Should().Be("PettyCash.Insufficient");
            (await petty.ReimburseFundAsync(companyId, "MAIN", new DateOnly(2026, 4, 1), "PC-INIT")).Value.ReimbursedAmount.Should().Be(200m);

            // 115 paid on a VAT receipt: 100 expense, 15 input tax, 115 out of the fund.
            var paid = await petty.DisburseFromFundAsync(companyId, new PettyCashDisbursement("MAIN", 115m, new DateOnly(2026, 4, 3), Expense, "PC-1", "PVAT", "Stationers", "Printer paper"));
            paid.IsSuccess.Should().BeTrue(paid.IsFailure ? paid.Error.ToString() : null);
            paid.Value.Net.Should().Be(100m);
            paid.Value.InputTax.Should().Be(15m);
            paid.Value.FundBalanceAfter.Should().Be(85m);

            // Counted 80 against a book of 85: a 5 shortage.
            var count = await petty.CountAsync(companyId, new PettyCashCountInput("MAIN", new DateOnly(2026, 4, 30), 80m, 1, "50x1, 20x1, 10x1", "Missing 5"));
            count.IsSuccess.Should().BeTrue(count.IsFailure ? count.Error.ToString() : null);
            count.Value.BookBalance.Should().Be(85m);
            count.Value.Variance.Should().Be(-5m);
            countId = count.Value.CountId;

            // Reconcile: the shortage is written off and the float restored (120 drawn: 115 spent + 5 short).
            var rec = await petty.ReconcileAsync(companyId, countId, new DateOnly(2026, 4, 30), "PC-REC", 1);
            rec.IsSuccess.Should().BeTrue(rec.IsFailure ? rec.Error.ToString() : null);
            rec.Value.Variance.Should().Be(-5m);
            rec.Value.VarianceJournalNumber.Should().NotBeNull();
            rec.Value.ReimbursedAmount.Should().Be(120m);
            rec.Value.FloatBalanceAfter.Should().Be(200m);
            (await petty.ReconcileAsync(companyId, countId, new DateOnly(2026, 4, 30), "PC-REC2", 1)).Error.Code.Should().Be("PettyCash.Reconciled");

            IReadOnlyList<PettyCashFundRow> funds = await petty.ListFundsAsync(companyId);
            funds.Should().ContainSingle(f => f.Code == "MAIN" && f.FloatAmount == 200m && f.Balance == 200m && f.LastCountDate == new DateOnly(2026, 4, 30));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            PettyCashCount stored = await db.Set<PettyCashCount>().AsNoTracking().SingleAsync(c => c.Id == countId);
            stored.Status.Should().Be(PettyCashCountStatus.Reconciled);
            stored.ReimbursementJournalNumber.Should().NotBeNull();

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == PettyCash && r.Debit == 200m);
            tb.Rows.Should().Contain(r => r.AccountCode == Expense && r.Debit == 100m);
            tb.Rows.Should().Contain(r => r.AccountCode == VatInput && r.Debit == 15m);
            tb.Rows.Should().Contain(r => r.AccountCode == OverShort && r.Debit == 5m);
            tb.Rows.Should().Contain(r => r.AccountCode == Bank && r.Credit == 320m);

            IReportDataProvider provider2 = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("CashPettyCounts"));
            var rows = await provider2.GetRowsAsync(companyId, "CashPettyCounts", new Dictionary<string, object?> { ["FundCode"] = "MAIN", ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) });
            rows.Should().HaveCount(2);
            rows[1]["Variance"].Should().Be("-5.00");
            rows[1]["Status"].Should().Be("Reconciled");
        }
    }
}
