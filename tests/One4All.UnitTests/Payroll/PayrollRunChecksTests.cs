using FluentAssertions;
using One4All.Application.Payroll;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers the pre-commit payroll exception report's record checks.</summary>
public sealed class PayrollRunChecksTests
{
    private static RunEmployee Clean(string number) => new(number, $"ID-{number}", $"T-{number}", PayMethod.BankTransfer, $"ACC-{number}", null, EmployeeStatus.Active, null, null,
        1500m, "B2", 1000m, 2000m, [], null);

    [Fact]
    public void A_clean_run_has_no_exceptions()
        => PayrollRunChecks.Exceptions([Clean("E1"), Clean("E2")], ["E1", "E2"], new DateOnly(2026, 5, 25)).Should().BeEmpty();

    [Fact]
    public void Every_record_problem_is_reported()
    {
        DateOnly payDate = new(2026, 5, 25);
        RunEmployee[] run =
        [
            Clean("E1") with { BankAccount = "ACC-9", NationalId = "ID-9" },
            Clean("E2") with { BankAccount = "acc 9", NationalId = "id-9", TaxNumber = null },
            Clean("E3") with { BankAccount = null },
            Clean("E4") with { Status = EmployeeStatus.Terminated, TerminationDate = new DateOnly(2026, 4, 30) },
            Clean("E5") with { ContractEndDate = new DateOnly(2026, 5, 15), BasicSalary = 900m },
            Clean("E6") with { BasicSalary = 2500m, NegativeLeave = [("Annual", -2.5m)], BankDetailsChangedOn = new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero) },
            Clean("E7") with { PaymentMethod = PayMethod.MobileMoney, MobileNumber = null },
        ];

        IReadOnlyList<string> issues = PayrollRunChecks.Exceptions(run, ["E1", "E8"], payDate);
        issues.Should().Contain([
            "E1: bank account shared with E2", "E1: national ID shared with E2", "E2: no tax number", "E3: paid by bank transfer but has no bank account",
            "E4: terminated on 2026-04-30 but still in the run", "E5: contract expired on 2026-05-15", "E5: basic 900.00 is below the B2 minimum of 1,000.00",
            "E6: basic 2,500.00 is above the B2 maximum of 2,000.00", "E6: Annual leave balance is negative (-2.5 days)", "E7: paid by mobile money but has no wallet number",
            "E8: in the previous run but not in this one",
        ]);
        issues.Should().Contain(i => i.StartsWith("E6: bank details changed on 2026-05-03", StringComparison.Ordinal));
    }
}
