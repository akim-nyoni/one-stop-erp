using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Infrastructure.Reporting;
using One4All.Reporting;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the report resolution order user version, company version, system version: a user's own version is theirs alone and can be removed.</summary>
[Collection(AccountingCollection.Name)]
public sealed class UserReportVersionTests
{
    private readonly AccountingFixture _fixture;

    public UserReportVersionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_users_own_version_comes_first_for_that_user_only()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        IReportDefinitionService definitions = scope.ServiceProvider.GetRequiredService<IReportDefinitionService>();

        ReportDefinition company = SystemReports.TrialBalance();
        company.Name = "Company trial balance";
        (await definitions.SaveAsync(companyId, company.Code, company.Name, company.Module, ReportDefinitionSerializer.Serialize(company), null, "company")).IsSuccess.Should().BeTrue();
        ReportDefinition mine = SystemReports.TrialBalance();
        mine.Name = "My trial balance";
        const long me = 990001, someoneElse = 990002;
        (await definitions.SaveUserVersionAsync(companyId, me, mine.Code, mine.Name, mine.Module, ReportDefinitionSerializer.Serialize(mine), "mine")).Value.Should().Be(1);
        (await definitions.SaveUserVersionAsync(companyId, me, mine.Code, mine.Name, mine.Module, ReportDefinitionSerializer.Serialize(mine), "again")).Error.Code.Should().Be("Rpt.NoChange");

        static async Task<string?> NameFor(One4AllDbContext db, long companyId, long? user)
        {
            ReportRunner runner = user is long id ? new ReportRunner(db, [], new FixedUser(id)) : new ReportRunner(db, []);
            ReportDefinition? found = await (Task<ReportDefinition?>)typeof(ReportRunner).GetMethod("ResolveDefinitionAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                [typeof(long), typeof(string), typeof(CancellationToken)])!.Invoke(runner, [companyId, SystemReports.TrialBalanceCode, CancellationToken.None])!;
            return found?.Name;
        }

        (await NameFor(db, companyId, me)).Should().Be("My trial balance");
        (await NameFor(db, companyId, someoneElse)).Should().Be("Company trial balance");
        (await NameFor(db, companyId, null)).Should().Be("Company trial balance");

        (await definitions.RemoveUserVersionAsync(companyId, me, SystemReports.TrialBalanceCode)).IsSuccess.Should().BeTrue();
        (await NameFor(db, companyId, me)).Should().Be("Company trial balance");
        (await definitions.RemoveUserVersionAsync(companyId, me, SystemReports.TrialBalanceCode)).Error.Code.Should().Be("Rpt.NoUserVersion");
    }

    private sealed class FixedUser(long id) : One4All.Application.Abstractions.Security.ICurrentUser
    {
        public long? UserId => id;

        public string UserName => "tester";

        public long? CompanyId => null;

        public bool IsAuthenticated => true;

        public One4All.Core.Security.EffectivePermissions Permissions => null!;

        public string? Terminal => null;
    }
}
