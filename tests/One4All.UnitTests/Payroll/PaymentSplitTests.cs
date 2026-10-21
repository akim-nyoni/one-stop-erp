using FluentAssertions;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers net-pay splits by amount or percentage: a percentage takes its share of the net, and an employee's percentages never pass 100.</summary>
public sealed class PaymentSplitTests
{
    [Fact]
    public void A_split_takes_a_fixed_amount_or_its_share_of_net_pay()
    {
        new EmployeePaymentSplit(1, 1, PayMethod.MobileMoney, "0772", 0m, 40m).AmountFrom(1234.56m).Should().Be(493.82m);
        new EmployeePaymentSplit(1, 1, PayMethod.BankTransfer, "333", 500m).AmountFrom(1234.56m).Should().Be(500m);

        EmployeePaymentSplit.Problem(null, null, 0m).Should().NotBeNull();
        EmployeePaymentSplit.Problem(100m, 10m, 0m).Should().NotBeNull();
        EmployeePaymentSplit.Problem(null, 70m, 40m).Should().Contain("110");
        EmployeePaymentSplit.Problem(null, 60m, 40m).Should().BeNull();
        EmployeePaymentSplit.Problem(250m, null, 90m).Should().BeNull();
    }

    [Fact]
    public void Splits_share_out_what_is_left_in_order_and_never_more()
    {
        // 1,000 net with 300 already paid in another currency: 60% would be 600 but only 700 is left, so the fixed 500 after it gets the last 100.
        EmployeePaymentSplit share = new(1, 1, PayMethod.BankTransfer, "A", 0m, 60m);
        EmployeePaymentSplit fixedPart = new(1, 1, PayMethod.MobileMoney, "B", 500m);
        EmployeePaymentSplit.Allocate([share, fixedPart], 1000m, 700m).Select(x => x.Amount).Should().Equal(600m, 100m);
        EmployeePaymentSplit.Allocate([share, fixedPart], 1000m, 0m).Select(x => x.Amount).Should().Equal(0m, 0m);
    }
}
