using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the SoD rules table end to end: a rule added there flags a user, and switching a built-in rule off clears it.</summary>
[Collection(AccountingCollection.Name)]
public sealed class SodRulesTableDbTests
{
    private readonly AccountingFixture _fixture;

    public SodRulesTableDbTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rules_kept_in_the_table_decide_the_violations()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role role = new($"SoD table {tag}");
        role.Grant("Purchasing.Supplier", OperationFlags.View | OperationFlags.Create);
        role.Grant("Purchasing.PaymentRun", OperationFlags.View | OperationFlags.Approve);
        role.Grant("Reporting.Report", OperationFlags.View | OperationFlags.Export);
        User user = new($"sod-{tag}", "Everything Clerk");
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        user.AssignRole(role.Id);
        await db.SaveChangesAsync();

        IPermissionService permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        string added = $"SOD-T{tag[..4]}".ToUpperInvariant();
        try
        {
            (await permissions.GetSodViolationsAsync(user.Id)).Select(r => r.Code).Should().Contain("SOD-AP-01");
            (await permissions.SaveSodRuleAsync(new SodRule(added, "Bad", "Nope.Nothing", OperationFlags.View, "Reporting.Report", OperationFlags.Export, SodSeverity.High), true))
                .Error.Code.Should().Be("Sod.Permission");
            (await permissions.SaveSodRuleAsync(new SodRule(added, "Approve payments and export reports", "Purchasing.PaymentRun", OperationFlags.Approve, "Reporting.Report",
                OperationFlags.Export, SodSeverity.Medium), true)).IsSuccess.Should().BeTrue();
            (await permissions.SaveSodRuleAsync(SegregationOfDutiesCatalogue.All.Single(r => r.Code == "SOD-AP-01"), false)).IsSuccess.Should().BeTrue();

            IReadOnlyList<SodRule> violated = await permissions.GetSodViolationsAsync(user.Id);
            violated.Select(r => r.Code).Should().Contain(added).And.NotContain("SOD-AP-01");
        }
        finally
        {
            // The rules table is shared by every company: leave it as found.
            db.RemoveRange(db.Set<SodRuleSetting>().Where(s => s.Code == added || s.Code == "SOD-AP-01"));
            await db.SaveChangesAsync();
        }
    }
}
