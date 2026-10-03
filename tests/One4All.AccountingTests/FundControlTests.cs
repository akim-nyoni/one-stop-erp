using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers fund control: with it on, expenditure against a vote (a budget-enabled expense account) is refused until the
/// year's active budget appropriates it, whether by journal or by supplier invoice; a draft budget does not count, an
/// account that is not a vote posts freely, and a department's budget appropriates only for that department.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FundControlTests
{
    private readonly AccountingFixture _fixture;

    public FundControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Expenditure_against_a_vote_needs_an_appropriation()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).FundControlEnabled = true;
        (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "6800")).BudgetEnabled = true;
        (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "6700")).BudgetEnabled = true;
        Supplier supplier = new(companyId, "FC-SUP", "Stationers", Scenario.Usd);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        (await engine.PostAsync(Spend(companyId, "6800", null))).Error.Code.Should().Be("Posting.NoAppropriation");
        (await ap.PostInvoiceAsync(Invoice(companyId, supplier.Id))).Error.Code.Should().Be("Posting.NoAppropriation");
        (await engine.PostAsync(Spend(companyId, "6100", null))).IsSuccess.Should().BeTrue("6100 is not a vote");

        // A draft budget appropriates nothing; once active it does.
        IBudgetingService budgets = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgets.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null,
            [new NewBudgetLine("6800", 3, 1_000m), new NewBudgetLine("6700", 3, 500m, null)])).Value;
        (await engine.PostAsync(Spend(companyId, "6800", null))).Error.Code.Should().Be("Posting.NoAppropriation");
        (await budgets.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();
        (await engine.PostAsync(Spend(companyId, "6800", null))).IsSuccess.Should().BeTrue();
        (await ap.PostInvoiceAsync(Invoice(companyId, supplier.Id))).IsSuccess.Should().BeTrue();

        // A department's own budget appropriates only for that department.
        long salesBudget = (await budgets.CreateBudgetAsync(companyId, yearId, "2026 Sales", BudgetType.OriginalApproved, "SAL", [new NewBudgetLine("6200", 3, 300m)])).Value;
        (await budgets.ActivateAsync(companyId, salesBudget)).IsSuccess.Should().BeTrue();
        (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "6200")).BudgetEnabled = true;
        await db.SaveChangesAsync();
        (await engine.PostAsync(Spend(companyId, "6200", "SAL"))).IsSuccess.Should().BeTrue();
        (await engine.PostAsync(Spend(companyId, "6200", "ADM"))).Error.Code.Should().Be("Posting.NoAppropriation");
    }

    private static JournalRequest Spend(long companyId, string account, string? department) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
        Lines =
        [
            new JournalLineRequest { AccountCode = account, Department = department, Side = JournalSide.Debit, TransactionAmount = 40m },
            new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 40m },
        ],
    };

    private static PurchaseInvoiceRequest Invoice(long companyId, long supplierId) => new()
    {
        CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "FC-I1", DocumentDate = new DateOnly(2026, 3, 12), Currency = Scenario.Usd, NetAmount = 60m, ExpenseAccountCode = "6800",
    };
}
