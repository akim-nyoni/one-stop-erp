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
}
