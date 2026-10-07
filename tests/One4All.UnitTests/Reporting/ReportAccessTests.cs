using FluentAssertions;
using One4All.Application.Reporting;
using One4All.Application.Security;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers report security: a module with its own reports right (payroll, budget) needs it, a report can name its own permission, a super
/// administrator runs everything; and report rows are cut to the user's scope — rows of another department or with the department blank
/// are left out, rows without a department column pass.
/// </summary>
public sealed class ReportAccessTests
{
    [Fact]
    public void Module_and_report_permissions_are_needed()
    {
        EffectivePermissions clerk = new(new Dictionary<string, OperationFlags> { ["Reporting.Report"] = OperationFlags.View });
        EffectivePermissions payrollOfficer = new(new Dictionary<string, OperationFlags> { ["Reporting.Report"] = OperationFlags.View, ["Payroll.Reports"] = OperationFlags.View });
        ReportAccess clerkAccess = ReportAccess.For(clerk, DataScope.Everything), officerAccess = ReportAccess.For(payrollOfficer, DataScope.Everything);

        clerkAccess.MayRun("Payroll", null).Should().BeFalse();
        clerkAccess.MayRun("Budget", null).Should().BeFalse();
        clerkAccess.MayRun("Receivables", null).Should().BeTrue("the module has no reports right of its own");
        clerkAccess.MayRun(null, null).Should().BeTrue();
        officerAccess.MayRun("Payroll", null).Should().BeTrue();
        officerAccess.MayRun("Payroll", "Payroll.ViewSalary").Should().BeFalse();
        ReportAccess.For(new EffectivePermissions(new Dictionary<string, OperationFlags>(), true), DataScope.Everything).MayRun("Payroll", "Payroll.ViewSalary").Should().BeTrue();
        ReportAccess.Unrestricted.MayRun("Payroll", "Anything").Should().BeTrue();
    }

    [Fact]
    public void Rows_are_cut_to_the_scope()
    {
        DataScope finance = DataScope.From([new Dictionary<ScopeDimension, IReadOnlyList<string>> { [ScopeDimension.Department] = ["FIN"] }], isSuperAdministrator: false);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["Department"] = "FIN", ["Amount"] = 1m },
            new Dictionary<string, object?> { ["Department"] = "wks", ["Amount"] = 2m },
            new Dictionary<string, object?> { ["Department"] = " ", ["Amount"] = 3m },
            new Dictionary<string, object?> { ["Department"] = "fin", ["Branch"] = "HRE", ["Amount"] = 4m },
        ];
        ReportRowScope.Apply(rows, finance).Select(r => r["Amount"]).Should().Equal(1m, 4m);
        ReportRowScope.Apply([new Dictionary<string, object?> { ["Account"] = "6100" }], finance).Should().HaveCount(1);
        ReportRowScope.Apply(rows, DataScope.Everything).Should().HaveCount(4);
    }
}
