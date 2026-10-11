using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers a cooperative's account: linked to a customer, charged 1,000, two members contribute 300 and 200 (receipted on its account),
/// a non-member is refused; the statement opens at nil, runs to 500 owed and lists each member's contributions.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CooperativeAccountTests
{
    private readonly AccountingFixture _fixture;

    public CooperativeAccountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_cooperative_keeps_its_own_account_with_members_contributions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer account = new(companyId, "CA-COOP", "Tashinga Housing Cooperative", Scenario.Usd);
        db.Customers.Add(account);
        await db.SaveChangesAsync();

        ICooperativeService cooperatives = scope.ServiceProvider.GetRequiredService<ICooperativeService>();
        (await cooperatives.RegisterAsync(companyId, "TASH", "Tashinga", "COOP/77")).IsSuccess.Should().BeTrue();
        (await cooperatives.AddMemberAsync(companyId, "TASH", "11-111111A11", "Chipo Moyo", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await cooperatives.AddMemberAsync(companyId, "TASH", "22-222222B22", "Farai Ncube", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await cooperatives.ChargeAsync(companyId, "TASH", new DateOnly(2026, 2, 1), 1000m, "4000", "CA-CHG-1")).Error.Code.Should().Be("Hse.NoAccount");
        (await cooperatives.LinkAccountAsync(companyId, "TASH", account.Id)).IsSuccess.Should().BeTrue();

        (await cooperatives.ChargeAsync(companyId, "TASH", new DateOnly(2026, 2, 1), 1000m, "4000", "CA-CHG-1")).IsSuccess.Should().BeTrue();
        (await cooperatives.RecordContributionAsync(companyId, "TASH", "11-111111A11", new DateOnly(2026, 3, 1), 300m, "1100", "CA-RC-1")).IsSuccess.Should().BeTrue();
        (await cooperatives.RecordContributionAsync(companyId, "TASH", "22-222222B22", new DateOnly(2026, 3, 5), 200m, "1100", "CA-RC-2")).IsSuccess.Should().BeTrue();
        (await cooperatives.RecordContributionAsync(companyId, "TASH", "99-999999Z99", new DateOnly(2026, 3, 5), 50m, "1100", "CA-RC-3")).Error.Code.Should().Be("Hse.MemberNotFound");

        CooperativeStatement statement = (await cooperatives.StatementAsync(companyId, "TASH", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).Value;
        statement.Opening.Should().Be(0m);
        statement.Lines.Select(l => (l.Reference, l.Debit, l.Credit, l.Balance)).Should().Equal(("CA-CHG-1", 1000m, 0m, 1000m), ("CA-RC-1", 0m, 300m, 700m), ("CA-RC-2", 0m, 200m, 500m));
        statement.Closing.Should().Be(500m);
        statement.Contributions.Should().Equal(new MemberContributions("11-111111A11", "Chipo Moyo", 300m), new MemberContributions("22-222222B22", "Farai Ncube", 200m));

        CooperativeStatement march = (await cooperatives.StatementAsync(companyId, "TASH", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 31))).Value;
        (march.Opening, march.Closing, march.Lines.Count, march.Contributions.Count).Should().Be((700m, 500m, 1, 1));
    }
}
