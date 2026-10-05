using FluentAssertions;
using One4All.Application.Payroll;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

public class EmployeeChecksTests
{
    private static readonly DateOnly PayDate = new(2026, 3, 25);

    private static EmployeeFacts Facts(long id, string? nationalId = "63-111111A11", string? tax = "T1", DateOnly? born = null, decimal basic = 1000m, PayMethod method = PayMethod.BankTransfer,
        string? bank = "ACC-1", string? wallet = null, int yearsEngaged = 3, decimal leaveTaken = 5m)
        => new(id, $"E{id}", $"Employee {id}", nationalId, tax, born ?? new DateOnly(1990, 1, 1), basic, method, bank, wallet, PayDate.AddYears(-yearsEngaged), leaveTaken);

    [Fact]
    public void A_complete_record_is_clean_and_gaps_are_named()
    {
        EmployeeChecks.Issues(Facts(1), PayDate).Should().BeEmpty();
        EmployeeChecks.Issues(Facts(2, nationalId: null, tax: " ", bank: null, basic: 0m), PayDate)
            .Should().Equal("No national ID", "No tax number", "Bank transfer without an account number", "Basic salary is zero");
        EmployeeChecks.Issues(Facts(3, method: PayMethod.MobileMoney, bank: null, born: new DateOnly(2010, 6, 1)), PayDate)
            .Should().Equal("Mobile money without a wallet number", "Under 16 on the pay date");
    }

    [Fact]
    public void Shared_accounts_and_ids_and_no_leave_are_ghost_signals()
    {
        IReadOnlyDictionary<long, IReadOnlyList<string>> signals = EmployeeChecks.GhostSignals(
        [
            Facts(1, bank: "ACC-9"),
            Facts(2, bank: "acc 9", nationalId: "63-222222B22"),
            Facts(3, bank: "ACC-3", nationalId: "63 222222 b22", leaveTaken: 0m),
            Facts(4, bank: "ACC-4", nationalId: "63-444444D44", leaveTaken: 0m, yearsEngaged: 0),
        ], PayDate);

        signals.Keys.Should().BeEquivalentTo([1L, 2L, 3L]);
        signals[1].Should().Equal("Bank account shared with E2");
        signals[2].Should().Equal("Bank account shared with E1", "National ID shared with E3");
        signals[3].Should().Equal("National ID shared with E2", "No leave taken in over a year");
    }
}
