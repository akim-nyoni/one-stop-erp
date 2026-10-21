using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers API authorisation: a sales clerk with view-only customers may read the AR area but not change it nor touch the GL, may only act
/// as themselves (a left-out acting user is filled in), and a company scope limits them to that company.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ApiAccessTests
{
    private readonly AccountingFixture _fixture;

    public ApiAccessTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Requests_are_held_to_permissions_company_and_acting_user()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long home = await Scenario.SeedAsync(db);
        long other = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role clerk = new($"Clerk {tag}");
        clerk.Grant("Sales.Customer", OperationFlags.View);
        clerk.Grant("Sales.Invoice", OperationFlags.View | OperationFlags.Create | OperationFlags.Post);
        Role reader = new($"Reader {tag}");
        reader.Grant("Sales.Customer", OperationFlags.View);
        db.Roles.AddRange(clerk, reader);
        await db.SaveChangesAsync();
        User user = new($"clerk.{tag}", "Clerk");
        user.SetPasswordHash("hash", mustChange: false);
        user.AssignRole(clerk.Id);
        User scoped = new($"scoped.{tag}", "Scoped reader");
        scoped.SetPasswordHash("hash", mustChange: false);
        scoped.AssignRole(reader.Id).AddScope(new AccessScope(ScopeDimension.Company, [home.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
        db.Users.AddRange(user, scoped);
        await db.SaveChangesAsync();

        IApiAccessService access = scope.ServiceProvider.GetRequiredService<IApiAccessService>();
        Task<ApiAccessDecision> Ask(long userId, string method, string path, params (string Key, string Value)[] query)
            => access.AuthoriseAsync(new ApiAccessRequest(userId, method, path, query.ToDictionary(q => q.Key, q => q.Value)));

        (await Ask(user.Id, "GET", "/api/ar/customers/1/statement", ("companyId", $"{home}"))).Allowed.Should().BeTrue();
        (await Ask(user.Id, "POST", "/api/ar/invoices", ("companyId", $"{home}"))).Allowed.Should().BeTrue();
        (await Ask(user.Id, "POST", "/api/gl/journals/submit")).StatusCode.Should().Be(403);
        (await Ask(scoped.Id, "POST", "/api/ar/invoices", ("companyId", $"{home}"))).StatusCode.Should().Be(403);
        (await Ask(user.Id, "GET", "/api/nowhere/at-all")).StatusCode.Should().Be(404);

        // Acting as someone else is refused; leaving it out fills in the signed-in user; an assignee is not the actor.
        (await Ask(user.Id, "POST", "/api/ar/credit-limit-requests/1/approve", ("companyId", $"{home}"), ("userId", $"{user.Id + 1000}"))).StatusCode.Should().Be(403);
        (await Ask(user.Id, "POST", "/api/ar/credit-limit-requests/1/approve", ("companyId", $"{home}"))).MissingActorParameters.Should().Contain("userId");
        (await Ask(user.Id, "GET", "/api/workflow/inbox", ("userId", $"{user.Id}"))).Allowed.Should().BeTrue();

        // The scoped reader sees only their own company.
        (await Ask(scoped.Id, "GET", "/api/ar/customers/1/statement", ("companyId", $"{home}"))).Allowed.Should().BeTrue();
        (await Ask(scoped.Id, "GET", "/api/ar/customers/1/statement", ("companyId", $"{other}"))).StatusCode.Should().Be(403);
    }

    [SkippableFact]
    public async Task Order_and_stock_routes_are_held_to_their_modules()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long home = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role storeman = new($"Storeman {tag}");
        storeman.Grant("Inventory.Valuation", OperationFlags.View);
        storeman.Grant("Procurement.Grn", OperationFlags.View | OperationFlags.Create);
        db.Roles.Add(storeman);
        await db.SaveChangesAsync();
        User user = new($"storeman.{tag}", "Storeman");
        user.SetPasswordHash("hash", mustChange: false);
        user.AssignRole(storeman.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        IApiAccessService access = scope.ServiceProvider.GetRequiredService<IApiAccessService>();
        Task<ApiAccessDecision> Ask(string method, string path)
            => access.AuthoriseAsync(new ApiAccessRequest(user.Id, method, path, new Dictionary<string, string> { ["companyId"] = $"{home}" }));

        // Stock may be read but not moved; goods may be received against an order (as the signed-in user); sales orders are out of reach.
        (await Ask("GET", "/api/stock/on-hand")).Allowed.Should().BeTrue();
        (await Ask("GET", "/api/stock/valuation")).Allowed.Should().BeTrue();
        (await Ask("POST", "/api/stock/receipts")).StatusCode.Should().Be(403);
        (await Ask("POST", "/api/stock/transfers")).StatusCode.Should().Be(403);
        ApiAccessDecision receipt = await Ask("POST", "/api/pop/orders/PO-1/receipts");
        receipt.Allowed.Should().BeTrue();
        receipt.MissingActorParameters.Should().Contain("userId");
        (await Ask("GET", "/api/pop/orders")).Allowed.Should().BeTrue();
        (await Ask("GET", "/api/sop/orders")).StatusCode.Should().Be(403);
        (await Ask("POST", "/api/sop/orders/SO-1/despatch")).StatusCode.Should().Be(403);
    }

    [SkippableFact]
    public async Task Ledger_receipting_bank_and_till_routes_are_held_to_their_modules()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long home = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role cashier = new($"Till cashier {tag}");
        cashier.Grant("Pos.Sale", OperationFlags.View);
        cashier.Grant("CashBook.Teller", OperationFlags.View | OperationFlags.Create | OperationFlags.Post);
        db.Roles.Add(cashier);
        await db.SaveChangesAsync();
        User user = new($"cashier.{tag}", "Cashier");
        user.SetPasswordHash("hash", mustChange: false);
        user.AssignRole(cashier.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        IApiAccessService access = scope.ServiceProvider.GetRequiredService<IApiAccessService>();
        Task<ApiAccessDecision> Ask(string method, string path)
            => access.AuthoriseAsync(new ApiAccessRequest(user.Id, method, path, new Dictionary<string, string> { ["companyId"] = $"{home}" }));

        // Till sales and X-reads may be read but a sale not voided; receipts may be taken (as the signed-in cashier) and statement lines posted; the GL is out of reach.
        (await Ask("GET", "/api/pos/sales")).Allowed.Should().BeTrue();
        (await Ask("GET", "/api/pos/tills/TILL1/x-read")).Allowed.Should().BeTrue();
        (await Ask("POST", "/api/pos/sales/S-1/void")).StatusCode.Should().Be(403);
        ApiAccessDecision receipt = await Ask("POST", "/api/receipting/receipts");
        receipt.Allowed.Should().BeTrue();
        receipt.MissingActorParameters.Should().Contain("userId");
        (await Ask("GET", "/api/receipting/payers")).Allowed.Should().BeTrue();
        (await Ask("POST", "/api/banking/statement-lines/5/to-account")).Allowed.Should().BeTrue();
        (await Ask("GET", "/api/gl/trial-balance")).StatusCode.Should().Be(403);
        (await Ask("GET", "/api/ap/aged-creditors")).StatusCode.Should().Be(403);
    }

    [SkippableFact]
    public async Task Employee_payroll_and_stand_history_routes_are_held_to_their_modules()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long home = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role officer = new($"HR officer {tag}");
        officer.Grant("Payroll.Employee", OperationFlags.View);
        db.Roles.Add(officer);
        await db.SaveChangesAsync();
        User user = new($"hr.{tag}", "HR officer");
        user.SetPasswordHash("hash", mustChange: false);
        user.AssignRole(officer.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        IApiAccessService access = scope.ServiceProvider.GetRequiredService<IApiAccessService>();
        Task<ApiAccessDecision> Ask(string method, string path)
            => access.AuthoriseAsync(new ApiAccessRequest(user.Id, method, path, new Dictionary<string, string> { ["companyId"] = $"{home}" }));

        // Employees and runs may be read (the payslip route then asks for the right to see salaries) but no employee engaged or changed; stands are out of reach.
        (await Ask("GET", "/api/hr/employees/E-1")).Allowed.Should().BeTrue();
        (await Ask("GET", "/api/payroll/runs")).Allowed.Should().BeTrue();
        ApiAccessDecision payslip = await Ask("GET", "/api/payroll/payslips/E-1");
        payslip.Allowed.Should().BeTrue();
        payslip.MissingActorParameters.Should().Contain("userId");
        (await Ask("POST", "/api/hr/employees")).StatusCode.Should().Be(403);
        (await Ask("PUT", "/api/hr/employees/E-1")).StatusCode.Should().Be(403);
        (await Ask("GET", "/api/housing/stands/ST-12/history")).StatusCode.Should().Be(403);
    }
}
