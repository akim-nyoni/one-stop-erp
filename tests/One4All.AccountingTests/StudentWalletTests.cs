using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Accounting;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers student wallets (brief §14 J 185): a top-up, spends up to the daily limit and the balance, a withdrawn student refused,
/// and the tuck-shop report rolling the wallet forward.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StudentWalletTests
{
    private readonly AccountingFixture _fixture;

    public StudentWalletTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Wallets_are_topped_up_spent_within_limits_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.AddRange(new LedgerAccount(companyId, "2495", "Student Wallets", AccountType.Liability), new LedgerAccount(companyId, "4215", "Tuck Shop Sales", AccountType.Income));
        Customer parent = new(companyId, "SW-P", "Wallet Parent", Scenario.Usd);
        db.Customers.Add(parent);
        await db.SaveChangesAsync();
        Student kid = new(companyId, "SW-001", "S Wal", "G2", parent.Id) { DailySpendLimit = 10m };
        Student gone = new(companyId, "SW-002", "S Gone", "G2", parent.Id);
        db.Students.AddRange(kid, gone);
        await db.SaveChangesAsync();

        IStudentWalletService wallets = scope.ServiceProvider.GetRequiredService<IStudentWalletService>();
        (await wallets.TopUpAsync(companyId, kid.Id, new DateOnly(2026, 3, 1), 50m, "1100", "2495")).Value.Should().Be(50m);
        (await wallets.SpendAsync(companyId, kid.Id, new DateOnly(2026, 3, 2), 6m, "Juice and bun", "2495", "4215")).Value.Should().Be(44m);
        (await wallets.SpendAsync(companyId, kid.Id, new DateOnly(2026, 3, 2), 5m, "Chips", "2495", "4215")).Error.Code.Should().Be("Sch.WalletDailyLimit");
        (await wallets.SpendAsync(companyId, kid.Id, new DateOnly(2026, 3, 2), 4m, "Chips", "2495", "4215")).Value.Should().Be(40m);
        (await wallets.SpendAsync(companyId, kid.Id, new DateOnly(2026, 3, 3), 45m, "Everything", "2495", "4215")).Error.Code.Should().Be("Sch.WalletInsufficient");
        (await wallets.TopUpAsync(companyId, gone.Id, new DateOnly(2026, 3, 1), 5m, "1100", "2495")).IsSuccess.Should().BeTrue();
        gone.Withdraw();
        await db.SaveChangesAsync();
        (await wallets.SpendAsync(companyId, gone.Id, new DateOnly(2026, 3, 2), 1m, "Sweets", "2495", "4215")).Error.Code.Should().Be("Sch.WalletNotEnrolled");

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        var rendered = await runner.RenderAsync(companyId, "SC185", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue();
        string report = Encoding.UTF8.GetString(rendered.Value.Content);
        report.Should().Contain("S Wal (SW-001),G2,0.00,50.00,10.00,1,40").And.Contain("S Gone (SW-002),G2,0.00,5.00,0.00,0,5");
    }
}
