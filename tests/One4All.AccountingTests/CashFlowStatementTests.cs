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
/// Covers the Statement of Cash Flows (movement method): movements on tagged accounts are classified into
/// operating, investing and financing activities, and - because every journal balances - their sum equals
/// the change in cash.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CashFlowStatementTests
{
    private readonly AccountingFixture _fixture;

    public CashFlowStatementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Classified_movements_reconcile_to_the_change_in_cash()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            // Tag the accounts we will touch.
            var tags = new Dictionary<string, CashFlowCategory>
            {
                ["1000"] = CashFlowCategory.CashAndEquivalents, // Cash on Hand
                ["4000"] = CashFlowCategory.Operating,          // Sales
                ["6800"] = CashFlowCategory.Operating,          // Office Supplies
                ["1600"] = CashFlowCategory.Investing,          // Motor Vehicles at Cost
                ["2470"] = CashFlowCategory.Financing,          // Accruals (stand-in for a loan)
            };
            List<LedgerAccount> accounts = await db.Accounts.Where(a => a.CompanyId == companyId && tags.Keys.Contains(a.AccountCode)).ToListAsync();
            foreach (LedgerAccount account in accounts)
            {
                account.SetCashFlowCategory(tags[account.AccountCode]);
            }

            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            DateOnly date = new(2026, 3, 10);

            await Post(engine, companyId, date, "CF-SALE", ("1000", JournalSide.Debit, 1000m), ("4000", JournalSide.Credit, 1000m));
            await Post(engine, companyId, date, "CF-EXP", ("6800", JournalSide.Debit, 100m), ("1000", JournalSide.Credit, 100m));
            await Post(engine, companyId, date, "CF-ASSET", ("1600", JournalSide.Debit, 500m), ("1000", JournalSide.Credit, 500m));
            await Post(engine, companyId, date, "CF-LOAN", ("1000", JournalSide.Debit, 300m), ("2470", JournalSide.Credit, 300m));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            CashFlowStatement cf = await reports.CashFlowStatementAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

            cf.Operating.Should().Be(900m);    // +1000 sale − 100 expense
            cf.Investing.Should().Be(-500m);   // asset purchase
            cf.Financing.Should().Be(300m);    // loan drawdown
            cf.Unclassified.Should().Be(0m);
            cf.OpeningCash.Should().Be(0m);
            cf.ClosingCash.Should().Be(700m);  // 1000 − 100 − 500 + 300
            cf.NetChange.Should().Be(700m);
            cf.ClassifiedChange.Should().Be(cf.NetChange);

            // The indirect method: profit, then each account's movement, adding up to each activity's total.
            cf.Lines.Should().ContainEquivalentOf(new CashFlowLine("Operating", string.Empty, "Profit for the period", 900m));
            cf.Lines.Should().ContainEquivalentOf(new CashFlowLine("Investing", "1600", "Increase in Motor Vehicles at Cost", -500m));
            cf.Lines.Should().ContainEquivalentOf(new CashFlowLine("Financing", "2470", "Increase in Accruals", 300m));
            cf.Lines.Where(l => l.Section == "Operating").Sum(l => l.Amount).Should().Be(cf.Operating);
        }
    }

    private static async Task Post(IPostingEngine engine, long companyId, DateOnly date, string doc, params (string Account, JournalSide Side, decimal Amount)[] lines)
    {
        var result = await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = Scenario.Usd,
            EnforceManualJournalRules = false,
            DocumentType = "Journal",
            DocumentNumber = doc,
            Narrative = doc,
            Lines = lines.Select(l => new JournalLineRequest { AccountCode = l.Account, Side = l.Side, TransactionAmount = l.Amount }).ToList(),
        });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
    }
}
