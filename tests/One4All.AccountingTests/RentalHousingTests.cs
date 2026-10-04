using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers council rentals: a flat at 300 a month is let from 16 March with a 400 deposit held as a liability (it cannot
/// be let twice); the March run bills 154.84 pro rata and a repeat bills nothing; April bills 300 and the tenant pays
/// 300; ending the tenancy on 15 May bills 145.16 final rent, applies 300 of the deposit to the arrears and refunds 100;
/// the voids report shows the flat void 16 days (157.81 lost) behind a house void since January (992.88). Rental income
/// is 600, the deposit liability nets to zero and the tenant's account is clear.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RentalHousingTests
{
    private readonly AccountingFixture _fixture;

    public RentalHousingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tenancies_are_billed_and_ended_with_the_deposit_settled()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4975", "Rental Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "2260", "Rental Deposits Held", AccountType.Liability));
        Customer tenant = new(companyId, "RH-TEN", "Council Tenant", Scenario.Usd);
        db.Customers.Add(tenant);
        await db.SaveChangesAsync();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "flat-1", "Block A, Flat 1", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.AddUnitAsync(companyId, "HSE-9", "9 Council Road", RentalUnitType.House, 3, 200m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();

        LetUnitRequest Let() => new()
        {
            CompanyId = companyId, UnitCode = "FLAT-1", TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 3, 16), Deposit = 400m, BankAccountCode = "1100", DepositAccountCode = "2260",
        };

        string tenancy = (await rentals.LetAsync(Let())).Value;
        (await rentals.LetAsync(Let())).Error.Code.Should().Be("Hse.UnitLet");

        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 3, 31))).Value.Should().Be(new RentRunResult(1, 154.84m));
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 3, 31))).Value.TenanciesBilled.Should().Be(0);
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 4, 30))).Value.TotalRent.Should().Be(300m);
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = tenant.Id, DocumentNumber = "RH-R1", ReceiptDate = new DateOnly(2026, 4, 5), Currency = Scenario.Usd, Amount = 300m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        TenancyEnded ended = (await rentals.EndTenancyAsync(companyId, tenancy, new DateOnly(2026, 5, 15), "1100")).Value;
        ended.Should().Be(new TenancyEnded(145.16m, new DepositSettlement(300m, 100m)));
        (await rentals.EndTenancyAsync(companyId, tenancy, new DateOnly(2026, 5, 15), "1100")).Error.Code.Should().Be("Hse.TenancyEnded");
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 5, 31))).Value.TenanciesBilled.Should().Be(0);

        (await rentals.VoidsAsync(companyId, new DateOnly(2026, 5, 31))).Should().Equal(
            new VoidUnit("HSE-9", new DateOnly(2026, 1, 1), 151, 992.88m),
            new VoidUnit("FLAT-1", new DateOnly(2026, 5, 16), 16, 157.81m));

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 5, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        Net("4975").Should().Be(-600m);
        Net("2260").Should().Be(0m);
        Net("1200").Should().Be(0m);
        Net("1100").Should().Be(600m);
    }
}
