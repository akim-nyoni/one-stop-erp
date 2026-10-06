using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Security;
using One4All.Core.Budgeting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the budget gate on requisitions: spend that fits passes and commits on approval; an overrun is warned, blocked or needs an
/// approver who may override, by the most specific rule; a requisition's commitment moves to its order; reservations earmark budget.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetGateTests
{
    private readonly AccountingFixture _fixture;

    public BudgetGateTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Requisitions_are_held_to_the_available_budget()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        Supplier supplier = new(companyId, "BG-SUP", "Stationers", Scenario.Usd);
        Item paper = new(companyId, "BG-PAPER", "Paper");
        db.AddRange(supplier, paper);
        await db.SaveChangesAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budget = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6000", 1, 1_000m)])).Value;
        (await budgeting.ActivateAsync(companyId, budget)).IsSuccess.Should().BeTrue();

        IBudgetGateService gate = scope.ServiceProvider.GetRequiredService<IBudgetGateService>();
        (await gate.SetRuleAsync(companyId, null, "6", 0m, BudgetAction.Warn)).IsSuccess.Should().BeTrue();
        (await gate.SetRuleAsync(companyId, null, "6", 500m, BudgetAction.Block)).IsSuccess.Should().BeTrue();

        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        long clerk = (await admin.CreateUserAsync($"bg.clerk.{tag}", "Clerk", null, "Budget2026a")).Value;
        long controller = (await admin.CreateUserAsync($"bg.ctl.{tag}", "Controller", null, "Budget2026b")).Value;
        long overrideRole = (await admin.CreateRoleAsync($"Budget override {tag}", null)).Value;
        db.Roles.Single(r => r.Id == overrideRole).Grant("Budget.OverrideBlock", OperationFlags.View);
        await db.SaveChangesAsync();
        (await admin.AssignRoleAsync(controller, overrideRole)).IsSuccess.Should().BeTrue();

        IPurchaseRequisitionService requisitions = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionService>();
        async Task<long> Raise(string number, decimal amount) => (await requisitions.CreateAsync(companyId, supplier.Id, number, new DateOnly(2026, 3, 1), Scenario.Usd,
            [new PurchaseRequisitionLineRequest(paper.Id, "Paper", 1m, amount, "6000")])).Value;
        PurchaseRequisition Load(long id) => db.Set<PurchaseRequisition>().AsNoTracking().Single(r => r.Id == id);

        // 300 fits; approving it commits 300, leaving 700.
        long fits = await Raise("BG-R1", 300m);
        (await requisitions.SubmitAsync(companyId, fits)).IsSuccess.Should().BeTrue();
        (await requisitions.ApproveAsync(companyId, fits, clerk)).IsSuccess.Should().BeTrue();
        (await gate.CheckAsync(companyId, new DateOnly(2026, 3, 1), [new BudgetDemand("6000", null, 1m)])).Value.Lines.Single().Available.Should().Be(700m);

        // 800 overruns by 100 and is in the 500+ band: blocked; once that band needs approval, only the controller can approve it.
        long big = await Raise("BG-R2", 800m);
        (await requisitions.SubmitAsync(companyId, big)).Error.Code.Should().Be("Budget.Blocked");
        (await gate.SetRuleAsync(companyId, null, "6", 500m, BudgetAction.RequireApproval)).IsSuccess.Should().BeTrue();
        (await requisitions.SubmitAsync(companyId, big)).IsSuccess.Should().BeTrue();
        Load(big).BudgetOutcome.Should().Be(BudgetAction.RequireApproval);
        (await requisitions.ApproveAsync(companyId, big, clerk)).Error.Code.Should().Be("Budget.ApprovalNeeded");
        (await requisitions.ApproveAsync(companyId, big, controller)).IsSuccess.Should().BeTrue();

        // A small overrun is only warned.
        long small = await Raise("BG-R3", 200m);
        (await requisitions.SubmitAsync(companyId, small)).IsSuccess.Should().BeTrue();
        Load(small).BudgetOutcome.Should().Be(BudgetAction.Warn);

        // The first requisition's commitment moves to its order.
        (await requisitions.ConvertToPurchaseOrderAsync(companyId, fits, "BG-PO1", new DateOnly(2026, 3, 5))).IsSuccess.Should().BeTrue();
        List<BudgetCommitment> commitments = await db.Set<BudgetCommitment>().AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync();
        commitments.Should().Contain(c => c.Reference == "BG-R1" && c.Status == CommitmentStatus.Released);
        commitments.Should().Contain(c => c.Reference == "BG-PO1" && c.Status == CommitmentStatus.Open && c.Amount == 300m);

        // Reservations: a small one is warned and kept; with every overrun blocked, the next is refused; releasing frees it.
        (await gate.ReserveAsync(companyId, "BG-RES1", new DateOnly(2026, 3, 9), new BudgetDemand("6000", null, 50m))).Value.Outcome.Should().Be(BudgetAction.Warn);
        (await gate.ReserveAsync(companyId, "BG-RES1", new DateOnly(2026, 3, 9), new BudgetDemand("6000", null, 50m))).Error.Code.Should().Be("Budget.ReservationExists");
        (await gate.SetRuleAsync(companyId, null, "6", 0m, BudgetAction.Block)).IsSuccess.Should().BeTrue();
        (await gate.ReserveAsync(companyId, "BG-RES2", new DateOnly(2026, 3, 9), new BudgetDemand("6000", null, 10m))).Error.Code.Should().Be("Budget.Blocked");
        (await gate.ReleaseAsync(companyId, "BG-RES1")).Should().Be(50m);
    }
}
