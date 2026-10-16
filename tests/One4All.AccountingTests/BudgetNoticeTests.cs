using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Communications;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers budget-season notices: utilisation alerts are emailed once per vote and threshold (a vote crossing a higher one is alerted again); departments
/// due within the reminder window and not yet submitted have their budget holders reminded, from the preparation board.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetNoticeTests
{
    private readonly AccountingFixture _fixture;

    public BudgetNoticeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Alerts_go_once_per_threshold_and_holders_are_reminded_before_the_deadline()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetNotificationService notices = scope.ServiceProvider.GetRequiredService<IBudgetNotificationService>();
        (await notices.AlertAsync(companyId, new DateOnly(2026, 3, 20))).Error.Code.Should().Be("Budget.NoAlertEmail");
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.BudgetAlertEmail = "finance@example.com";
        company.BudgetAlertThresholds = "80, 100";
        db.DepartmentUnits.AddRange(new DepartmentUnit(companyId, "Roads", "Works", "Engineering") { BudgetHolder = "R. Holder", BudgetHolderEmail = "roads@example.com" },
            new DepartmentUnit(companyId, "Parks", "Amenities", "Community") { BudgetHolder = "P. Holder", BudgetHolderEmail = "parks@example.com" },
            new DepartmentUnit(companyId, "Clinics", "Health", "Community") { BudgetHolder = "C. Holder", BudgetHolderEmail = "clinics@example.com" });
        await db.SaveChangesAsync();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6800", 3, 1_000m)])).Value;
        (await budgeting.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Spend(decimal amount) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        })).IsSuccess.Should().BeTrue();

        await Spend(850m);
        (await notices.AlertAsync(companyId, new DateOnly(2026, 3, 20))).Value.Items.Should().Be(1, "85% crosses 80");
        (await notices.AlertAsync(companyId, new DateOnly(2026, 3, 21))).Value.Items.Should().Be(0, "already alerted at 80");
        await Spend(200m);
        (await notices.AlertAsync(companyId, new DateOnly(2026, 3, 22))).Value.Items.Should().Be(1, "105% crosses 100");
        List<OutboundMessage> alerts = await db.Set<OutboundMessage>().AsNoTracking().Where(m => m.CompanyId == companyId && m.Recipient == "finance@example.com").OrderBy(m => m.Id).ToListAsync(); // in the order sent: the database returns rows in no promised order
        alerts.Should().HaveCount(2);
        alerts.Last().Body.Should().Contain("6800").And.Contain("past 100%");


        // Roads is due in 5 days and has not started; Parks is overdue but has submitted; Clinics is due in a month.
        (await notices.SetDeadlineAsync(companyId, "roads", new DateOnly(2026, 3, 25))).IsSuccess.Should().BeTrue();
        (await notices.SetDeadlineAsync(companyId, "Parks", new DateOnly(2026, 3, 1))).IsSuccess.Should().BeTrue();
        (await notices.SetDeadlineAsync(companyId, "Clinics", new DateOnly(2026, 4, 20))).IsSuccess.Should().BeTrue();
        (await notices.SetDeadlineAsync(companyId, "Nowhere", new DateOnly(2026, 4, 20))).Error.Code.Should().Be("Budget.DepartmentNotFound");
        long parks = (await budgeting.CreateBudgetAsync(companyId, yearId, "Parks 2026", BudgetType.OriginalApproved, "Parks", [new NewBudgetLine("6800", 3, 100m)])).Value;
        (await scope.ServiceProvider.GetRequiredService<IBudgetPreparationService>().SubmitAsync(companyId, parks, 1, null)).IsSuccess.Should().BeTrue();

        IReadOnlyList<PreparationStatus> board = await notices.BoardAsync(companyId, yearId, new DateOnly(2026, 3, 20));
        board.Select(r => (r.Department, r.Status, r.DaysLeft)).Should().Equal(("Clinics", null, 31), ("Parks", "Submitted", -19), ("Roads", null, 5));
        BudgetNotices reminded = (await notices.RemindAsync(companyId, yearId, new DateOnly(2026, 3, 20), 7)).Value;
        (reminded.Messages, reminded.Items).Should().Be((1, 1));
        (await db.Set<OutboundMessage>().AsNoTracking().SingleAsync(m => m.CompanyId == companyId && m.Recipient == "roads@example.com")).Body.Should().Contain("due in 5 day(s)");

        // A department's holder hears about the votes in that department's budget, at the department's own thresholds: Roads carries 6900 with a 50% threshold.
        DepartmentUnit roads = await db.DepartmentUnits.SingleAsync(u => u.CompanyId == companyId && u.Department == "Roads");
        roads.AlertThresholds = "50";
        await db.SaveChangesAsync();
        long roadsBudget = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026-ROADS", BudgetType.OriginalApproved, "Roads", [new NewBudgetLine("6900", 3, 1_000m)])).Value;
        (await budgeting.ActivateAsync(companyId, roadsBudget)).IsSuccess.Should().BeTrue();
        (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 11), TransactionCurrency = Scenario.Usd,
            Lines = [new JournalLineRequest { AccountCode = "6900", Side = JournalSide.Debit, TransactionAmount = 600m }, new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 600m }],
        })).IsSuccess.Should().BeTrue();
        (await notices.AlertAsync(companyId, new DateOnly(2026, 3, 23))).Value.Items.Should().Be(1, "6900 at 60% crosses the Roads holder's 50%");
        List<OutboundMessage> toRoads = await db.Set<OutboundMessage>().AsNoTracking().Where(m => m.CompanyId == companyId && m.Recipient == "roads@example.com" && m.Subject.Contains("utilisation")).ToListAsync();
        toRoads.Should().ContainSingle().Which.Body.Should().Contain("6900").And.Contain("past 50%").And.NotContain("6800");
        (await db.Set<OutboundMessage>().AsNoTracking().CountAsync(m => m.CompanyId == companyId && m.Recipient == "finance@example.com")).Should().Be(2, "50% is not one of the company's thresholds");
    }
}
