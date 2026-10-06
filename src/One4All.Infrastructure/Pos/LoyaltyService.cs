using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Common;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Pos;

/// <summary>The loyalty programme: points earned, redeemed and expired, with their value carried as a liability (brief §4.1).</summary>
public sealed class LoyaltyService : ILoyaltyService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public LoyaltyService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<long>> ConfigureAsync(LoyaltySchemeRequest request, CancellationToken ct = default)
    {
        if (request.PointsPerUnit <= 0m || request.ValuePerPoint <= 0m || request.ExpiryMonths <= 0 || string.IsNullOrWhiteSpace(request.LiabilityAccountCode)
            || string.IsNullOrWhiteSpace(request.ExpenseAccountCode) || request.Tiers.Any(t => t.Multiplier <= 0m || t.MinPoints < 0m))
        {
            return Error.Validation("Loyalty.SchemeInvalid", "A scheme needs positive earn and redemption rates, a positive expiry if any, both accounts, and positive tier multipliers.");
        }

        LoyaltyScheme? scheme = await _db.LoyaltySchemes.Include(s => s.Tiers).FirstOrDefaultAsync(s => s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (scheme is null)
        {
            scheme = new LoyaltyScheme(request.CompanyId, request.LiabilityAccountCode, request.ExpenseAccountCode);
            _db.LoyaltySchemes.Add(scheme);
        }

        scheme.PointsPerUnit = request.PointsPerUnit;
        scheme.ValuePerPoint = request.ValuePerPoint;
        scheme.ExpiryMonths = request.ExpiryMonths;
        scheme.LiabilityAccountCode = request.LiabilityAccountCode;
        scheme.ExpenseAccountCode = request.ExpenseAccountCode;
        scheme.SetTiers(request.Tiers);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return scheme.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> EnrolAsync(long companyId, string memberNumber, string name, long? customerId, DateOnly date, CancellationToken ct = default)
    {
        string number = (memberNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0 || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("Loyalty.MemberInvalid", "A member needs a number and a name.");
        }

        if (await _db.LoyaltyMembers.AnyAsync(m => m.CompanyId == companyId && m.MemberNumber == number, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Loyalty.MemberExists", $"Member {number} already exists.");
        }

        LoyaltyMember member = new(companyId, number, name.Trim(), date) { CustomerId = customerId };
        _db.LoyaltyMembers.Add(member);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return member.Id;
    }

    /// <inheritdoc />
    public Task<Result<LoyaltyEarned>> EarnAsync(long companyId, string memberNumber, decimal spend, DateOnly date, string reference, CancellationToken ct = default)
        => _db.AtomicAsync(() => EarnCoreAsync(companyId, memberNumber, spend, date, reference, ct), ct);

    /// <inheritdoc />
    public async Task<Result<decimal>> RedeemAsync(long companyId, string memberNumber, decimal points, DateOnly date, string reference, CancellationToken ct = default)
    {
        Result<(LoyaltyScheme Scheme, LoyaltyMember Member)> found = await FindAsync(companyId, memberNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        (LoyaltyScheme scheme, LoyaltyMember member) = found.Value;
        decimal balance = await BalanceAsync(member.Id, ct).ConfigureAwait(false);
        if (points <= 0m || points > balance)
        {
            return Error.Rule("Loyalty.InsufficientPoints", $"{member.MemberNumber} has {balance:0.##} points.");
        }

        decimal value = decimal.Round(points * scheme.ValuePerPoint, 2, MidpointRounding.AwayFromZero);
        _db.LoyaltyMovements.Add(new LoyaltyMovement(companyId, member.Id, LoyaltyMovementKind.Redeem, date, -points, -value, (reference ?? string.Empty).Trim(), null));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return value;
    }

    /// <inheritdoc />
    public Task<Result<(int Members, decimal Points, decimal Value)>> ExpireAsync(long companyId, DateOnly asAt, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ExpireCoreAsync(companyId, asAt, documentNumber, ct), ct);

    /// <inheritdoc />
    public async Task<Result<LoyaltyStatement>> StatementAsync(long companyId, string memberNumber, DateOnly asAt, CancellationToken ct = default)
    {
        Result<(LoyaltyScheme Scheme, LoyaltyMember Member)> found = await FindAsync(companyId, memberNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        (LoyaltyScheme scheme, LoyaltyMember member) = found.Value;
        List<LoyaltyMovement> movements = await _db.LoyaltyMovements.AsNoTracking().Where(m => m.MemberId == member.Id).OrderBy(m => m.Date).ThenBy(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
        decimal balance = movements.Sum(m => m.Points);
        (string? tier, _) = TierOf(scheme, movements, asAt);
        return new LoyaltyStatement(member.MemberNumber, member.Name, tier, balance, decimal.Round(balance * scheme.ValuePerPoint, 2, MidpointRounding.AwayFromZero), movements);
    }

    private async Task<Result<LoyaltyEarned>> EarnCoreAsync(long companyId, string memberNumber, decimal spend, DateOnly date, string reference, CancellationToken ct)
    {
        Result<(LoyaltyScheme Scheme, LoyaltyMember Member)> found = await FindAsync(companyId, memberNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        (LoyaltyScheme scheme, LoyaltyMember member) = found.Value;
        List<LoyaltyMovement> movements = await _db.LoyaltyMovements.AsNoTracking().Where(m => m.MemberId == member.Id).ToListAsync(ct).ConfigureAwait(false);
        (string? tier, decimal multiplier) = TierOf(scheme, movements, date);
        decimal points = LoyaltyRules.Points(spend, scheme.PointsPerUnit, multiplier);
        if (points == 0m)
        {
            return new LoyaltyEarned(0m, tier, movements.Sum(m => m.Points));
        }

        decimal value = decimal.Round(points * scheme.ValuePerPoint, 2, MidpointRounding.AwayFromZero);
        Result<PostingReceipt> posted = await PostAsync(companyId, date, $"LOY-{(reference ?? string.Empty).Trim()}", $"Points issued to {member.MemberNumber}", scheme.ExpenseAccountCode,
            scheme.LiabilityAccountCode, value, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        _db.LoyaltyMovements.Add(new LoyaltyMovement(companyId, member.Id, LoyaltyMovementKind.Earn, date, points, value, (reference ?? string.Empty).Trim(),
            scheme.ExpiryMonths is int months ? date.AddMonths(months) : null));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LoyaltyEarned(points, tier, movements.Sum(m => m.Points) + points);
    }

    private async Task<Result<(int Members, decimal Points, decimal Value)>> ExpireCoreAsync(long companyId, DateOnly asAt, string documentNumber, CancellationToken ct)
    {
        LoyaltyScheme? scheme = await _db.LoyaltySchemes.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return Error.NotFound("Loyalty.NoScheme", "The company has no loyalty scheme.");
        }

        List<LoyaltyMovement> all = await _db.LoyaltyMovements.AsNoTracking().Where(m => m.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        List<(long Member, decimal Points)> expiring = [.. all.GroupBy(m => m.MemberId).Select(g => (g.Key, LoyaltyRules.Expiring(
            g.Where(m => m.Kind == LoyaltyMovementKind.Earn).Select(m => (m.Points, m.ExpiresOn)), -g.Where(m => m.Kind != LoyaltyMovementKind.Earn).Sum(m => m.Points), asAt)))
            .Where(x => x.Item2 > 0m)];
        decimal points = expiring.Sum(e => e.Points);
        decimal value = decimal.Round(points * scheme.ValuePerPoint, 2, MidpointRounding.AwayFromZero);
        if (points == 0m)
        {
            return (0, 0m, 0m);
        }

        Result<PostingReceipt> posted = await PostAsync(companyId, asAt, documentNumber, "Loyalty points expired", scheme.LiabilityAccountCode, scheme.ExpenseAccountCode, value, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        foreach ((long member, decimal memberPoints) in expiring)
        {
            _db.LoyaltyMovements.Add(new LoyaltyMovement(companyId, member, LoyaltyMovementKind.Expire, asAt, -memberPoints,
                -decimal.Round(memberPoints * scheme.ValuePerPoint, 2, MidpointRounding.AwayFromZero), documentNumber, null));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (expiring.Count, points, value);
    }

    private static (string? Tier, decimal Multiplier) TierOf(LoyaltyScheme scheme, IEnumerable<LoyaltyMovement> movements, DateOnly asAt)
        => LoyaltyRules.Tier(movements.Where(m => m.Kind == LoyaltyMovementKind.Earn && m.Date > asAt.AddMonths(-12) && m.Date <= asAt).Sum(m => m.Points),
            scheme.Tiers.Select(t => (t.Name, t.MinPoints, t.Multiplier)));

    private async Task<decimal> BalanceAsync(long memberId, CancellationToken ct)
        => await _db.LoyaltyMovements.Where(m => m.MemberId == memberId).SumAsync(m => (decimal?)m.Points, ct).ConfigureAwait(false) ?? 0m;

    private async Task<Result<(LoyaltyScheme Scheme, LoyaltyMember Member)>> FindAsync(long companyId, string memberNumber, CancellationToken ct)
    {
        LoyaltyScheme? scheme = await _db.LoyaltySchemes.AsNoTracking().Include(s => s.Tiers).FirstOrDefaultAsync(s => s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return Error.NotFound("Loyalty.NoScheme", "The company has no loyalty scheme.");
        }

        string number = (memberNumber ?? string.Empty).Trim().ToUpperInvariant();
        LoyaltyMember? member = await _db.LoyaltyMembers.FirstOrDefaultAsync(m => m.CompanyId == companyId && m.MemberNumber == number, ct).ConfigureAwait(false);
        return member is null ? Error.NotFound("Loyalty.MemberNotFound", $"No member '{number}'.") : (scheme, member);
    }

    private async Task<Result<PostingReceipt>> PostAsync(long companyId, DateOnly date, string document, string narrative, string debit, string credit, decimal amount, CancellationToken ct)
    {
        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        return await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = company.BaseCurrency, DocumentType = "LOYALTY", DocumentNumber = document, Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
    }
}
