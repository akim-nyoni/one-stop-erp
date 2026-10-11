using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the monthly budget-holder statement: a department budgeted 100 a month on stationery that spent 950 by March shows budget and actual
/// to date with the variance, flags the vote past 75% of the year's 1,200, and is queued to the holder's address.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetHolderStatementTests
{
    private readonly AccountingFixture _fixture;

    public BudgetHolderStatementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_budget_holder_gets_the_departments_statement()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "6391", "Stationery", AccountType.Expense) { BudgetEnabled = true, AllowManualJournal = true, IsActive = true });
        db.Departments.Add(new Department(companyId, "BHS", "Budget holder statements"));
        db.DepartmentUnits.Add(new DepartmentUnit(companyId, "BHS", "Admin", "Corporate") { BudgetHolder = "Ms Banda", BudgetHolderEmail = "banda@example.test" });
        await db.SaveChangesAsync();
        FiscalYear year = await db.FiscalYears.AsNoTracking().Include(y => y.Periods).FirstAsync(y => y.CompanyId == companyId && y.StartDate <= new DateOnly(2026, 3, 15) && y.EndDate >= new DateOnly(2026, 3, 15));

        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, DocumentType = "JNL", DocumentNumber = "BHS-1", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, Narrative = "Stationery",
            Lines =
            [
                new JournalLineRequest { AccountCode = "6391", Side = JournalSide.Debit, TransactionAmount = 950m, Department = "BHS" },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 950m },
            ],
        })).IsSuccess.Should().BeTrue();
        IBudgetingService budgets = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgets.CreateBudgetAsync(companyId, year.Id, "BHS 2026", BudgetType.OriginalApproved, "BHS",
            [.. Enumerable.Range(1, 12).Select(p => new NewBudgetLine("6391", p, 100m))])).Value;
        (await budgets.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();

        IBudgetNotificationService notices = scope.ServiceProvider.GetRequiredService<IBudgetNotificationService>();
        string statement = (await notices.HolderStatementAsync(companyId, "BHS", new DateOnly(2026, 3, 31))).Value;
        statement.Should().Contain("Budget statement for BHS, period 3").And.Contain("300.00").And.Contain("950.00").And.Contain("-650.00").And.Contain("79.2%")
            .And.Contain("Past a utilisation threshold").And.Contain("past 75%");

        BudgetNotices sent = (await notices.HolderStatementsAsync(companyId, new DateOnly(2026, 3, 31))).Value;
        sent.Messages.Should().Be(1);
        (await db.Set<One4All.Core.Communications.OutboundMessage>().AsNoTracking().CountAsync(m => m.CompanyId == companyId && m.Recipient == "banda@example.test")).Should().Be(1);
    }
}
