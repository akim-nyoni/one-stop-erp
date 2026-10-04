using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers a stand lease straight-lined under IFRS 16: three years from January 2026 at 12,000 a year escalating 10% a
/// year (39,720 in all). January bills 1,000 rent and recognises 1,103.33 income, accruing 103.33 (a repeat bills
/// nothing); after the first year the accrued income stands at 1,239.96; billing every month to the end recognises the
/// full 39,720 as income, invoices exactly that rent, and leaves the accrued income account at nothing. An overlapping
/// lease of the same stand and a lease not starting on the first are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandLeaseTests
{
    private readonly AccountingFixture _fixture;

    public StandLeaseTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Lease_income_is_straight_lined_and_the_accrual_unwinds()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4979", "Stand Lease Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "1285", "Accrued Lease Income", AccountType.Asset));
        Customer lessee = new(companyId, "SL-LES", "Industrial Lessee", Scenario.Usd);
        db.Customers.Add(lessee);
        LandParcel parcel = new(companyId, $"LP-SL-{companyId}", "Lease parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand stand = new(companyId, parcel.Id, null, "SL-1", 2000m, 20000m);
        db.HousingStands.Add(stand);
        foreach (int fiscal in new[] { 2027, 2028 })
        {
            FiscalYear year = new(companyId, fiscal.ToString(System.Globalization.CultureInfo.InvariantCulture), new DateOnly(fiscal, 1, 1), new DateOnly(fiscal, 12, 31));
            for (int month = 1; month <= 12; month++)
            {
                DateOnly start = new(fiscal, month, 1);
                FiscalPeriod period = year.AddPeriod(month, start.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture), start, start.AddMonths(1).AddDays(-1));
                period.SetStatus("GL", PeriodStatus.Open);
                period.SetStatus("AR", PeriodStatus.Open);
            }

            db.FiscalYears.Add(year);
        }

        await db.SaveChangesAsync();

        IStandLeaseService leases = scope.ServiceProvider.GetRequiredService<IStandLeaseService>();
        StandLeaseRequest Request(DateOnly start) => new()
        {
            CompanyId = companyId, StandId = stand.Id, LesseeCustomerId = lessee.Id, Use = LeaseUse.Industrial, StartDate = start, TermYears = 3, AnnualRent = 12000m,
            ReviewEveryYears = 1, EscalationPercent = 10m, IncomeAccountCode = "4979", AccruedIncomeAccountCode = "1285",
        };

        (await leases.CreateAsync(Request(new DateOnly(2026, 1, 15)))).Error.Code.Should().Be("Hse.LeaseTerms");
        string lease = (await leases.CreateAsync(Request(new DateOnly(2026, 1, 1)))).Value;
        (await leases.CreateAsync(Request(new DateOnly(2027, 1, 1)))).Error.Code.Should().Be("Hse.StandLeased");
        (await leases.ScheduleAsync(companyId, lease)).Value.Should().Equal(new LeaseYear(1, 12000m), new LeaseYear(2, 13200m), new LeaseYear(3, 14520m));

        (await leases.RunMonthAsync(companyId, new DateOnly(2026, 1, 31))).Value.Should().Be(new LeaseRunResult(1, 1000m, 1103.33m));
        (await leases.RunMonthAsync(companyId, new DateOnly(2026, 1, 31))).Value.LeasesBilled.Should().Be(0);

        decimal Net(string code, DateOnly asAt)
        {
            ConsolidatedTrialBalance tb = scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], asAt).GetAwaiter().GetResult().Value;
            tb.IsBalanced.Should().BeTrue();
            return tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        }

        for (DateOnly month = new(2026, 2, 1); month <= new DateOnly(2026, 12, 1); month = month.AddMonths(1))
        {
            (await leases.RunMonthAsync(companyId, month)).IsSuccess.Should().BeTrue();
        }

        Net("1285", new DateOnly(2026, 12, 31)).Should().Be(1239.96m); // 12 × 1,103.33 − 12,000

        for (DateOnly month = new(2027, 1, 1); month <= new DateOnly(2028, 12, 1); month = month.AddMonths(1))
        {
            (await leases.RunMonthAsync(companyId, month)).IsSuccess.Should().BeTrue();
        }

        (await leases.RunMonthAsync(companyId, new DateOnly(2029, 1, 1))).Value.LeasesBilled.Should().Be(0); // the lease has ended
        Net("1285", new DateOnly(2029, 12, 31)).Should().Be(0m);
        Net("4979", new DateOnly(2029, 12, 31)).Should().Be(-39720m);
        Net("1200", new DateOnly(2029, 12, 31)).Should().Be(39720m);
    }
}
