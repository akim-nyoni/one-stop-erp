using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers back-office discount control: with a 10% limit and a price list at 100, an order at 95 passes; at 80 it is refused without authority,
/// refused when the raiser authorises it, accepted with a manager who may override discounts; with a SalesDiscount workflow it is held (no
/// despatch) until approved.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesDiscountControlTests
{
    private readonly AccountingFixture _fixture;

    public SalesDiscountControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Discounts_beyond_the_limit_need_authority_or_approval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        long clerk = (await admin.CreateUserAsync($"sdc.{tag}", "Sales clerk", null, "Discount2026a")).Value;
        long manager = (await admin.CreateUserAsync($"sdm.{tag}", "Sales manager", null, "Discount2026b")).Value;
        long role = (await admin.CreateRoleAsync($"Discounts {tag}", null)).Value;
        db.Roles.Single(r => r.Id == role).Grant("Sales.Invoice", OperationFlags.OverrideDiscount);
        await db.SaveChangesAsync();
        (await admin.AssignRoleAsync(manager, role, 900_001)).IsSuccess.Should().BeTrue();

        Customer customer = new(companyId, "SDC-C", "Discount hunter", Scenario.Usd);
        Item widget = new(companyId, "SDC-W", "Widget") { RevenueAccountCode = "4000" };
        db.AddRange(customer, widget);
        await db.SaveChangesAsync();
        (await db.Companies.SingleAsync(c => c.Id == companyId)).MaxSalesDiscountPercent = 10m;
        await db.SaveChangesAsync();
        IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
        long list = (await pricing.CreatePriceListAsync(companyId, $"SDC{tag}", "Standard", Scenario.Usd, true)).Value;
        (await pricing.SetPriceAsync(companyId, list, widget.Id, 1m, 100m)).IsSuccess.Should().BeTrue();

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        CreateSalesOrderRequest Order(string number, decimal price, long? authoriser = null) => new()
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, RaisedByUserId = clerk,
            DiscountAuthorisedByUserId = authoriser, Lines = [new SalesOrderLineRequest(widget.Id, "Widget", 1m, price, null, "4000")],
        };

        (await orders.CreateOrderAsync(Order("SDC-1", 95m))).IsSuccess.Should().BeTrue();
        (await orders.CreateOrderAsync(Order("SDC-2", 80m))).Error.Code.Should().Be("Sop.DiscountOverLimit");
        (await orders.CreateOrderAsync(Order("SDC-2", 80m, clerk))).Error.Code.Should().Be("Sop.DiscountSelfAuthorised");
        long approved = (await orders.CreateOrderAsync(Order("SDC-2", 80m, manager))).Value.SalesOrderId;
        SalesOrder signed = await db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == approved);
        signed.DiscountAuthorisedByUserId.Should().Be(manager);
        signed.Lines.Single().ListUnitPrice.Should().Be(100m);

        WorkflowDefinition definition = new(companyId, WorkflowDocumentTypes.SalesDiscount, "Sales discounts");
        definition.AddStage(1, "Manager").AddEligibleUser(manager);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
        long held = (await orders.CreateOrderAsync(Order("SDC-3", 70m))).Value.SalesOrderId;
        (await db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.Id == held)).AwaitingDiscountApproval.Should().BeTrue();
        long instance = await db.Set<WorkflowInstance>().Where(w => w.CompanyId == companyId && w.DocumentReference == "SDC-3").Select(w => w.Id).SingleAsync();
        (await scope.ServiceProvider.GetRequiredService<IWorkflowService>().ActAsync(instance, manager, WorkflowActionType.Approve, null)).IsSuccess.Should().BeTrue();
        (await db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.Id == held)).AwaitingDiscountApproval.Should().BeFalse();
    }
}
