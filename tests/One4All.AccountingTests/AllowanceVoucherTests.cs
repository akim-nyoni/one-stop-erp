using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Trading;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers allowance vouchers: five 50 fuel vouchers issued to a driver for March; a fuel station's invoice billing three
/// for 150 reconciles; billing one of them again, an unknown serial, a voucher used after it expired, the same serial
/// twice, or a wrong total is refused and changes nothing; two remain outstanding, both expired by April.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AllowanceVoucherTests
{
    private readonly AccountingFixture _fixture;

    public AllowanceVoucherTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Vouchers_billed_must_be_issued_valid_and_billed_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier station = new(companyId, "AV-FUEL", "Fuel Station", Scenario.Usd);
        db.Suppliers.Add(station);
        await db.SaveChangesAsync();

        IAllowanceVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IAllowanceVoucherService>();
        IReadOnlyList<string> serials = (await vouchers.IssueAsync(companyId,
            new AllowanceIssueRequest("FV", 1, 5, AllowanceKind.Fuel, 50m, "Driver T. Moyo", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)))).Value;
        serials.Should().Equal("FV000001", "FV000002", "FV000003", "FV000004", "FV000005");
        (await vouchers.IssueAsync(companyId, new AllowanceIssueRequest("FV", 5, 2, AllowanceKind.Fuel, 50m, "Someone", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))))
            .Error.Code.Should().Be("Vou.SerialTaken");

        DateOnly used = new(2026, 3, 12);
        BilledVoucher[] three = [new("FV000001", used), new("fv000002", used), new("FV000003", used)];
        (await vouchers.ReconcileInvoiceAsync(companyId, station.Id, "FS-77", 140m, three)).Error.Code.Should().Be("Vou.InvoiceMismatch");
        (await vouchers.ReconcileInvoiceAsync(companyId, station.Id, "FS-77", 150m, three)).Value.Should().Be(new VoucherReconciliation("FS-77", 3, 150m, 150m));

        (await vouchers.ReconcileInvoiceAsync(companyId, station.Id, "FS-78", 50m, [new("FV000001", used)])).Error.Code.Should().Be("Vou.AlreadyBilled");
        (await vouchers.ReconcileInvoiceAsync(companyId, station.Id, "FS-78", 50m, [new("FV999999", used)])).Error.Code.Should().Be("Vou.NotIssued");
        (await vouchers.ReconcileInvoiceAsync(companyId, station.Id, "FS-78", 50m, [new("FV000004", new DateOnly(2026, 4, 2))])).Error.Code.Should().Be("Vou.Expired");
        (await vouchers.ReconcileInvoiceAsync(companyId, station.Id, "FS-78", 100m, [new("FV000004", used), new("FV000004", used)])).Error.Code.Should().Be("Vou.BilledTwice");

        (await vouchers.OutstandingAsync(companyId, new DateOnly(2026, 4, 5))).Should().ContainSingle()
            .Which.Should().Be(new OutstandingVouchers("Driver T. Moyo", AllowanceKind.Fuel, 2, 100m, 2));
    }
}
