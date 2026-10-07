using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Core.Inventory;
using One4All.Core.Payroll;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers licence count limits at the point of creation: with an enforced licence allowing one more warehouse (installation-wide) and one more
/// employee (the payroll module's limit), the next of each is saved and the one after is refused whole; terminated employees do not count.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LicenceLimitTests
{
    private readonly AccountingFixture _fixture;

    public LicenceLimitTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Creating_past_a_licence_limit_is_refused()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Ed25519KeyPair keys = Ed25519KeyPair.Generate();
        LicensingOptions options = new() { Enforce = true };
        options.TrustedKeys["lim-test"] = keys.PublicKeyBase64;
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(options));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        int warehouses = await db.Warehouses.CountAsync(), employees = await db.Employees.CountAsync(e => e.Status != EmployeeStatus.Terminated);

        ILicenceService licence = scope.ServiceProvider.GetRequiredService<ILicenceService>();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        string token = new LicenseSigner(keys.PrivateKeyBase64).Sign(new LicensePayload
        {
            KeyId = "lim-test", Product = LicenceService.Product, Customer = new LicenceCustomer { Id = "L", Name = "Limited Co" }, ValidFrom = new DateOnly(2025, 1, 1), ValidTo = today.AddYears(1),
            Modules = [new LicenceModuleGrant { Code = "GL" }, new LicenceModuleGrant { Code = "PAY", Limits = new Dictionary<string, int?> { ["employees"] = employees + 1 } }],
            Limits = new Dictionary<string, int?> { ["warehouses"] = warehouses + 1 },
        });
        (await licence.InstallAsync(token)).IsUsable.Should().BeTrue();

        db.Warehouses.Add(new Warehouse(companyId, "LL-W1", "One more"));
        await db.SaveChangesAsync();
        db.Warehouses.Add(new Warehouse(companyId, "LL-W2", "Too many"));
        Func<Task> overWarehouses = () => db.SaveChangesAsync();
        (await overWarehouses.Should().ThrowAsync<LicenceLimitException>()).Which.Message.Should().Contain("warehouses");
        db.ChangeTracker.Clear();

        Employee first = new(companyId, "LL-E1", "First Hire", 1000m, today);
        db.Employees.Add(first);
        await db.SaveChangesAsync();
        db.Employees.Add(new Employee(companyId, "LL-E2", "Second Hire", 1000m, today));
        Func<Task> overEmployees = () => db.SaveChangesAsync();
        await overEmployees.Should().ThrowAsync<LicenceLimitException>();
        db.ChangeTracker.Clear();
        (await db.Employees.AnyAsync(e => e.CompanyId == companyId && e.EmployeeNumber == "LL-E2")).Should().BeFalse();

        // Once someone leaves, there is room again.
        Employee leaver = await db.Employees.SingleAsync(e => e.Id == first.Id);
        leaver.Terminate();
        await db.SaveChangesAsync();
        db.Employees.Add(new Employee(companyId, "LL-E2", "Second Hire", 1000m, today));
        await db.SaveChangesAsync();
    }
}
