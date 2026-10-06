using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Modules;
using One4All.Core.System;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the module registry in the running system: every registered scheduled task belongs to a module, and with an enforced licence
/// a job whose module is not licensed is not run (its run says why) while a licensed module's job runs.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ModuleGatingTests
{
    private readonly AccountingFixture _fixture;

    public ModuleGatingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Jobs_run_only_for_licensed_modules()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Ed25519KeyPair keys = Ed25519KeyPair.Generate();
        LicensingOptions options = new() { Enforce = true };
        options.TrustedKeys["mod-test"] = keys.PublicKeyBase64;
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(options));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        IModuleRegistry registry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();
        IJobScheduler jobs = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        registry.Problems(jobs.TaskKeys).Should().BeEmpty();

        // Licensed for the ledger and reporting (core), not budgeting.
        ILicenceService licence = scope.ServiceProvider.GetRequiredService<ILicenceService>();
        (await licence.InstallAsync(new LicenseSigner(keys.PrivateKeyBase64).Sign(new LicensePayload
        {
            KeyId = "mod-test", Product = LicenceService.Product, Customer = new LicenceCustomer { Id = "M", Name = "Modules Co" }, ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = new DateOnly(2030, 1, 1), Modules = [new LicenceModuleGrant { Code = "GL" }],
        }))).Status.Should().Be(LicenseStatus.Valid);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        JobDefinition Job(string code, string task) => new() { CompanyId = companyId, Code = code, TaskKey = task, Frequency = JobFrequency.Daily, TimeOfDay = new TimeOnly(2, 0), From = now };
        (await jobs.DefineAsync(Job("MODBUD", "budget.reconcile-commitments"))).IsSuccess.Should().BeTrue();
        (await jobs.DefineAsync(Job("MODSNAP", "reporting.refresh-snapshots"))).IsSuccess.Should().BeTrue();

        JobRunEntry budget = (await jobs.RunNowAsync(companyId, "MODBUD", now)).Value;
        (budget.Succeeded, budget.Message).Should().Be((false, "Not run: Budgeting (BUD) is not licensed."));
        (await jobs.RunNowAsync(companyId, "MODSNAP", now)).Value.Succeeded.Should().BeTrue();
    }
}
