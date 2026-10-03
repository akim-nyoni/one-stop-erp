using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers accounting vouchers: a payment voucher for 120 of stationery and 30 of travel is numbered from the PV sequence,
/// cannot be approved by its preparer, and posts on a second user's approval; a receipt voucher posts money in; an
/// unbalanced journal voucher is refused and a balanced one posts; a cancelled petty cash voucher cannot be approved; a
/// payment voucher to a control account is refused at approval; the printed voucher shows the posting.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AccountingVoucherTests
{
    private const long Clerk = 51;
    private const long Accountant = 52;

    private readonly AccountingFixture _fixture;

    public AccountingVoucherTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Vouchers_are_numbered_approved_by_a_second_user_and_posted()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        IAccountingVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IAccountingVoucherService>();
        DateOnly date = new(2026, 3, 10);
        PreparedVoucher payment = (await vouchers.PrepareAsync(companyId, new VoucherRequest(AccountingVoucherType.Payment, date, "Office Mart", "1100", "March stationery",
            [new VoucherLineRequest("6800", 120m, "Paper"), new VoucherLineRequest("6700", 30m)], Clerk))).Value;
        payment.Number.Should().StartWith("PV-");
        payment.Total.Should().Be(150m);
        (await vouchers.ApproveAsync(companyId, payment.VoucherId, Clerk)).Error.Code.Should().Be("Vou.SelfApproval");
        string journal = (await vouchers.ApproveAsync(companyId, payment.VoucherId, Accountant)).Value;
        (await vouchers.ApproveAsync(companyId, payment.VoucherId, Accountant)).Error.Code.Should().Be("Vou.NotDraft");

        PreparedVoucher receipt = (await vouchers.PrepareAsync(companyId, new VoucherRequest(AccountingVoucherType.Receipt, date, "Walk-in customer", "1100", "Cash sale",
            [new VoucherLineRequest("4000", 200m)], Clerk))).Value;
        receipt.Number.Should().StartWith("RV-");
        (await vouchers.ApproveAsync(companyId, receipt.VoucherId, Accountant)).IsSuccess.Should().BeTrue();

        (await vouchers.PrepareAsync(companyId, new VoucherRequest(AccountingVoucherType.Journal, date, "Reclassify", null, null,
            [new VoucherLineRequest("6100", 50m), new VoucherLineRequest("6800", -40m)], Clerk))).Error.Code.Should().Be("Vou.JournalUnbalanced");
        PreparedVoucher reclass = (await vouchers.PrepareAsync(companyId, new VoucherRequest(AccountingVoucherType.Journal, date, "Reclassify", null, "Wages coded to stationery",
            [new VoucherLineRequest("6100", 50m), new VoucherLineRequest("6800", -50m)], Clerk))).Value;
        (await vouchers.ApproveAsync(companyId, reclass.VoucherId, Accountant)).IsSuccess.Should().BeTrue();

        PreparedVoucher petty = (await vouchers.PrepareAsync(companyId, new VoucherRequest(AccountingVoucherType.PettyCash, date, "Tea and milk", "1000", null,
            [new VoucherLineRequest("6800", 8m)], Clerk))).Value;
        (await vouchers.CancelAsync(companyId, petty.VoucherId)).IsSuccess.Should().BeTrue();
        (await vouchers.ApproveAsync(companyId, petty.VoucherId, Accountant)).Error.Code.Should().Be("Vou.NotDraft");

        PreparedVoucher toControl = (await vouchers.PrepareAsync(companyId, new VoucherRequest(AccountingVoucherType.Payment, date, "Sneaky", "1100", null,
            [new VoucherLineRequest("1200", 10m)], Clerk))).Value;
        (await vouchers.ApproveAsync(companyId, toControl.VoucherId, Accountant)).Error.Code.Should().Be("Posting.ControlAccountManualJournal");

        (await vouchers.RenderAsync(companyId, payment.VoucherId)).Value.Should().Contain("PAYMENT VOUCHER").And.Contain($"Posted: journal {journal}");

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "6800").Debit.Should().Be(70m);   // 120 paid less 50 reclassified
        tb.Rows.Single(r => r.AccountCode == "6100").Debit.Should().Be(50m);
        tb.Rows.Single(r => r.AccountCode == "1100").Debit.Should().Be(50m);   // 200 in, 150 out
    }
}
