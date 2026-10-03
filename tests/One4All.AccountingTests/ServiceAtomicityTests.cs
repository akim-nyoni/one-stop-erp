using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.FixedAssets;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers two of the services now run as single units of work: registering an asset into a closed period leaves no
/// asset in the register (it would otherwise depreciate with no cost in the GL), and gift-card breakage refused by a
/// closed period leaves the card's balance intact — each even after a save in the same scope.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ServiceAtomicityTests
{
    private readonly AccountingFixture _fixture;

    public ServiceAtomicityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Refused_postings_leave_no_asset_and_no_breakage()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long yearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();

            (await scope.ServiceProvider.GetRequiredService<IVoucherService>().IssueGiftCardAsync(new IssueGiftCardRequest
            {
                CompanyId = companyId, CardNumber = "SA-GC-1", Currency = Scenario.Usd, Value = 100m, IssueDate = new DateOnly(2026, 4, 5),
                CashAccountCode = "1000", LiabilityAccountCode = "2300", ExpiryDate = new DateOnly(2026, 4, 30),
            })).IsSuccess.Should().BeTrue();

            IPeriodService periods = scope.ServiceProvider.GetRequiredService<IPeriodService>();
            (await periods.SetStatusAsync(companyId, yearId, 5, "GL", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
            (await periods.SetStatusAsync(companyId, yearId, 5, "VOU", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IFixedAssetService>().RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = "SA-PRINTER", Description = "Printer", AssetClass = "Equipment",
                Cost = 1_200m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 5, 10), Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "REG-SA",
            })).IsFailure.Should().BeTrue();

            (await scope.ServiceProvider.GetRequiredService<IVoucherService>().RecogniseBreakageAsync(companyId, "SA-GC-1", new DateOnly(2026, 5, 20), "2300", "4900")).IsFailure.Should().BeTrue();

            await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<FixedAsset>().CountAsync(a => a.CompanyId == companyId)).Should().Be(0);
            (await scope.ServiceProvider.GetRequiredService<IVoucherService>().GetBalanceAsync(companyId, "SA-GC-1")).Value.Balance.Should().Be(100m);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();

            // Reopened, both go through — the closed period was the only obstacle.
            IPeriodService periods = scope.ServiceProvider.GetRequiredService<IPeriodService>();
            (await periods.SetStatusAsync(companyId, yearId, 5, "GL", PeriodStatus.Open)).IsSuccess.Should().BeTrue();
            (await periods.SetStatusAsync(companyId, yearId, 5, "VOU", PeriodStatus.Open)).IsSuccess.Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<IFixedAssetService>().RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = "SA-PRINTER", Description = "Printer", AssetClass = "Equipment",
                Cost = 1_200m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 5, 10), Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "REG-SA",
            })).IsSuccess.Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<IVoucherService>().RecogniseBreakageAsync(companyId, "SA-GC-1", new DateOnly(2026, 5, 20), "2300", "4900")).Value.Balance.Should().Be(0m);
        }
    }
}
