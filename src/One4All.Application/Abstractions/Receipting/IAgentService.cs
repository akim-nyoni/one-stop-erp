using One4All.Core.Common;

namespace One4All.Application.Abstractions.Receipting;

/// <summary>The result of recording an agent collection.</summary>
/// <param name="Amount">The gross collected.</param>
/// <param name="AllocatedToDebt">The amount applied to the payer's debt.</param>
/// <param name="AgentOutstanding">The agent's held-not-remitted balance after this collection.</param>
public readonly record struct AgentCollectionResult(decimal Amount, decimal AllocatedToDebt, decimal AgentOutstanding);

/// <summary>The result of an agent remittance.</summary>
/// <param name="Gross">The gross remitted.</param>
/// <param name="Commission">The commission deducted.</param>
/// <param name="Net">The net banked.</param>
/// <param name="AgentOutstanding">The agent's held-not-remitted balance after this remittance.</param>
/// <param name="JournalNumber">The GL journal number.</param>
public readonly record struct AgentRemittanceResult(decimal Gross, decimal Commission, decimal Net, decimal AgentOutstanding, string JournalNumber);

/// <summary>
/// Third-party collection agents (brief §RCP-11). An agent collects on the council's behalf against a
/// debtor account (settling the debt, holding the cash) and later remits - less commission. The agent's
/// collected-minus-remitted balance is reconciled and aged.
/// </summary>
public interface IAgentService
{
    /// <summary>Registers an agent.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The agent code.</param>
    /// <param name="name">The agent name.</param>
    /// <param name="commissionPercent">The commission percentage.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created agent id.</returns>
    Task<Result<long>> RegisterAgentAsync(long companyId, string code, string name, decimal commissionPercent, CancellationToken ct = default);

    /// <summary>
    /// Records a collection the agent made against a debtor account: Dr agent cash control, Cr debtor
    /// control (settling the debt oldest-first), so the money is held by the agent until remitted.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="agentId">The agent.</param>
    /// <param name="customerId">The debtor the money is collected against.</param>
    /// <param name="amount">The gross amount collected.</param>
    /// <param name="date">The collection date.</param>
    /// <param name="agentControlAccountCode">The agent cash-control account (debited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The collection result.</returns>
    Task<Result<AgentCollectionResult>> RecordCollectionAsync(long companyId, long agentId, long customerId, decimal amount, DateOnly date, string agentControlAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Records a remittance from the agent: Dr bank (net), Dr commission expense, Cr agent cash control
    /// (gross). Refuses to remit more than the agent currently holds.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="agentId">The agent.</param>
    /// <param name="grossAmount">The gross amount remitted (before commission).</param>
    /// <param name="date">The remittance date.</param>
    /// <param name="agentControlAccountCode">The agent cash-control account (credited).</param>
    /// <param name="bankAccountCode">The bank account the net is received into (debited).</param>
    /// <param name="commissionAccountCode">The commission-expense account (debited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The remittance result.</returns>
    Task<Result<AgentRemittanceResult>> RemitAsync(long companyId, long agentId, decimal grossAmount, DateOnly date, string agentControlAccountCode, string bankAccountCode, string commissionAccountCode, CancellationToken ct = default);
}
