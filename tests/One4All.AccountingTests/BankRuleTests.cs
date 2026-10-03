using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bank rules: creation validation, a run that posts and matches only the lines its rules recognise
/// (leaving the rest open), a re-run that does nothing, and a deactivated rule that no longer applies.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankRuleTests
{
    private readonly AccountingFixture _fixture;

    public BankRuleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rules_post_and_match_recognised_statement_lines()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long bankAccountId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount bank = new(companyId, "BR-BANK", "Current", Scenario.Usd, "1100");
            db.Set<BankAccount>().Add(bank);
            await db.SaveChangesAsync();
            bankAccountId = bank.Id;

            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            (await recon.ImportStatementLinesAsync(companyId, bankAccountId,
            [
                new StatementLineImport(new DateOnly(2026, 3, 31), "MONTHLY SERVICE FEE", -12.50m, null),
                new StatementLineImport(new DateOnly(2026, 3, 31), "Credit interest", 3.10m, null),
                new StatementLineImport(new DateOnly(2026, 3, 20), "Customer EFT", 800m, "X1"),  // no rule: stays open
                new StatementLineImport(new DateOnly(2026, 4, 30), "MONTHLY SERVICE FEE", -12.50m, null), // after the run date
            ])).IsSuccess.Should().BeTrue();
        }

        long feeRuleId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankRuleService rules = scope.ServiceProvider.GetRequiredService<IBankRuleService>();

            (await rules.CreateRuleAsync(Request(companyId, "fee", "6800") with { DescriptionContains = " " })).Error.Code.Should().Be("BankRule.Phrase");
            (await rules.CreateRuleAsync(Request(companyId, "fee", "9999"))).Error.Code.Should().Be("BankRule.AccountNotFound");
            (await rules.CreateRuleAsync(Request(companyId, "fee", "1200"))).Error.Code.Should().Be("BankRule.ControlAccount");
            (await rules.CreateRuleAsync(Request(companyId, "fee", "6800") with { MinAmount = 50m, MaxAmount = 10m })).Error.Code.Should().Be("BankRule.Range");

            var fee = await rules.CreateRuleAsync(Request(companyId, "service fee", "6800") with { Name = "Bank charges", Direction = BankRuleDirection.MoneyOut, Narrative = "Bank charges" });
            fee.IsSuccess.Should().BeTrue(fee.IsFailure ? fee.Error.ToString() : null);
            feeRuleId = fee.Value;
            (await rules.CreateRuleAsync(Request(companyId, "interest", "4800") with { Name = "Interest received", Direction = BankRuleDirection.MoneyIn, BankAccountId = bankAccountId })).IsSuccess.Should().BeTrue();

            (await rules.ListAsync(companyId)).Should().HaveCount(2);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankRuleService rules = scope.ServiceProvider.GetRequiredService<IBankRuleService>();
            var run = await rules.ApplyAsync(companyId, bankAccountId, new DateOnly(2026, 3, 31));
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.Posted.Should().HaveCount(2);
            run.Value.Posted.Select(p => p.RuleName).Should().BeEquivalentTo(["Bank charges", "Interest received"]);
            run.Value.Failures.Should().BeEmpty();

            // Nothing left for the rules to do as at March.
            (await rules.ApplyAsync(companyId, bankAccountId, new DateOnly(2026, 3, 31))).Value.Posted.Should().BeEmpty();

            // Deactivated, the fee rule no longer applies to April's fee.
            (await rules.SetActiveAsync(companyId, feeRuleId, false)).IsSuccess.Should().BeTrue();
            (await rules.ApplyAsync(companyId, bankAccountId, new DateOnly(2026, 4, 30))).Value.Posted.Should().BeEmpty();
            (await rules.ApplyAsync(companyId, 999_999, new DateOnly(2026, 4, 30))).Error.Code.Should().Be("Cb.BankNotFound");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            BankWorkList list = (await recon.WorkListAsync(companyId, bankAccountId, new DateOnly(2026, 4, 30))).Value;
            list.StatementLines.Where(s => s.MatchedJournalLineId is null).Select(s => s.Description)
                .Should().BeEquivalentTo(["Customer EFT", "MONTHLY SERVICE FEE"]); // the EFT (no rule) and April's fee (rule off)

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 4, 30))).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "6800").Debit.Should().Be(12.50m);
            tb.Rows.Single(r => r.AccountCode == "4800").Credit.Should().Be(3.10m);
        }
    }

    private static CreateBankRuleRequest Request(long companyId, string phrase, string account) => new()
    {
        CompanyId = companyId, Name = phrase, DescriptionContains = phrase, AccountCode = account,
    };
}
