using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Cooperative allocations: stands to cooperatives, then to their members.</summary>
public sealed class CooperativeService : ICooperativeService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The receivables ledger the cooperative's account is kept on.</param>
    public CooperativeService(One4AllDbContext db, ISalesLedgerService ar)
    {
        _db = db;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result> LinkAccountAsync(long companyId, string cooperativeCode, long customerId, CancellationToken ct = default)
    {
        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (!await _db.Customers.AnyAsync(c => c.CompanyId == companyId && c.Id == customerId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Hse.CustomerNotFound", "The customer account was not found."));
        }

        if (await _db.Set<Cooperative>().AnyAsync(c => c.CompanyId == companyId && c.Id != found.Value.Id && c.AccountCustomerId == customerId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.AccountInUse", "That account is already another cooperative's."));
        }

        found.Value.AccountCustomerId = customerId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ChargeAsync(long companyId, string cooperativeCode, DateOnly date, decimal amount, string incomeAccountCode, string reference, CancellationToken ct = default)
    {
        Result<(Cooperative Cooperative, Customer Account)> found = await AccountAsync(companyId, cooperativeCode, amount, reference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = found.Value.Account.Id, DocumentNumber = reference.Trim(), DocumentDate = date, Currency = found.Value.Account.Currency, NetAmount = amount,
            SalesAccountCode = (incomeAccountCode ?? string.Empty).Trim(), Narrative = $"Charge to cooperative {found.Value.Cooperative.Code}",
        }, ct).ConfigureAwait(false);
        return invoiced.IsFailure ? Result.Failure(invoiced.Error) : Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> RecordContributionAsync(long companyId, string cooperativeCode, string memberIdNumber, DateOnly date, decimal amount, string bankAccountCode, string reference,
        CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<(Cooperative Cooperative, Customer Account)> found = await AccountAsync(companyId, cooperativeCode, amount, reference, ct).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return Result.Failure(found.Error);
            }

            string id = NormaliseId(memberIdNumber);
            CooperativeMember? member = found.Value.Cooperative.Members.FirstOrDefault(m => m.IdNumber == id && m.IsActive);
            if (member is null)
            {
                return Result.Failure(Error.NotFound("Hse.MemberNotFound", $"ID {id} is not an active member of {found.Value.Cooperative.Code}."));
            }

            Result<TradingPostingResult> receipted = await _ar.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, CustomerId = found.Value.Account.Id, DocumentNumber = reference.Trim(), ReceiptDate = date, Currency = found.Value.Account.Currency, Amount = amount,
                BankAccountCode = (bankAccountCode ?? string.Empty).Trim(), Narrative = $"Contribution by {member.FullName} to cooperative {found.Value.Cooperative.Code}",
            }, ct).ConfigureAwait(false);
            if (receipted.IsFailure)
            {
                return Result.Failure(receipted.Error);
            }

            _db.Set<CooperativeContribution>().Add(new CooperativeContribution(companyId, found.Value.Cooperative.Id, member.Id, date, amount, reference.Trim()));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }, ct);

    /// <inheritdoc />
    public async Task<Result<CooperativeStatement>> StatementAsync(long companyId, string cooperativeCode, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Cooperative cooperative = found.Value;
        if (cooperative.AccountCustomerId is not long account)
        {
            return Error.Rule("Hse.NoAccount", $"Cooperative {cooperative.Code} has no account linked.");
        }

        if (to < from)
        {
            return Error.Validation("Hse.StatementDates", "The statement ends before it starts.");
        }

        var items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == account && i.DocumentDate <= to)
            .Select(i => new { i.DocumentDate, i.DocumentNumber, i.ItemType, i.Narrative, Signed = i.Sign == LedgerSign.Debit ? i.OriginalBase : -i.OriginalBase, i.Id })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal opening = items.Where(i => i.DocumentDate < from).Sum(i => i.Signed);
        decimal running = opening;
        List<CooperativeStatementLine> lines = [];
        foreach (var item in items.Where(i => i.DocumentDate >= from).OrderBy(i => i.DocumentDate).ThenBy(i => i.Id))
        {
            running += item.Signed;
            lines.Add(new CooperativeStatementLine(item.DocumentDate, item.DocumentNumber, item.Narrative ?? item.ItemType.ToString(), Math.Max(item.Signed, 0m), Math.Max(-item.Signed, 0m), running));
        }

        var paid = await _db.Set<CooperativeContribution>().AsNoTracking().Where(c => c.CooperativeId == cooperative.Id && c.Date >= from && c.Date <= to)
            .GroupBy(c => c.MemberId).Select(g => new { g.Key, Total = g.Sum(c => c.Amount) }).ToListAsync(ct).ConfigureAwait(false);
        List<MemberContributions> contributions = [.. paid.Select(p => cooperative.Members.First(m => m.Id == p.Key)).Zip(paid, (m, p) => new MemberContributions(m.IdNumber, m.FullName, p.Total))
            .OrderBy(m => m.FullName, StringComparer.Ordinal)];
        return new CooperativeStatement($"{cooperative.Code} {cooperative.Name}", from, to, opening, lines, running, contributions);
    }

    // The cooperative with its linked account, for a positive amount and a reference.
    private async Task<Result<(Cooperative Cooperative, Customer Account)>> AccountAsync(long companyId, string cooperativeCode, decimal amount, string reference, CancellationToken ct)
    {
        if (amount <= 0m || string.IsNullOrWhiteSpace(reference))
        {
            return Error.Validation("Hse.CooperativeAmount", "A charge or contribution needs a positive amount and a reference.");
        }

        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Customer? account = found.Value.AccountCustomerId is long id ? await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false) : null;
        if (account is null)
        {
            return Error.Rule("Hse.NoAccount", $"Cooperative {found.Value.Code} has no account linked.");
        }

        return (found.Value, account);
    }

    /// <inheritdoc />
    public async Task<Result<long>> RegisterAsync(long companyId, string code, string name, string registrationNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(registrationNumber))
        {
            return Error.Validation("Hse.CooperativeDetails", "A cooperative needs a code, a name and a registration number.");
        }

        string key = code.Trim().ToUpperInvariant();
        if (await _db.Set<Cooperative>().AnyAsync(c => c.CompanyId == companyId && c.Code == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.CooperativeExists", $"Cooperative {key} already exists.");
        }

        Cooperative cooperative = new(companyId, key, name.Trim(), registrationNumber.Trim());
        _db.Set<Cooperative>().Add(cooperative);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return cooperative.Id;
    }

    /// <inheritdoc />
    public async Task<Result> AddMemberAsync(long companyId, string cooperativeCode, string idNumber, string fullName, DateOnly joinedOn, CancellationToken ct = default)
    {
        string id = NormaliseId(idNumber);
        if (id.Length == 0 || string.IsNullOrWhiteSpace(fullName))
        {
            return Result.Failure(Error.Validation("Hse.MemberDetails", "A member needs a national ID and a name."));
        }

        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Members.Any(m => m.IdNumber == id))
        {
            return Result.Failure(Error.Conflict("Hse.MemberExists", $"ID {id} is already a member of {found.Value.Code}."));
        }

        found.Value.AddMember(new CooperativeMember(id, fullName.Trim(), joinedOn));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RemoveMemberAsync(long companyId, string cooperativeCode, string idNumber, CancellationToken ct = default)
    {
        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        CooperativeMember? member = found.Value.Members.FirstOrDefault(m => m.IdNumber == NormaliseId(idNumber) && m.IsActive);
        if (member is null)
        {
            return Result.Failure(Error.NotFound("Hse.MemberNotFound", $"No active member {NormaliseId(idNumber)} in {found.Value.Code}."));
        }

        member.Leave();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<int>> AllocateStandsAsync(long companyId, string cooperativeCode, IReadOnlyList<long> standIds, DateOnly date, long requestedByUserId, long approvedByUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(standIds);
        return _db.AtomicAsync(() => AllocateCoreAsync(companyId, cooperativeCode, standIds, date, requestedByUserId, approvedByUserId, ct), ct);
    }

    private async Task<Result<int>> AllocateCoreAsync(long companyId, string cooperativeCode, IReadOnlyList<long> standIds, DateOnly date, long requestedByUserId, long approvedByUserId,
        CancellationToken ct)
    {
        // Stands go to a cooperative on two signatures, like a waiting-list allocation (brief §4.10.4).
        if (approvedByUserId <= 0 || approvedByUserId == requestedByUserId)
        {
            return Error.Rule("Hse.SelfApproval", "A cooperative allocation is approved by someone other than whoever requested it.");
        }

        if (standIds.Count == 0 || standIds.Distinct().Count() != standIds.Count)
        {
            return Error.Validation("Hse.CooperativeStands", "List each stand once.");
        }

        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        List<HousingStand> stands = await _db.HousingStands.Where(s => s.CompanyId == companyId && standIds.Contains(s.Id)).ToListAsync(ct).ConfigureAwait(false);
        if (stands.Count != standIds.Count)
        {
            return Error.NotFound("Hse.StandNotFound", "A stand is not on the register.");
        }

        HousingStand? unavailable = stands.FirstOrDefault(s => s.Status != StandStatus.Available);
        if (unavailable is not null || await StandClaims.AnyClaimedAsync(_db, standIds, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.StandUnavailable", "Every stand must be available and not already allocated.");
        }

        foreach (HousingStand stand in stands)
        {
            stand.MarkAllocated();
            CooperativeStand holding = new(companyId, found.Value.Id, stand.Id, date);
            holding.Approved(requestedByUserId, approvedByUserId);
            _db.Set<CooperativeStand>().Add(holding);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stands.Count;
    }

    /// <inheritdoc />
    public async Task<Result> SubAllocateAsync(long companyId, string cooperativeCode, long standId, string memberIdNumber, DateOnly date, CancellationToken ct = default)
    {
        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Cooperative cooperative = found.Value;
        CooperativeStand? holding = await _db.Set<CooperativeStand>().FirstOrDefaultAsync(c => c.CooperativeId == cooperative.Id && c.StandId == standId, ct).ConfigureAwait(false);
        if (holding is null)
        {
            return Result.Failure(Error.Rule("Hse.NotCooperativeStand", $"The stand is not allocated to {cooperative.Code}."));
        }

        if (holding.MemberId is not null)
        {
            return Result.Failure(Error.Conflict("Hse.StandSubAllocated", "The stand has already been sub-allocated."));
        }

        CooperativeMember? member = cooperative.Members.FirstOrDefault(m => m.IdNumber == NormaliseId(memberIdNumber) && m.IsActive);
        if (member is null)
        {
            return Result.Failure(Error.Rule("Hse.NotActiveMember", $"{NormaliseId(memberIdNumber)} is not an active member of {cooperative.Code}."));
        }

        if (await _db.Set<CooperativeStand>().AnyAsync(c => c.MemberId == member.Id, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.MemberHoldsStand", $"{member.FullName} already holds a stand."));
        }

        holding.SubAllocate(member.Id, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CooperativeStandEntry>>> StandsAsync(long companyId, string cooperativeCode, CancellationToken ct = default)
    {
        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        long cooperativeId = found.Value.Id;
        Dictionary<long, string> members = found.Value.Members.ToDictionary(m => m.Id, m => m.FullName);
        var rows = await (
            from c in _db.Set<CooperativeStand>().AsNoTracking()
            join s in _db.HousingStands.AsNoTracking() on c.StandId equals s.Id
            where c.CooperativeId == cooperativeId
            orderby s.StandNumber
            select new { s.StandNumber, c.AllocatedOn, c.MemberId, c.SubAllocatedOn }).ToListAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<CooperativeStandEntry>>.Success([.. rows.Select(r =>
            new CooperativeStandEntry(r.StandNumber, r.AllocatedOn, r.MemberId is long id ? members.GetValueOrDefault(id) : null, r.SubAllocatedOn))]);
    }

    private static string NormaliseId(string? value) => (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    private async Task<Result<Cooperative>> FindAsync(long companyId, string cooperativeCode, CancellationToken ct)
    {
        string key = (cooperativeCode ?? string.Empty).Trim().ToUpperInvariant();
        Cooperative? cooperative = await _db.Set<Cooperative>().Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Code == key, ct).ConfigureAwait(false);
        if (cooperative is null)
        {
            return Error.NotFound("Hse.CooperativeNotFound", $"Cooperative {key} not found.");
        }

        return cooperative;
    }
}
