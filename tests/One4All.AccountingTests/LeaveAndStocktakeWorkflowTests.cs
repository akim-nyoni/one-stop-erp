using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Inventory;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers routing leave applications and stocktake postings through the approval workflow: with a workflow defined, a leave application
/// waits for it (HR cannot decide it directly) and is taken when approved; a stocktake's variances are held until approved, then posted, and a
/// rejection releases them for correction.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LeaveAndStocktakeWorkflowTests
{
    private readonly AccountingFixture _fixture;

    public LeaveAndStocktakeWorkflowTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Leave_and_stocktakes_wait_for_their_workflows()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        User staff = new($"lsw-s{tag}", "Staff"), manager = new($"lsw-m{tag}", "Manager"), clerk = new($"lsw-c{tag}", "Store clerk");
        db.Users.AddRange(staff, manager, clerk);
        Item tins = new(companyId, "LSW-TIN", "Tins") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "LSW-WH", "Store");
        db.AddRange(tins, store);
        await db.SaveChangesAsync();
        Employee employee = new(companyId, "LSW-1", "Staff", 1000m, new DateOnly(2025, 1, 1)) { UserId = staff.Id };
        db.Employees.Add(employee);
        foreach (string type in new[] { WorkflowDocumentTypes.LeaveApplication, WorkflowDocumentTypes.Stocktake })
        {
            WorkflowDefinition definition = new(companyId, type, type);
            definition.AddStage(1, "Manager").AddEligibleUser(manager.Id);
            db.WorkflowDefinitions.Add(definition);
        }

        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        async Task<long> InstanceFor(string type, string reference) => await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.DocumentType == type && i.DocumentReference == reference).Select(i => i.Id).SingleAsync();

        // Leave: the application waits for the workflow and is taken when it approves.
        (await scope.ServiceProvider.GetRequiredService<ILeaveService>().AccrueAsync(companyId, employee.Id, "ANNUAL", 10m)).IsSuccess.Should().BeTrue();
        IEmployeeSelfService ess = scope.ServiceProvider.GetRequiredService<IEmployeeSelfService>();
        long application = (await ess.ApplyForLeaveAsync(staff.Id, "ANNUAL", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4), 3m)).Value;
        (await ess.DecideLeaveAsync(companyId, application, true, manager.Id, null)).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.LeaveApplication, $"LV-{application}"), manager.Id, WorkflowActionType.Approve, "Enjoy")).Value
            .Should().Be(WorkflowStatus.Approved);
        (await db.LeaveApplications.AsNoTracking().SingleAsync(a => a.Id == application)).Status.Should().Be(SelfServiceStatus.Approved);
        (await ess.LeaveBalancesAsync(staff.Id)).Value.Should().ContainSingle(b => b.LeaveType == "ANNUAL" && b.Balance == 7m);

        // Stocktake: the variances are held for approval; a rejection releases them, an approval posts them.
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = tins.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 1), Quantity = 50m, UnitCost = 2m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        IStocktakeService stocktakes = scope.ServiceProvider.GetRequiredService<IStocktakeService>();
        long count = (await stocktakes.StartAsync(companyId, store.Id, new DateOnly(2026, 3, 31), "LSW-ST")).Value;
        (await stocktakes.RecordCountAsync(companyId, count, tins.Id, 45m)).IsSuccess.Should().BeTrue();
        (await stocktakes.PostAsync(companyId, count, new DateOnly(2026, 3, 31), "5000", postedByUserId: clerk.Id)).Value.AwaitingApproval.Should().BeTrue();
        (await stocktakes.PostAsync(companyId, count, new DateOnly(2026, 3, 31), "5000", postedByUserId: clerk.Id)).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.Stocktake, $"ST-{count}"), manager.Id, WorkflowActionType.Reject, "Recount the tins")).IsSuccess.Should().BeTrue();
        StocktakeSession released = await db.Set<StocktakeSession>().AsNoTracking().SingleAsync(s => s.Id == count);
        (released.Status, released.PendingPostingDate).Should().Be((StocktakeStatus.Draft, (DateOnly?)null));

        (await stocktakes.PostAsync(companyId, count, new DateOnly(2026, 3, 31), "5000", postedByUserId: clerk.Id)).Value.AwaitingApproval.Should().BeTrue("posting again starts a new workflow");
        long second = await db.WorkflowInstances.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentType == WorkflowDocumentTypes.Stocktake && i.Status == WorkflowStatus.InProgress)
            .Select(i => i.Id).SingleAsync();
        (await workflow.ActAsync(second, manager.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        (await db.Set<StocktakeSession>().AsNoTracking().SingleAsync(s => s.Id == count)).Status.Should().Be(StocktakeStatus.Posted);
    }
}
