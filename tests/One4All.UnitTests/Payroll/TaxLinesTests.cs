using FluentAssertions;
using One4All.Application.Payroll;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers how a payslip line is recognised as PAYE, the AIDS levy or another statutory line: the element's flag first, its name as the fallback.</summary>
public sealed class TaxLinesTests
{
    [Fact]
    public void A_flagged_element_counts_whatever_it_is_called()
    {
        TaxLines.KindOf(PayElementType.Deduction, "HLEVY", "Health surcharge", StatutoryKind.AidsLevy).Should().Be(StatutoryKind.AidsLevy);
        TaxLines.IsTax(PayElementType.Deduction, "IT", "Income withholding", StatutoryKind.Paye).Should().BeTrue();
        TaxLines.IsTax(PayElementType.Deduction, "PAYE", "PAYE", StatutoryKind.SocialSecurity).Should().BeFalse("the flag wins over the name");
        TaxLines.KindOf(PayElementType.EmployerContribution, "NSSAER", "Employer NSSA", StatutoryKind.SocialSecurity).Should().Be(StatutoryKind.SocialSecurity);
    }

    [Fact]
    public void An_unflagged_deduction_is_recognised_by_its_name()
    {
        TaxLines.KindOf(PayElementType.Deduction, "PAYE", "PAYE").Should().Be(StatutoryKind.Paye);
        TaxLines.KindOf(PayElementType.Deduction, "IT", "Income tax").Should().Be(StatutoryKind.Paye);
        TaxLines.KindOf(PayElementType.Deduction, "AIDS", "AIDS levy").Should().Be(StatutoryKind.AidsLevy);
        TaxLines.KindOf(PayElementType.Deduction, "LEVY", "Aids levy on tax").Should().Be(StatutoryKind.AidsLevy, "the levy is not counted as PAYE");
        TaxLines.KindOf(PayElementType.Deduction, "MED", "Medical aid").Should().Be(StatutoryKind.None);
        TaxLines.KindOf(PayElementType.Earning, "TAXALLOW", "Tax allowance").Should().Be(StatutoryKind.None, "only a deduction is tax by name");
    }
}
