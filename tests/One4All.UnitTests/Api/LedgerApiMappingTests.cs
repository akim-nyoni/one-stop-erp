using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Api;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using Xunit;
using PosTender = One4All.Core.Pos.TenderType;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;
using ReceiptTender = One4All.Core.Receipting.TenderType;

namespace One4All.UnitTests.Api;

/// <summary>The REST API's ledger reports map to their rows, and its receipting and till documents onto the application's requests, bad input refused.</summary>
public sealed class LedgerApiMappingTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    [Fact]
    public void Ledger_reports_map_to_codes_names_types_and_totals()
    {
        TrialBalanceApi tb = LedgerApiMapping.TrialBalance(new TrialBalanceReport(3, Day, "USD",
            [new TrialBalanceRow("1000", "Bank", AccountType.Asset, 150m, 0m), new TrialBalanceRow("4000", "Sales", AccountType.Income, 0m, 150m)], 150m, 150m));
        tb.Should().BeEquivalentTo(new { AsAt = Day, Currency = "USD", TotalDebit = 150m, TotalCredit = 150m, IsBalanced = true });
        tb.Rows.Should().Equal(new TrialBalanceRowApi("1000", "Bank", "Asset", 150m, 0m), new TrialBalanceRowApi("4000", "Sales", "Income", 0m, 150m));

        TrialBalanceMovementApi movement = LedgerApiMapping.TrialBalanceMovement(new TrialBalanceMovementReport(3, Day, Day.AddDays(30),
            [new TrialBalanceMovementRow("1000", "Bank", 10m, 40m, 5m, 45m), new TrialBalanceMovementRow("4000", "Sales", -10m, 5m, 40m, -45m)]));
        movement.Should().BeEquivalentTo(new { From = Day, To = Day.AddDays(30), TotalDebit = 45m, TotalCredit = 45m });
        movement.Rows[0].Should().Be(new TrialBalanceMovementRowApi("1000", "Bank", 10m, 40m, 5m, 45m));

        LedgerAccount debtors = new(3, " 1100 ", "Debtors control", AccountType.Asset, "Current assets");
        debtors.MakeControlAccount(ControlAccountType.AccountsReceivable);
        LedgerApiMapping.Account(debtors).Should().BeEquivalentTo(new { Code = "1100", Name = "Debtors control", Type = "Asset", ControlType = "AccountsReceivable" });
        LedgerApiMapping.Account(new LedgerAccount(3, "6100", "Bank charges", AccountType.Expense)).ControlType.Should().Be("None");

        AgedBalancesApi aged = LedgerApiMapping.AgedBalances(new AgeingReport(TradingLedger.AccountsReceivable, Day,
            [new AgeingRow("C1", "Acme", 10m, 20m, 0m, 0m, 5m), new AgeingRow("C2", "Bolt", 0m, 1m, 2m, 3m, 4m)]));
        aged.Rows.Should().HaveCount(2);
        aged.Rows[0].Should().Be(new AgedBalanceRowApi("C1", "Acme", 10m, 20m, 0m, 0m, 5m, 35m));
        aged.Totals.Should().Be(new AgedBalanceRowApi(string.Empty, string.Empty, 10m, 21m, 2m, 3m, 9m, 45m));

        LedgerApiMapping.Payer(new PayerHit(9, "C1", "Acme", "Stand 12", -4m)).Should().Be(new PayerApi("C1", "Acme", "Stand 12", -4m));
    }

    [Fact]
    public void Cashier_receipts_need_a_payer_for_an_account_line_and_an_account_for_any_other()
    {
        CashierReceiptApi api = new(Day, " 1000 ", [new CashierReceiptLineApi("account", 50m, Service: "Water"), new CashierReceiptLineApi("Sundry", 10m, "4500", "Clearance")],
            [new CashierReceiptTenderApi("cash", 60m)], "C1", Currency: "eur");

        ReceiptRequest request = LedgerApiMapping.Receipt(3, 21, api, 9).Value;
        request.Should().BeEquivalentTo(new
        {
            CompanyId = 3L, SessionId = 21L, PayerKind = PayerKind.ArCustomer, PayerAccountId = (long?)9L, Date = Day, CashAccountCode = "1000",
            AllocationStrategy = ReceiptAllocationStrategy.OldestFirst,
        });
        request.Currency!.Value.Value.Should().Be("EUR");
        request.Lines.Should().Equal(new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 50m, null, null, "Water"), new ReceiptLineRequest(ReceiptLineType.Sundry, 10m, "4500", "Clearance"));
        request.Tenders.Should().Equal(new ReceiptTenderRequest(ReceiptTender.Cash, 60m));

        LedgerApiMapping.Receipt(3, 21, api, null).Error.Code.Should().Be("Api.Payer");
        LedgerApiMapping.Receipt(3, 21, api with { Lines = [new CashierReceiptLineApi("Deposit", 5m)] }, 9).Error.Code.Should().Be("Api.Account");
        LedgerApiMapping.Receipt(3, 21, api with { Lines = [new CashierReceiptLineApi("Gift", 5m, "4500")] }, 9).Error.Code.Should().Be("Api.LineType");
        LedgerApiMapping.Receipt(3, 21, api with { Lines = [] }, 9).Error.Code.Should().Be("Api.Lines");
        LedgerApiMapping.Receipt(3, 21, api with { Tenders = [new CashierReceiptTenderApi("Barter", 60m)] }, 9).Error.Code.Should().Be("Api.Tender");
        LedgerApiMapping.Receipt(3, 21, api with { CashAccountCode = " " }, 9).Error.Code.Should().Be("Api.CashAccount");
        LedgerApiMapping.Receipt(3, 21, api with { AllocationStrategy = "NewestFirst" }, 9).Error.Code.Should().Be("Api.AllocationReason");
        LedgerApiMapping.Receipt(3, 21, api with { AllocationStrategy = "NewestFirst", AllocationReason = "Keep current" }, 9).Value.AllocationStrategy.Should().Be(ReceiptAllocationStrategy.NewestFirst);
        LedgerApiMapping.Receipt(3, 21, api with { AllocationStrategy = "UserDirected", AllocationReason = "x" }, 9).Error.Code.Should().Be("Api.AllocationStrategy");
        LedgerApiMapping.Receipt(3, 21, api with { Currency = "dollars" }, 9).Error.Code.Should().Be("Api.Currency");
        LedgerApiMapping.Receipt(3, 21, api with { PayerCode = null, Lines = [new CashierReceiptLineApi("Suspense", 5m, "9990")] }, null).Value.PayerKind.Should().Be(PayerKind.Anonymous);
    }

    [Fact]
    public void Reversals_need_a_reason_and_an_x_read_names_its_tenders()
    {
        LedgerApiMapping.Reason("  Posted twice ").Value.Should().Be("Posted twice");
        LedgerApiMapping.Reason(" ").Error.Type.Should().Be(ErrorType.Validation);

        DateTimeOffset opened = new(2026, 10, 1, 7, 30, 0, TimeSpan.Zero);
        TillXReadApi read = LedgerApiMapping.XRead("TILL1", opened, new TillRead(4, 120m, new Dictionary<PosTender, decimal> { [PosTender.Card] = 70m, [PosTender.Cash] = 50m }, null) { Tips = 5m });
        read.Should().BeEquivalentTo(new { TerminalCode = "TILL1", OpenedOnUtc = opened, SaleCount = 4, TotalGross = 120m, Tips = 5m });
        read.Tenders.Should().Equal(new Dictionary<string, decimal> { ["Cash"] = 50m, ["Card"] = 70m });
    }
}
