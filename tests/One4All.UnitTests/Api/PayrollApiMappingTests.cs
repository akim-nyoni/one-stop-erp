using FluentAssertions;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Api;
using One4All.Application.Security;
using One4All.Core.Housing;
using One4All.Core.Payroll;
using MaritalStatus = One4All.Core.Payroll.MaritalStatus;
using Xunit;

namespace One4All.UnitTests.Api;

/// <summary>
/// The REST API's employee documents map onto the application's requests (enumerations by name, bank details only at engagement), and employees,
/// runs and stand histories onto their views, the pay fields named so the response filter strips them.
/// </summary>
public sealed class PayrollApiMappingTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    [Fact]
    public void Engaging_needs_number_name_and_date_and_parses_enumerations_by_name()
    {
        EmployeeApi api = new()
        {
            EmployeeNumber = " e-7 ", FullName = " Rudo Moyo ", EngagementDate = Day, BasicSalary = 1_200m, EmploymentType = "contract", ContractEndDate = Day.AddYears(1),
            PaymentMethod = "mobilemoney", MobileMoneyNumber = "0771", PayFrequency = "Weekly", MaritalStatus = "married", Gender = "female", Department = "ADM",
        };

        NewEmployee employee = PayrollApiMapping.NewEmployee(api).Value;
        employee.Should().BeEquivalentTo(new
        {
            EmployeeNumber = "e-7", FullName = "Rudo Moyo", EngagementDate = Day, BasicSalary = 1_200m, EmploymentType = EmploymentType.Contract, PaymentMethod = PayMethod.MobileMoney,
            MobileMoneyNumber = "0771", PayFrequency = (PayFrequency?)PayFrequency.Weekly, MaritalStatus = (MaritalStatus?)MaritalStatus.Married, Gender = Gender.Female, Department = "ADM",
        });
        PayrollApiMapping.NewEmployee(api with { BasicSalary = null }).Value.BasicSalary.Should().Be(0m);
        PayrollApiMapping.NewEmployee(api with { FullName = " " }).Error.Code.Should().Be("Api.Employee");
        PayrollApiMapping.NewEmployee(api with { EngagementDate = null }).Error.Code.Should().Be("Api.EngagementDate");
        PayrollApiMapping.NewEmployee(api with { HousingTenancyNumber = "T-1" }).Error.Code.Should().Be("Api.EngageOnly");
        PayrollApiMapping.NewEmployee(api with { PaymentMethod = "barter" }).Error.Message.Should().Contain("BankTransfer");
        PayrollApiMapping.NewEmployee(api with { Gender = "2" }).Error.Code.Should().Be("Api.Enum", "enumerations are named, not numbered");
    }

    [Fact]
    public void A_change_leaves_out_fields_and_refuses_bank_details_and_engagement_fields()
    {
        EmployeeUpdate update = PayrollApiMapping.Update("E-7", new EmployeeApi { EmployeeNumber = "e-7", Grade = "C2", PayFrequency = "monthly", HousingTenancyNumber = "" }).Value;
        update.Should().BeEquivalentTo(new { Grade = "C2", PayFrequency = (PayFrequency?)PayFrequency.Monthly, HousingTenancyNumber = string.Empty });
        (update.FullName, update.BasicSalary, update.EmploymentType, update.PaymentMethod).Should().Be((null, null, null, null));

        PayrollApiMapping.Update("E-7", new EmployeeApi { EmployeeNumber = "E-8" }).Error.Code.Should().Be("Api.EmployeeNumber");
        PayrollApiMapping.Update("E-7", new EmployeeApi { BankAccountNumber = "123" }).Error.Code.Should().Be("Api.BankDetails");
        PayrollApiMapping.Update("E-7", new EmployeeApi { MobileMoneyNumber = "0771" }).Error.Code.Should().Be("Api.BankDetails");
        PayrollApiMapping.Update("E-7", new EmployeeApi { EngagementDate = Day }).Error.Code.Should().Be("Api.EngagedFields");
        PayrollApiMapping.Update("E-7", new EmployeeApi { MaritalStatus = "complicated" }).Error.Code.Should().Be("Api.Enum");
    }

    [Fact]
    public void Views_name_pay_fields_so_the_response_filter_strips_them()
    {
        Employee employee = new(3, "E-7", "Rudo Moyo", 1_200m, Day) { Department = "ADM", BankAccountNumber = "123" };
        EmployeeViewApi view = PayrollApiMapping.Employee(employee, "HQ", "E-1", null);
        view.Should().BeEquivalentTo(new { EmployeeNumber = "E-7", FullName = "Rudo Moyo", Status = "Active", BasicSalary = 1_200m, Branch = "HQ", ReportsTo = "E-1", Department = "ADM" });
        typeof(EmployeeViewApi).GetProperties().Select(p => p.Name).Should().NotContain(n => n.StartsWith("Bank", StringComparison.Ordinal), "bank details are not shown");

        PayrollRunRecord record = new(3, "2026-09", Day, 12, 10_000m, 2_500m, 7_500m, "J-9", PayRunKind.Regular, "PR-09", payGroup: "WEEKLY");
        PayrollRunApi run = PayrollApiMapping.Run(record);
        run.Should().Be(new PayrollRunApi("PR-09", "2026-09", "WEEKLY", "Regular", Day, 12, 10_000m, 2_500m, 7_500m, "J-9", null, null, null));

        // Pay fields are removed for a user without the right to see salaries; nothing else is.
        IReadOnlySet<SensitiveField> salary = new HashSet<SensitiveField> { SensitiveField.Salary };
        string[] hidden = [.. typeof(EmployeeViewApi).GetProperties().Concat(typeof(PayrollRunApi).GetProperties()).Select(p => p.Name).Where(n => FieldSensitivity.IsHidden(n, salary))];
        hidden.Should().BeEquivalentTo("BasicSalary", "IsSalaryHeld", "GrossPay", "TotalDeductions", "NetPay");
    }

    [Fact]
    public void A_stand_history_names_its_states_and_users()
    {
        StandHistory history = new("ST-12", StandStatus.Allocated, [StandStatus.Sold],
            [new StandMove(StandStatus.Planned, StandStatus.Available, Day, 4, "Council resolution 12", null), new StandMove(StandStatus.Available, StandStatus.Allocated, Day.AddDays(3), null, "AL-1", "Allocated")],
            ["ST-1", "P-100"]);
        StandHistoryApi api = PayrollApiMapping.StandHistory(history, new Dictionary<long, string> { [4] = "planner" });
        api.Should().BeEquivalentTo(new { StandNumber = "ST-12", Status = "Allocated", Next = new[] { "Sold" }, Lineage = new[] { "ST-1", "P-100" } });
        api.Moves.Should().Equal(new StandMoveApi("Planned", "Available", Day, "planner", "Council resolution 12", null), new StandMoveApi("Available", "Allocated", Day.AddDays(3), string.Empty, "AL-1", "Allocated"));
    }
}
