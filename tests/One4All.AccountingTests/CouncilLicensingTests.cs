using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Municipal;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers council licensing: an annual liquor licence (fee 200) and a quarterly hawker permit (fee 30) are issued and
/// invoiced; the hawker permit is due for renewal first; a failed inspection suspends the liquor licence and blocks its
/// renewal until an inspection passes; the hawker permit renewed months late pays two cycles plus a 10% penalty; the
/// liquor licence renewed on time pays one cycle; a revoked permit can be neither renewed nor inspected.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CouncilLicensingTests
{
    private readonly AccountingFixture _fixture;

    public CouncilLicensingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Licences_are_issued_inspected_renewed_and_revoked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4960", "Licence Fees", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "4970", "Penalty Income", AccountType.Income));
        Customer holder = new(companyId, "LIC-HOLD", "Corner Tavern", Scenario.Usd);
        db.Customers.Add(holder);
        await db.SaveChangesAsync();

        ICouncilLicensingService licensing = scope.ServiceProvider.GetRequiredService<ICouncilLicensingService>();
        DateOnly issued = new(2026, 1, 1);
        IssueLicenceRequest Request(CouncilLicenceKind kind, int months, decimal fee, string account = "4960", long? propertyId = null) => new()
        {
            CompanyId = companyId, Kind = kind, HolderCustomerId = holder.Id, PropertyId = propertyId, Description = "Corner Tavern, 4 Main St",
            IssuedOn = issued, CycleMonths = months, Fee = fee, RevenueAccountCode = account,
        };

        (await licensing.IssueAsync(Request(CouncilLicenceKind.Shop, 12, 50m, account: ""))).Error.Code.Should().Be("Lic.RevenueAccount");
        (await licensing.IssueAsync(Request(CouncilLicenceKind.Shop, 12, 50m, propertyId: -1))).Error.Code.Should().Be("Mun.PropertyNotFound");
        IssuedLicence liquor = (await licensing.IssueAsync(Request(CouncilLicenceKind.Liquor, 12, 200m))).Value;
        liquor.ExpiresOn.Should().Be(new DateOnly(2026, 12, 31));
        liquor.FeeInvoiceNumber.Should().Be(liquor.LicenceNumber + "-1");
        IssuedLicence hawker = (await licensing.IssueAsync(Request(CouncilLicenceKind.Hawker, 3, 30m))).Value;
        hawker.ExpiresOn.Should().Be(new DateOnly(2026, 3, 31));

        (await licensing.RenewalsDueAsync(companyId, new DateOnly(2026, 3, 15), 30)).Should().Equal(
            new ExpiringLicence(hawker.LicenceNumber, CouncilLicenceKind.Hawker, "LIC-HOLD", "Corner Tavern, 4 Main St", new DateOnly(2026, 3, 31), 16));

        (await licensing.RecordInspectionAsync(companyId, liquor.LicenceNumber, new DateOnly(2026, 6, 1), "J. Moyo", false, null)).Error.Code.Should().Be("Lic.Findings");
        (await licensing.RecordInspectionAsync(companyId, liquor.LicenceNumber, new DateOnly(2026, 6, 1), "J. Moyo", false, "No fire extinguisher")).Value.Should().Be(CouncilLicenceStatus.Suspended);
        (await licensing.RenewAsync(companyId, liquor.LicenceNumber, new DateOnly(2026, 6, 2), 10m, "4970")).Error.Code.Should().Be("Lic.Suspended");
        (await licensing.RecordInspectionAsync(companyId, liquor.LicenceNumber, new DateOnly(2026, 6, 20), "J. Moyo", true, null)).Value.Should().Be(CouncilLicenceStatus.Active);

        DateOnly late = new(2026, 8, 10);
        (await licensing.RenewAsync(companyId, hawker.LicenceNumber, late, 10m, null)).Error.Code.Should().Be("Lic.PenaltyAccount");
        (await licensing.RenewAsync(companyId, hawker.LicenceNumber, late, 10m, "4970")).Value.Should().Be(
            new LicenceRenewed(new RenewalTerms(2, new DateOnly(2026, 9, 30), 60m, 6m, true), hawker.LicenceNumber + "-2"));
        (await licensing.RenewAsync(companyId, liquor.LicenceNumber, new DateOnly(2026, 12, 20), 10m, "4970")).Value.Should().Be(
            new LicenceRenewed(new RenewalTerms(1, new DateOnly(2027, 12, 31), 200m, 0m, false), liquor.LicenceNumber + "-2"));

        (await licensing.RevokeAsync(companyId, hawker.LicenceNumber, " ")).Error.Code.Should().Be("Lic.Reason");
        (await licensing.RevokeAsync(companyId, hawker.LicenceNumber, "Trading without a permit holder present")).IsSuccess.Should().BeTrue();
        (await licensing.RenewAsync(companyId, hawker.LicenceNumber, new DateOnly(2026, 9, 1), 10m, "4970")).Error.Code.Should().Be("Lic.Revoked");
        (await licensing.RecordInspectionAsync(companyId, hawker.LicenceNumber, new DateOnly(2026, 9, 1), "J. Moyo", true, null)).Error.Code.Should().Be("Lic.Revoked");
        (await licensing.RenewalsDueAsync(companyId, new DateOnly(2026, 12, 1), 60)).Should().BeEmpty();

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "4960").Credit.Should().Be(490m);
        tb.Rows.Single(r => r.AccountCode == "4970").Credit.Should().Be(6m);
        tb.Rows.Single(r => r.AccountCode == "1200").Debit.Should().Be(496m);
    }
}
