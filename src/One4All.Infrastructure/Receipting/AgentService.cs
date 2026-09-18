using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Receipting;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using TradingReceiptRequest = One4All.Application.Abstractions.Trading.ReceiptRequest;

namespace One4All.Infrastructure.Receipting;

/// <summary>
/// Third-party collection agents (brief §RCP-11). A collection settles the debtor and holds the cash in an
/// agent cash-control account; remittance brings the net into the bank, expenses the commission, and clears
/// the agent control. The agent's collected-minus-remitted balance is the money it still holds.
/// </summary>
public sealed class AgentService : IAgentService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ISalesLedgerService _salesLedger;
    private readonly IAllocationService _allocation;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="salesLedger">The AR ledger (collections settle debtors).</param>
    /// <param name="allocation">The allocation service.</param>
    public AgentService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService salesLedger, IAllocationService allocation)
    {
        _db = db;
        _posting = posting;
        _salesLedger = salesLedger;
        _allocation = allocation;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RegisterAgentAsync(long companyId, string code, string name, decimal commissionPercent, CancellationToken ct = default)
    {
        if (commissionPercent < 0m || commissionPercent > 100m)
        {
            return Error.Validation("Rcp.InvalidCommission", "Commission must be between 0 and 100 percent.");
        }

        Agent agent = new(companyId, code, name, commissionPercent);
        _db.Set<Agent>().Add(agent);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return agent.Id;
    }

    /// <inheritdoc />
    public async Task<Result<AgentCollectionResult>> RecordCollectionAsync(long companyId, long agentId, long customerId, decimal amount, DateOnly date, string agentControlAccountCode, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The collection amount must be positive.");
        }

        Agent? agent = await _db.Set<Agent>().FirstOrDefaultAsync(a => a.Id == agentId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (agent is null)
        {
            return Error.NotFound("Rcp.AgentNotFound", $"Agent {agentId} not found.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Dr agent cash control / Cr debtor control (money is held by the agent, the debt is settled).
        Result<TradingPostingResult> receipt = await _salesLedger.PostReceiptAsync(new TradingReceiptRequest
        {
            CompanyId = companyId,
            CustomerId = customerId,
            DocumentNumber = $"AGT{agent.Code}-{date:yyyyMMdd}",
            ReceiptDate = date,
            Currency = company.BaseCurrency,
            Amount = amount,
            BankAccountCode = agentControlAccountCode,
            Narrative = $"Agent collection ({agent.Code})",
        }, ct).ConfigureAwait(false);

        if (receipt.IsFailure)
        {
            return receipt.Error;
        }

        decimal allocated = await AllocateOldestFirstAsync(companyId, customerId, receipt.Value.OpenItemId, amount, date, ct).ConfigureAwait(false);
        agent.RecordCollection(amount);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new AgentCollectionResult(amount, allocated, agent.Outstanding);
    }

    /// <inheritdoc />
    public async Task<Result<AgentRemittanceResult>> RemitAsync(long companyId, long agentId, decimal grossAmount, DateOnly date, string agentControlAccountCode, string bankAccountCode, string commissionAccountCode, CancellationToken ct = default)
    {
        if (grossAmount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The remittance amount must be positive.");
        }

        Agent? agent = await _db.Set<Agent>().FirstOrDefaultAsync(a => a.Id == agentId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (agent is null)
        {
            return Error.NotFound("Rcp.AgentNotFound", $"Agent {agentId} not found.");
        }

        if (grossAmount > agent.Outstanding + 0.0001m)
        {
            return Error.Rule("Rcp.OverRemit", $"Remittance {grossAmount} exceeds the {agent.Outstanding} the agent holds.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        decimal commission = decimal.Round(grossAmount * agent.CommissionPercent / 100m, 2, MidpointRounding.ToEven);
        decimal net = grossAmount - commission;

        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = bankAccountCode, Side = JournalSide.Debit, TransactionAmount = net },
            new JournalLineRequest { AccountCode = agentControlAccountCode, Side = JournalSide.Credit, TransactionAmount = grossAmount },
        ];
        if (commission > 0m)
        {
            lines.Insert(1, new JournalLineRequest { AccountCode = commissionAccountCode, Side = JournalSide.Debit, TransactionAmount = commission });
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "AGT-REMIT",
            DocumentNumber = $"REM{agent.Code}-{date:yyyyMMdd}",
            Narrative = $"Agent remittance ({agent.Code})",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        agent.RecordRemittance(grossAmount);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new AgentRemittanceResult(grossAmount, commission, net, agent.Outstanding, posted.Value.JournalNumber);
    }

    private async Task<decimal> AllocateOldestFirstAsync(long companyId, long customerId, long creditItemId, decimal amount, DateOnly date, CancellationToken ct)
    {
        List<OpenItem> debits = await _db.Set<OpenItem>()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.Sign == LedgerSign.Debit && i.OutstandingTransaction > 0m)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        decimal remaining = amount;
        decimal allocated = 0m;
        foreach (OpenItem debit in debits)
        {
            if (remaining <= 0m)
            {
                break;
            }

            decimal take = Math.Min(remaining, debit.OutstandingTransaction);
            Result<AllocationResult> result = await _allocation.AllocateAsync(companyId, TradingLedger.AccountsReceivable, debit.Id, creditItemId, take, date, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                break;
            }

            allocated += take;
            remaining -= take;
        }

        return allocated;
    }
}
