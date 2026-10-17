using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>
/// A company's loyalty scheme (<c>pos.loyalty_scheme</c>, brief §4.1): points earned per currency unit spent, what a point is worth when
/// redeemed, how long points last, the tiers (earn multipliers by points earned in the last twelve months) and the GL accounts - the
/// value of unredeemed points is a liability, its cost an expense.
/// </summary>
public sealed class LoyaltyScheme : Entity, ICompanyScoped
{
    private readonly List<LoyaltyTier> _tiers = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LoyaltyScheme()
    {
        LiabilityAccountCode = string.Empty;
        ExpenseAccountCode = string.Empty;
    }

    /// <summary>Creates the scheme.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="liabilityAccountCode">The points liability account.</param>
    /// <param name="expenseAccountCode">The loyalty cost account.</param>
    public LoyaltyScheme(long companyId, string liabilityAccountCode, string expenseAccountCode)
    {
        CompanyId = companyId;
        LiabilityAccountCode = liabilityAccountCode;
        ExpenseAccountCode = expenseAccountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets or sets the points earned per currency unit spent.</summary>
    public decimal PointsPerUnit { get; set; }

    /// <summary>Gets or sets what a point is worth when redeemed.</summary>
    public decimal ValuePerPoint { get; set; }

    /// <summary>Gets or sets how many months points last (none = they never expire).</summary>
    public int? ExpiryMonths { get; set; }

    /// <summary>Gets or sets the points liability account.</summary>
    public string LiabilityAccountCode { get; set; }

    /// <summary>Gets or sets the loyalty cost account.</summary>
    public string ExpenseAccountCode { get; set; }

    /// <summary>The tiers, lowest first.</summary>
    public IReadOnlyList<LoyaltyTier> Tiers => [.. _tiers.OrderBy(t => t.MinPoints)];

    /// <summary>Replaces the tiers.</summary>
    /// <param name="tiers">Name, the points earned in twelve months it needs, and its earn multiplier.</param>
    public void SetTiers(IEnumerable<(string Name, decimal MinPoints, decimal Multiplier)> tiers)
    {
        _tiers.Clear();
        _tiers.AddRange(tiers.Select(t => new LoyaltyTier(t.Name, t.MinPoints, t.Multiplier)));
    }
}

/// <summary>A loyalty tier.</summary>
public sealed class LoyaltyTier : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LoyaltyTier() => Name = string.Empty;

    /// <summary>Creates the tier.</summary>
    /// <param name="name">The name.</param>
    /// <param name="minPoints">The points earned in the last twelve months it needs.</param>
    /// <param name="multiplier">The earn multiplier.</param>
    public LoyaltyTier(string name, decimal minPoints, decimal multiplier)
    {
        Name = name;
        MinPoints = minPoints;
        Multiplier = multiplier;
    }

    /// <summary>The owning scheme.</summary>
    public long LoyaltySchemeId { get; private set; }

    /// <summary>The name.</summary>
    public string Name { get; private set; }

    /// <summary>The points earned in the last twelve months it needs.</summary>
    public decimal MinPoints { get; private set; }

    /// <summary>The earn multiplier.</summary>
    public decimal Multiplier { get; private set; }
}

/// <summary>A loyalty member (<c>pos.loyalty_member</c>).</summary>
public sealed class LoyaltyMember : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LoyaltyMember()
    {
        MemberNumber = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates the member.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="memberNumber">The card / member number.</param>
    /// <param name="name">The name.</param>
    /// <param name="joinedOn">When they joined.</param>
    public LoyaltyMember(long companyId, string memberNumber, string name, DateOnly joinedOn)
    {
        CompanyId = companyId;
        MemberNumber = memberNumber.Trim().ToUpperInvariant();
        Name = name;
        JoinedOn = joinedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The card / member number.</summary>
    public string MemberNumber { get; private set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the customer account, when the member has one.</summary>
    public long? CustomerId { get; set; }

    /// <summary>When they joined.</summary>
    public DateOnly JoinedOn { get; private set; }
}

/// <summary>What a loyalty movement is.</summary>
public enum LoyaltyMovementKind
{
    /// <summary>Points earned on a purchase.</summary>
    Earn = 1,

    /// <summary>Points redeemed.</summary>
    Redeem = 2,

    /// <summary>Points that passed their expiry unused.</summary>
    Expire = 3,
}

/// <summary>A movement on a member's points (<c>pos.loyalty_movement</c>): earn (positive), redeem and expire (negative).</summary>
public sealed class LoyaltyMovement : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LoyaltyMovement() => Reference = string.Empty;

    /// <summary>Creates the movement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="memberId">The member.</param>
    /// <param name="kind">What it is.</param>
    /// <param name="date">The date.</param>
    /// <param name="points">The points (positive earned, negative used).</param>
    /// <param name="value">Their value at the scheme's rate.</param>
    /// <param name="reference">The sale or document.</param>
    /// <param name="expiresOn">When earned points expire.</param>
    public LoyaltyMovement(long companyId, long memberId, LoyaltyMovementKind kind, DateOnly date, decimal points, decimal value, string reference, DateOnly? expiresOn)
    {
        CompanyId = companyId;
        MemberId = memberId;
        Kind = kind;
        Date = date;
        Points = points;
        Value = value;
        Reference = reference;
        ExpiresOn = expiresOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The member.</summary>
    public long MemberId { get; private set; }

    /// <summary>What it is.</summary>
    public LoyaltyMovementKind Kind { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The points (positive earned, negative used).</summary>
    public decimal Points { get; private set; }

    /// <summary>Their value at the scheme's rate.</summary>
    public decimal Value { get; private set; }

    /// <summary>The sale or document.</summary>
    public string Reference { get; private set; }

    /// <summary>When earned points expire.</summary>
    public DateOnly? ExpiresOn { get; private set; }
}
