using FluentAssertions;
using One4All.Application.Vouchers;
using One4All.Core.Vouchers;
using Xunit;

namespace One4All.UnitTests.Vouchers;

/// <summary>Covers the printed accounting voucher: title, party, lines, totals, posting and signature blocks.</summary>
public sealed class AccountingVoucherFormatterTests
{
    private static AccountingVoucherDocument Payment(string? approvedBy, string? journal) => new(
        "Test Co", AccountingVoucherType.Payment, "PV-00001", new DateOnly(2026, 3, 10), "Office Mart", "March stationery", "USD",
        [new VoucherDocumentLine("6800", "Office Supplies", "Paper", 120m, 0m), new VoucherDocumentLine("6700", "Travel", null, 30m, 0m)],
        "User 1", approvedBy, journal);

    [Fact]
    public void An_approved_payment_voucher_prints_everything_needed_to_sign_it()
    {
        string text = AccountingVoucherFormatter.ToPlainText(Payment("User 2", "JNL-2026-00042"));

        text.Should().Contain("PAYMENT VOUCHER").And.Contain("No: PV-00001").And.Contain("Date: 10 Mar 2026");
        text.Should().Contain("Pay: Office Mart").And.Contain("Being: March stationery");
        text.Should().Contain("6800 Office Supplies - Paper").And.Contain("120.00");
        text.Should().Contain("TOTAL (USD)").And.Contain("150.00");
        text.Should().Contain("Posted: journal JNL-2026-00042");
        text.Should().Contain("Prepared by: User 1").And.Contain("Approved by: User 2").And.Contain("Received by:");
    }

    [Fact]
    public void A_draft_shows_it_is_not_yet_posted()
    {
        string text = AccountingVoucherFormatter.ToPlainText(Payment(null, null));

        text.Should().Contain("awaiting approval - not posted").And.Contain("Approved by: ____________");
    }

    [Fact]
    public void A_journal_voucher_has_no_recipient_signature()
    {
        AccountingVoucherDocument journal = Payment("User 2", "JNL-1") with { Type = AccountingVoucherType.Journal };

        string text = AccountingVoucherFormatter.ToPlainText(journal);

        text.Should().Contain("JOURNAL VOUCHER").And.Contain("Re: Office Mart").And.NotContain("Received by:");
        AccountingVoucherFormatter.Title(AccountingVoucherType.Receipt).Should().Be("RECEIPT VOUCHER");
    }
}
