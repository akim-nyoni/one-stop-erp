using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Budgeting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers requisition details: a requisition for the OPS department is checked against OPS's own budget (200), not the company's (10,000),
/// so 300 is blocked while the same 300 without a department passes; an OPS requisition within budget keeps its required-by date and
/// justification, and its purchase order carries the date, department and cost centre and commits OPS's budget.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RequisitionDetailsTests
{
    private readonly AccountingFixture _fixture;

    public RequisitionDetailsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Department_requisitions_are_checked_against_the_department_budget_and_carry_to_the_order()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, "OPS");
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        Supplier supplier = new(companyId, "RD-SUP", "Stationers", Scenario.Usd);
        Item paper = new(companyId, "RD-PAPER", "Paper");
        db.AddRange(supplier, paper);
        await db.SaveChangesAsync();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long company = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6000", 1, 10_000m)])).Value;
        long ops = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026 OPS", BudgetType.OriginalApproved, "OPS", [new NewBudgetLine("6000", 1, 200m)])).Value;
        (await budgeting.ActivateAsync(companyId, company)).IsSuccess.Should().BeTrue();
        (await budgeting.ActivateAsync(companyId, ops)).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IBudgetGateService>().SetRuleAsync(companyId, null, "6", 0m, BudgetAction.Block)).IsSuccess.Should().BeTrue();

        IPurchaseRequisitionService requisitions = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionService>();
        DateOnly date = new(2026, 3, 1);
        async Task<long> Raise(string number, decimal amount, RequisitionDetails? details) => (await requisitions.CreateAsync(companyId, supplier.Id, number, date, Scenario.Usd,
            [new PurchaseRequisitionLineRequest(paper.Id, "Paper", 1m, amount, "6000")], details)).Value;

        (await requisitions.CreateAsync(companyId, supplier.Id, "RD-BAD", date, Scenario.Usd, [new PurchaseRequisitionLineRequest(paper.Id, "Paper", 1m, 1m)],
            new RequisitionDetails(RequiredBy: new DateOnly(2026, 2, 1)))).Error.Code.Should().Be("Pop.ReqRequiredBy");
        (await requisitions.SubmitAsync(companyId, await Raise("RD-1", 300m, new RequisitionDetails(Department: "ops")))).Error.Code.Should().Be("Budget.Blocked");
        (await requisitions.SubmitAsync(companyId, await Raise("RD-2", 300m, null))).IsSuccess.Should().BeTrue();

        long fits = await Raise("RD-3", 150m, new RequisitionDetails(new DateOnly(2026, 3, 20), "Exam papers", "OPS", "OPS"));
        (await requisitions.SubmitAsync(companyId, fits)).IsSuccess.Should().BeTrue();
        (await requisitions.ApproveAsync(companyId, fits, 77)).IsSuccess.Should().BeTrue();
        db.Set<PurchaseRequisition>().AsNoTracking().Single(r => r.Id == fits).Should().Match<PurchaseRequisition>(r => r.Justification == "Exam papers" && r.Department == "OPS");
        long orderId = (await requisitions.ConvertToPurchaseOrderAsync(companyId, fits, "RD-PO1", new DateOnly(2026, 3, 2))).Value;

        PurchaseOrder order = await db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.Should().Match<PurchaseOrder>(o => o.RequiredBy == new DateOnly(2026, 3, 20) && o.Department == "OPS" && o.CostCentre == "OPS");
        (await db.Set<BudgetCommitment>().AsNoTracking().SingleAsync(c => c.CompanyId == companyId && c.Reference == "RD-PO1")).Department.Should().Be("OPS");
    }
}
