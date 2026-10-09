using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Pos;

/// <summary>Promotions as data, applied to a basket by the pure <see cref="PromotionEngine"/> (brief §4.1).</summary>
public sealed class PromotionService : IPromotionService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PromotionService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> SaveAsync(PromotionRequest request, CancellationToken ct = default)
    {
        string code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        string? problem = code.Length == 0 || string.IsNullOrWhiteSpace(request.DiscountAccountCode) ? "a code and a discount account"
            : request.Kind switch
            {
                PromotionKind.NForPrice when request.Quantity < 2 || request.Price is not > 0m || request.Items.Count == 0 => "N of at least 2, a price and the items",
                PromotionKind.BuyXGetY when request.Quantity < 1 || request.RewardQuantity < 1 || request.Percent is not (> 0m and <= 100m) || request.Items.Count == 0 => "X, Y, a percentage up to 100 and the items",
                PromotionKind.PercentOff when request.Percent is not (> 0m and <= 100m) || (request.Items.Count == 0 && string.IsNullOrWhiteSpace(request.Category)) => "a percentage up to 100 and the items or a category",
                PromotionKind.MealDeal when request.Courses.Count < 2 || request.Courses.Any(c => c.Count == 0) || request.Price is not > 0m => "at least two courses with items and a price",
                _ => null,
            };
        if (problem is not null || request.EndsOn < request.StartsOn)
        {
            return Error.Validation("Pos.PromotionInvalid", $"A {request.Kind} promotion needs {problem ?? "an end on or after its start"}.");
        }

        List<string> codes = [.. request.Items.Concat(request.RewardItems).Concat(request.Courses.SelectMany(c => c)).Select(c => c.Trim().ToUpperInvariant()).Distinct()];
        Dictionary<string, long> items = await _db.Items.Where(i => i.CompanyId == request.CompanyId && codes.Contains(i.Code.ToUpper())).ToDictionaryAsync(i => i.Code.ToUpper(), i => i.Id, ct)
            .ConfigureAwait(false);
        if (codes.FirstOrDefault(c => !items.ContainsKey(c)) is string missing)
        {
            return Error.NotFound("Pos.ItemNotFound", $"No item '{missing}'.");
        }

        Promotion? promotion = await _db.Promotions.Include(p => p.Items).FirstOrDefaultAsync(p => p.CompanyId == request.CompanyId && p.Code == code, ct).ConfigureAwait(false);
        if (promotion is null)
        {
            promotion = new Promotion(request.CompanyId, code, request.Name, request.Kind, request.DiscountAccountCode);
            _db.Promotions.Add(promotion);
        }
        else if (promotion.Kind != request.Kind)
        {
            return Error.Conflict("Pos.PromotionKind", $"Promotion {code} is a {promotion.Kind}; use a new code for a {request.Kind}.");
        }

        promotion.Name = request.Name;
        promotion.DiscountAccountCode = request.DiscountAccountCode;
        promotion.Quantity = request.Quantity;
        promotion.RewardQuantity = request.RewardQuantity;
        promotion.Price = request.Price;
        promotion.Percent = request.Percent;
        promotion.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        promotion.Priority = request.Priority;
        promotion.Compounding = request.Compounding;
        promotion.MemberOnly = request.MemberOnly;
        promotion.CustomerTypes = request.CustomerTypes.Count == 0 ? null : string.Join(",", request.CustomerTypes.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        promotion.CouponCode = string.IsNullOrWhiteSpace(request.CouponCode) ? null : request.CouponCode.Trim().ToUpperInvariant();
        promotion.StartsOn = request.StartsOn;
        promotion.EndsOn = request.EndsOn;
        promotion.DailyFrom = request.DailyFrom;
        promotion.DailyTo = request.DailyTo;
        promotion.BranchId = request.BranchId;
        long Id(string c) => items[c.Trim().ToUpperInvariant()];
        promotion.SetItems([.. request.Items.Select(c => (Id(c), 0)), .. request.RewardItems.Select(c => (Id(c), 1)),
            .. request.Courses.SelectMany((course, index) => course.Select(c => (Id(c), index + 1)))]);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return promotion.Id;
    }

    /// <inheritdoc />
    public async Task<BasketPricing> PriceBasketAsync(BasketRequest request, CancellationToken ct = default)
    {
        DateOnly date = DateOnly.FromDateTime(request.At);
        TimeOnly time = TimeOnly.FromDateTime(request.At);
        HashSet<string> coupons = new(request.Coupons.Select(c => c.Trim().ToUpperInvariant()));
        List<Promotion> running = [.. (await _db.Promotions.AsNoTracking().Include(p => p.Items)
                .Where(p => p.CompanyId == request.CompanyId && p.IsActive && p.StartsOn <= date && (p.EndsOn == null || p.EndsOn >= date) && (p.BranchId == null || p.BranchId == request.BranchId))
                .ToListAsync(ct).ConfigureAwait(false))
            .Where(p => (!p.MemberOnly || request.IsMember) && p.IsFor(request.CustomerType) && (p.CouponCode is null || coupons.Contains(p.CouponCode)) && InWindow(p, time))];

        List<string> categories = [.. running.Where(p => p.Category is not null).Select(p => p.Category!)];
        List<long> basketItems = [.. request.Lines.Select(l => l.ItemId).Distinct()];
        Dictionary<long, string?> itemCategory = await _db.Items.Where(i => basketItems.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Category, ct).ConfigureAwait(false);
        List<PromotionRule> rules = [.. running.Select(p => new PromotionRule(p.Code, p.Kind, p.Priority, p.Compounding, p.Quantity, p.RewardQuantity, p.Price, p.Percent,
            new HashSet<long>(p.Items.Where(i => i.Group == 0).Select(i => i.ItemId)
                .Concat(p.Category is null ? [] : itemCategory.Where(c => string.Equals(c.Value, p.Category, StringComparison.OrdinalIgnoreCase)).Select(c => c.Key))),
            p.Kind == PromotionKind.BuyXGetY ? new HashSet<long>(p.Items.Where(i => i.Group == 1).Select(i => i.ItemId)) : [],
            p.Kind == PromotionKind.MealDeal ? [.. p.Items.Where(i => i.Group > 0).GroupBy(i => i.Group).OrderBy(g => g.Key).Select(g => (IReadOnlySet<long>)new HashSet<long>(g.Select(i => i.ItemId)))] : [],
            p.DiscountAccountCode))];

        IReadOnlyList<LineDiscount> discounts = PromotionEngine.Apply(request.Lines.Select((l, i) => new BasketLine(i, l.ItemId, l.Quantity, l.UnitPrice)), rules);
        List<PricedLine> lines = [.. request.Lines.Select((l, i) =>
        {
            List<LineDiscount> mine = [.. discounts.Where(d => d.Line == i)];
            return new PricedLine(l.ItemId, l.Quantity, decimal.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero), mine.Sum(d => d.Amount), [.. mine.Select(d => d.PromotionCode)],
                mine.FirstOrDefault()?.DiscountAccountCode);
        })];
        Dictionary<string, string> names = running.ToDictionary(p => p.Code, p => p.Name);
        return new BasketPricing(lines, [.. discounts.GroupBy(d => d.PromotionCode).Select(g => (g.Key, names[g.Key], g.Sum(d => d.Amount)))]);
    }

    // A happy hour may run across midnight (e.g. 22:00 to 02:00).
    private static bool InWindow(Promotion p, TimeOnly time) => (p.DailyFrom, p.DailyTo) switch
    {
        (TimeOnly from, TimeOnly to) when from <= to => time >= from && time < to,
        (TimeOnly from, TimeOnly to) => time >= from || time < to,
        _ => true,
    };
}
