using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>Where a payroll draft is in the approval workflow.</summary>
public enum PayrollDraftStatus
{
    /// <summary>Calculated, awaiting approval (or a second approval).</summary>
    Calculated = 0,

    /// <summary>Approved, ready to commit.</summary>
    Approved = 1,

    /// <summary>Committed: payslips saved and the journal posted.</summary>
    Committed = 2,

    /// <summary>Replaced by a recalculation.</summary>
    Superseded = 3,
}

/// <summary>
/// A calculated payroll awaiting maker-checker approval (<c>pay.payroll_draft</c>, brief §4.15.6): the totals and exception report the
/// approver saw, the movement from the group's last run, who calculated, who approved (twice when the movement is large) and who
/// committed.
/// </summary>
public sealed class PayrollDraft : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PayrollDraft()
    {
        PayGroup = string.Empty;
        PeriodLabel = string.Empty;
        NetPayAccountCode = string.Empty;
        DocumentNumber = string.Empty;
        Exceptions = string.Empty;
    }

    /// <summary>Creates the draft.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="payGroup">The pay group.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="netPayAccountCode">The net-pay control account.</param>
    /// <param name="documentNumber">The run document number.</param>
    /// <param name="employees">Payslips calculated.</param>
    /// <param name="gross">Gross pay.</param>
    /// <param name="deductions">Deductions.</param>
    /// <param name="net">Net pay.</param>
    /// <param name="previousNet">The net pay of the group's last run.</param>
    /// <param name="requiresSecondApproval">Whether the movement needs a second approver.</param>
    /// <param name="exceptions">The exception report the approver sees.</param>
    /// <param name="calculatedByUserId">Who calculated.</param>
    /// <param name="calculatedOn">When.</param>
    public PayrollDraft(long companyId, string payGroup, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, int employees, decimal gross, decimal deductions, decimal net,
        decimal? previousNet, bool requiresSecondApproval, string exceptions, long calculatedByUserId, DateTimeOffset calculatedOn)
    {
        CompanyId = companyId;
        PayGroup = payGroup;
        PeriodLabel = periodLabel;
        PayDate = payDate;
        NetPayAccountCode = netPayAccountCode;
        DocumentNumber = documentNumber;
        Employees = employees;
        Gross = gross;
        Deductions = deductions;
        Net = net;
        PreviousNet = previousNet;
        RequiresSecondApproval = requiresSecondApproval;
        Exceptions = exceptions;
        CalculatedByUserId = calculatedByUserId;
        CalculatedOn = calculatedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The pay group.</summary>
    public string PayGroup { get; private set; }

    /// <summary>The period.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>The pay date.</summary>
    public DateOnly PayDate { get; private set; }

    /// <summary>The net-pay control account.</summary>
    public string NetPayAccountCode { get; private set; }

    /// <summary>The run document number.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>Payslips calculated.</summary>
    public int Employees { get; private set; }

    /// <summary>Gross pay.</summary>
    public decimal Gross { get; private set; }

    /// <summary>Deductions.</summary>
    public decimal Deductions { get; private set; }

    /// <summary>Net pay.</summary>
    public decimal Net { get; private set; }

    /// <summary>The net pay of the group's last run.</summary>
    public decimal? PreviousNet { get; private set; }

    /// <summary>Whether a second approver is needed.</summary>
    public bool RequiresSecondApproval { get; private set; }

    /// <summary>The exception report the approver sees (one issue per line).</summary>
    public string Exceptions { get; private set; }

    /// <summary>The status.</summary>
    public PayrollDraftStatus Status { get; private set; }

    /// <summary>Who calculated.</summary>
    public long CalculatedByUserId { get; private set; }

    /// <summary>When it was calculated.</summary>
    public DateTimeOffset CalculatedOn { get; private set; }

    /// <summary>The first approver.</summary>
    public long? FirstApproverUserId { get; private set; }

    /// <summary>The second approver, when one was needed.</summary>
    public long? SecondApproverUserId { get; private set; }

    /// <summary>When it was fully approved.</summary>
    public DateTimeOffset? ApprovedOn { get; private set; }

    /// <summary>Who committed.</summary>
    public long? CommittedByUserId { get; private set; }

    /// <summary>When it was committed.</summary>
    public DateTimeOffset? CommittedOn { get; private set; }

    /// <summary>The run's journal.</summary>
    public string? JournalNumber { get; private set; }

    /// <summary>Records an approval; the draft is approved once the approvals it needs are in.</summary>
    /// <param name="userId">The approver.</param>
    /// <param name="when">When.</param>
    public void Approve(long userId, DateTimeOffset when)
    {
        if (FirstApproverUserId is null)
        {
            FirstApproverUserId = userId;
        }
        else
        {
            SecondApproverUserId = userId;
        }

        if (!RequiresSecondApproval || SecondApproverUserId is not null)
        {
            Status = PayrollDraftStatus.Approved;
            ApprovedOn = when;
        }
    }

    /// <summary>Records the commit.</summary>
    /// <param name="userId">Who committed.</param>
    /// <param name="when">When.</param>
    /// <param name="journalNumber">The run's journal.</param>
    public void Commit(long userId, DateTimeOffset when, string journalNumber)
    {
        Status = PayrollDraftStatus.Committed;
        CommittedByUserId = userId;
        CommittedOn = when;
        JournalNumber = journalNumber;
    }

    /// <summary>Marks the draft replaced by a recalculation.</summary>
    public void Supersede() => Status = PayrollDraftStatus.Superseded;
}
