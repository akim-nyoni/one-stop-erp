using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Inventory;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers approval rules by document attributes (brief §4.3 "rules by value band, currency, department, cost centre, item category, project, requester"):
/// a requisition carries its currency, supplier, main item category and requester to the workflow, and only the stages whose conditions hold apply.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkflowAttributeRoutingTests
{
    private readonly AccountingFixture _fixture;

    public WorkflowAttributeRoutingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stage_conditions_see_the_documents_attributes()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        User requester = new($"wfa-req-{companyId}", "Stores Clerk");
        Supplier supplier = new(companyId, "WFA-SUP", "Tool Supplier", Scenario.Usd);
        Item spanner = new(companyId, "WFA-SPAN", "Spanner") { Category = "TOOLS", RevenueAccountCode = "4000" };
        Item bread = new(companyId, "WFA-BREAD", "Bread") { Category = "FOOD", RevenueAccountCode = "4000" };
        db.AddRange(requester, supplier, spanner, bread);
        WorkflowDefinition definition = new(companyId, WorkflowDocumentTypes.PurchaseRequisition, "Requisitions");
        definition.AddStage(1, "Stores manager").Condition = "Category = TOOLS";
        definition.AddStage(2, "Foreign currency desk").Condition = "Currency != USD";
        definition.AddStage(3, "Finance").Condition = "Requester = nobody";
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        IPurchaseRequisitionService requisitions = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionService>();
        long tools = (await requisitions.CreateAsync(companyId, supplier.Id, "WFA-1", new DateOnly(2026, 3, 1), Scenario.Usd, [new PurchaseRequisitionLineRequest(spanner.Id, "Spanner", 4m, 10m)])).Value;
        (await requisitions.SubmitAsync(companyId, tools, requester.Id)).IsSuccess.Should().BeTrue();

        WorkflowInstance instance = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentReference == tools.ToString());
        Dictionary<string, string> attributes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(instance.AttributesJson!)!;
        attributes.Should().Contain(new Dictionary<string, string> { ["Currency"] = "USD", ["Supplier"] = "WFA-SUP", ["Category"] = "TOOLS", ["Requester"] = requester.Username });
        (instance.Status, instance.CurrentStageSequence).Should().Be((WorkflowStatus.InProgress, 1), "only the stores manager's rule holds for tools in dollars");

        long food = (await requisitions.CreateAsync(companyId, supplier.Id, "WFA-2", new DateOnly(2026, 3, 1), Scenario.Usd, [new PurchaseRequisitionLineRequest(bread.Id, "Bread", 4m, 1m)])).Value;
        (await requisitions.SubmitAsync(companyId, food, requester.Id)).IsSuccess.Should().BeTrue();
        WorkflowInstance second = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentReference == food.ToString());
        second.CurrentStageSequence.Should().NotBe(1, "the stores manager's rule is for tools only");
    }
}
