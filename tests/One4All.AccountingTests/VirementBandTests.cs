using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers virement bands and dimensions: budget moves between cost centres and to another department's budget; an approver needs the
/// authorisation level of the amount's band, one more between budgets.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class VirementBandTests
{
    private readonly AccountingFixture _fixture;

    public VirementBandTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Virements_follow_delegated_bands_across_cost_centres_and_departments()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        User Approver(string name, OperationFlags level)
        {
            Role role = new($"Virements {name} {tag}");
            role.Grant("Budget.VirementApprove", OperationFlags.View | OperationFlags.Approve | level);
            User user = new($"vb-{name}-{tag}", name);
            db.AddRange(role, user);
            db.SaveChanges();
            user.AssignRole(role.Id);
            db.SaveChanges();
            return user;
        }

        User officer = Approver("officer", OperationFlags.AuthoriseLevel1), director = Approver("director", OperationFlags.AuthoriseLevel3);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        async Task<long> Budget(string department)
        {
            long id = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, department, [new NewBudgetLine("6000", 1, 10_000m), new NewBudgetLine("6100", 1, 1_000m)])).Value;
            (await budgeting.ActivateAsync(companyId, id)).IsSuccess.Should().BeTrue();
            return id;
        }

        long admin = await Budget("ADM"), works = await Budget("WRK");
        IBudgetControlService control = scope.ServiceProvider.GetRequiredService<IBudgetControlService>();
        (await control.SetVirementBandsAsync(companyId, [new VirementBandRule(1_000m, 3), new VirementBandRule(null, 1)])).Error.Code.Should().Be("Budget.Virement.Bands");
        (await control.SetVirementBandsAsync(companyId, [new VirementBandRule(1_000m, 1), new VirementBandRule(null, 3)])).IsSuccess.Should().BeTrue();

        // Within the officer's band, between cost centres.
        long small = (await control.RequestVirementAsync(companyId, admin, new VirementRequest("6000", "6100", 1, 500m, "Printer toner", "ADM-1", "ADM-2"), 31)).Value;
        (await control.ApproveVirementAsync(companyId, small, officer.Id)).IsSuccess.Should().BeTrue();
        List<BudgetLine> moved = await db.Set<BudgetLine>().AsNoTracking().Where(l => l.BudgetId == admin && l.VirementId == small).ToListAsync();
        moved.Select(l => (l.AccountCode, l.CostCentre, l.Amount)).Should().BeEquivalentTo([("6000", "ADM-1", -500m), ("6100", "ADM-2", 500m)]);

        // Beyond the officer's band.
        long large = (await control.RequestVirementAsync(companyId, admin, new VirementRequest("6000", "6100", 1, 5_000m, "Vehicle repairs"), 31)).Value;
        (await control.ApproveVirementAsync(companyId, large, officer.Id)).Error.Code.Should().Be("Budget.Virement.Band");
        (await control.ApproveVirementAsync(companyId, large, director.Id)).IsSuccess.Should().BeTrue();

        // To another department's budget: one level more than the band.
        (await control.RequestVirementAsync(companyId, admin, new VirementRequest("6000", "6100", 1, 100m, "Nope", ToBudgetId: 999_999), 31)).Error.Code.Should().Be("Budget.Virement.TargetBudget");
        long across = (await control.RequestVirementAsync(companyId, admin, new VirementRequest("6000", "6000", 1, 300m, "Road crew overtime", ToBudgetId: works), 31)).Value;
        (await control.ApproveVirementAsync(companyId, across, officer.Id)).Error.Code.Should().Be("Budget.Virement.Band");
        (await control.ApproveVirementAsync(companyId, across, director.Id)).IsSuccess.Should().BeTrue();
        (await db.Set<BudgetLine>().AsNoTracking().Where(l => l.BudgetId == works && l.VirementId == across).SumAsync(l => l.Amount)).Should().Be(300m);
        (await db.Set<BudgetLine>().AsNoTracking().Where(l => l.BudgetId == admin && l.VirementId == across).SumAsync(l => l.Amount)).Should().Be(-300m);
    }
}
