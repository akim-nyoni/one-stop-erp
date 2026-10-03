using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the owner register and transfers: a stand's sole owner is registered (a second registration is refused); a
/// transfer needs a clearance certificate for the stand, valid on the date and not used before, and buyers whose shares
/// make 100% with one ratepayer; joint buyers at 60 / 40 take over from the transfer date, the seller's ownership ends
/// the day before and the 60% buyer becomes the ratepayer.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PropertyTransferTests
{
    private readonly AccountingFixture _fixture;

    public PropertyTransferTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Transfers_need_a_valid_clearance_and_move_the_ratepayer()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer seller = new(companyId, "PT-SELL", "Seller", Scenario.Usd);
        Customer first = new(companyId, "PT-BUY1", "First Buyer", Scenario.Usd);
        Customer second = new(companyId, "PT-BUY2", "Second Buyer", Scenario.Usd);
        db.Customers.AddRange(seller, first, second);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "701", "Ward 8", seller.Id);
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        IOwnershipService ownership = scope.ServiceProvider.GetRequiredService<IOwnershipService>();
        DateOnly bought = new(2020, 1, 1);
        (await ownership.RegisterOwnersAsync(companyId, stand.Id, [new OwnerShare(seller.Id, 100m, true)], bought, "T100/2020")).IsSuccess.Should().BeTrue();
        (await ownership.RegisterOwnersAsync(companyId, stand.Id, [new OwnerShare(seller.Id, 100m, true)], bought, null)).Error.Code.Should().Be("Mun.OwnersRegistered");

        OwnerShare[] buyers = [new(first.Id, 60m, true), new(second.Id, 40m, false)];
        DateOnly transfer = new(2026, 5, 1);
        (await ownership.TransferAsync(companyId, stand.Id, buyers, transfer, "CC-NONE", null)).Error.Code.Should().Be("Mun.ClearanceNotFound");

        string certificate = (await scope.ServiceProvider.GetRequiredService<IClearanceService>().IssueAsync(companyId, stand.Id, new DateOnly(2026, 4, 10), 2, 60, 71)).Value.CertificateNumber;
        (await ownership.TransferAsync(companyId, stand.Id, buyers, new DateOnly(2026, 7, 1), certificate, null)).Error.Code.Should().Be("Mun.ClearanceExpired");
        (await ownership.TransferAsync(companyId, stand.Id, [new(first.Id, 60m, true), new(second.Id, 30m, false)], transfer, certificate, null)).Error.Code.Should().Be("Mun.Shares");
        (await ownership.TransferAsync(companyId, stand.Id, buyers, transfer, certificate, "T555/2026")).IsSuccess.Should().BeTrue();
        (await ownership.TransferAsync(companyId, stand.Id, [new(seller.Id, 100m, true)], new DateOnly(2026, 5, 20), certificate, null)).Error.Code.Should().Be("Mun.ClearanceUsed");

        (await db.Properties.AsNoTracking().SingleAsync(p => p.Id == stand.Id)).RatepayerCustomerId.Should().Be(first.Id);
        (await ownership.OwnersAsync(companyId, stand.Id, new DateOnly(2026, 4, 30))).Should().Equal(
            new OwnerEntry("PT-SELL", 100m, true, bought, new DateOnly(2026, 4, 30), "REGISTER"));
        (await ownership.OwnersAsync(companyId, stand.Id, transfer)).Should().Equal(
            new OwnerEntry("PT-BUY1", 60m, true, transfer, null, certificate),
            new OwnerEntry("PT-BUY2", 40m, false, transfer, null, certificate));
        (await ownership.HistoryAsync(companyId, stand.Id)).Should().HaveCount(3);
    }
}
