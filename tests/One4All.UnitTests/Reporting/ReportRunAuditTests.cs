using System.Text;
using FluentAssertions;
using One4All.Application.Reporting;
using One4All.Core.Reporting;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Reporting;

public class ReportRunAuditTests
{
    [Fact]
    public void Parameters_are_canonical_whatever_their_order()
    {
        Dictionary<string, object?> one = new() { ["Branch"] = "HQ", ["AsAtDate"] = new DateOnly(2026, 5, 31), ["Top"] = 10m, ["Empty"] = "", ["None"] = null };
        Dictionary<string, object?> two = new() { ["Top"] = 10m, ["asAtDate"] = new DateOnly(2026, 5, 31), ["branch"] = "HQ" };
        ReportRunAudit.Canonical(one).Should().Be("AsAtDate=2026-05-31; Branch=HQ; Top=10");
        ReportRunAudit.Canonical(two).Should().Be("asAtDate=2026-05-31; branch=HQ; Top=10");
    }

    [Theory]
    [InlineData(ReportRunPurpose.View, OperationFlags.View)]
    [InlineData(ReportRunPurpose.Print, OperationFlags.Print)]
    [InlineData(ReportRunPurpose.Export, OperationFlags.Export)]
    [InlineData(ReportRunPurpose.Reprint, OperationFlags.Reprint)]
    public void Each_purpose_needs_its_flag(ReportRunPurpose purpose, OperationFlags flag)
    {
        ReportRunAudit.RequiredFlag(purpose).Should().Be(flag);
    }

    [Fact]
    public void Repeated_prints_are_numbered_reprints()
    {
        ReportRunAudit.Classify(ReportRunPurpose.Print, 0).Should().Be((ReportRunPurpose.Print, (int?)1));
        ReportRunAudit.Classify(ReportRunPurpose.Print, 2).Should().Be((ReportRunPurpose.Reprint, (int?)3));
        ReportRunAudit.Classify(ReportRunPurpose.Reprint, 0).Should().Be((ReportRunPurpose.Print, (int?)1));
        ReportRunAudit.Classify(ReportRunPurpose.Export, 5).Should().Be((ReportRunPurpose.Export, (int?)null));
    }

    [Fact]
    public void The_fingerprint_is_a_sha256()
    {
        ReportRunAudit.Fingerprint(Encoding.UTF8.GetBytes("abc")).Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }
}
