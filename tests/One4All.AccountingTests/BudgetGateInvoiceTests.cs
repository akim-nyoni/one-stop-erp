using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Budgeting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the budget gate on a supplier invoice with no order behind it (brief §4.18.5): it is checked against what is left of the vote at posting,
/// an overrun is blocked, and spend within the budget posts and uses it up.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetGateInvoiceTests
{
    private readonly AccountingFixture _fixture;

    public BudgetGateInvoiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_invoice_without_an_order_is_checked_against_the_budget()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        Supplier supplier = new(companyId, "BGI-SUP", "Stationers", Scenario.Usd);
        db.Add(supplier);
        await db.SaveChangesAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budget = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6100", 1, 1_000m)])).Value;
        (await budgeting.ActivateAsync(companyId, budget)).IsSuccess.Should().BeTrue();
        IBudgetGateService gate = scope.ServiceProvider.GetRequiredService<IBudgetGateService>();
        (await gate.SetRuleAsync(companyId, null, "61", 0m, BudgetAction.Block)).IsSuccess.Should().BeTrue();

        IPurchaseLedgerService purchases = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        PurchaseInvoiceRequest Invoice(string number, decimal net) => new()
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd, NetAmount = net, ExpenseAccountCode = "6100",
        };

        (await purchases.PostInvoiceAsync(Invoice("BGI-1", 1_200m))).Error.Code.Should().Be("Budget.Blocked");
        (await purchases.PostInvoiceAsync(Invoice("BGI-2", 300m))).IsSuccess.Should().BeTrue();
        (await gate.CheckAsync(companyId, new DateOnly(2026, 4, 2), [new BudgetDemand("6100", null, 1m)])).Value.Lines.Single().Available.Should().Be(700m);
        (await purchases.PostInvoiceAsync(Invoice("BGI-3", 800m))).Error.Code.Should().Be("Budget.Blocked");
        (await purchases.PostInvoiceAsync(Invoice("BGI-4", 700m))).IsSuccess.Should().BeTrue();
    }

    [SkippableFact]
    public async Task An_expense_claim_is_checked_against_the_budget_when_approved()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        Core.Payroll.Employee claimant = new(companyId, "BGI-E1", "Travelling Clerk", 1_000m, new DateOnly(2025, 1, 1));
        db.Add(claimant);
        await db.SaveChangesAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budget = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6100", 1, 100m)])).Value;
        (await budgeting.ActivateAsync(companyId, budget)).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IBudgetGateService>().SetRuleAsync(companyId, null, "61", 0m, BudgetAction.Block)).IsSuccess.Should().BeTrue();

        Application.Abstractions.Expenses.IExpenseClaimService claims = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Expenses.IExpenseClaimService>();
        long big = (await claims.CreateAsync(companyId, claimant.Id, "BGI-C1", new DateOnly(2026, 4, 1), [new("Hotel", "6100", 150m)])).Value;
        (await claims.ApproveAsync(companyId, big, 5)).Error.Code.Should().Be("Budget.Blocked");
        long small = (await claims.CreateAsync(companyId, claimant.Id, "BGI-C2", new DateOnly(2026, 4, 1), [new("Taxi", "6100", 80m)])).Value;
        (await claims.ApproveAsync(companyId, small, 5)).IsSuccess.Should().BeTrue();
    }
}
