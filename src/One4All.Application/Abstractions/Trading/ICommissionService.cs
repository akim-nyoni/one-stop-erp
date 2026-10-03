using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>One customer's line on a commission statement.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="CustomerCode">Its code.</param>
/// <param name="Base">What commission is earned on: net revenue invoiced, or money collected.</param>
/// <param name="Commission">The commission earned.</param>
public sealed record CommissionLine(long CustomerId, string CustomerCode, decimal Base, decimal Commission);

/// <summary>A rep's commission statement for a period.</summary>
/// <param name="SalesRepId">The rep.</param>
/// <param name="RepCode">The rep's code.</param>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
/// <param name="Lines">Per customer.</param>
public sealed record CommissionStatement(long SalesRepId, string RepCode, DateOnly From, DateOnly To, IReadOnlyList<CommissionLine> Lines)
{
    /// <summary>The commission earned in the period.</summary>
    public decimal Total => Lines.Sum(l => l.Commission);
}

/// <summary>A rep's accrual for a month.</summary>
/// <param name="RepCode">The rep.</param>
/// <param name="Earned">Commission earned in the month.</param>
/// <param name="PreviouslyAccrued">Already booked for the month.</param>
public sealed record CommissionAccrualLine(string RepCode, decimal Earned, decimal PreviouslyAccrued)
{
    /// <summary>What this run books.</summary>
    public decimal Booked => Earned - PreviouslyAccrued;
}

/// <summary>
/// Sales rep commission (brief §4.5): commission statements per rep, and a monthly accrual (Dr commission expense, Cr
/// commission payable) that books only what has not been booked before, so reruns and late invoices are handled.
/// </summary>
public interface ICommissionService
{
    /// <summary>A rep's commission statement for a period.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesRepId">The rep.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The statement, or why there is none.</returns>
    Task<Result<CommissionStatement>> StatementAsync(long companyId, long salesRepId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Accrues the month's commission for every active rep, in one journal and one unit.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="monthEnd">Any date in the month; the journal is dated the month's last day.</param>
    /// <param name="expenseAccountCode">Commission expense.</param>
    /// <param name="payableAccountCode">Commission payable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What each rep earned and what was booked, or why it failed.</returns>
    Task<Result<IReadOnlyList<CommissionAccrualLine>>> AccrueAsync(long companyId, DateOnly monthEnd, string expenseAccountCode, string payableAccountCode, CancellationToken ct = default);
}
