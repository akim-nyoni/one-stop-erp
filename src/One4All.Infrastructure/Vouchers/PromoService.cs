using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Vouchers;
using One4All.Application.Vouchers;
using One4All.Core.Common;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Vouchers;

/// <summary>Promotion campaigns, codes and redemptions.</summary>
public sealed class PromoService : IPromoService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PromoService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> CreateCampaignAsync(long companyId, PromoCampaignRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<string> codes = [.. request.Codes.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0)];
        if (string.IsNullOrWhiteSpace(request.Code) || request.ValidTo < request.ValidFrom || request.DiscountValue <= 0m || request.UsesPerCode < 1
            || (request.DiscountType == PromoDiscountType.Percent && request.DiscountValue > 100m) || codes.Count == 0 || request.MaxRedemptions < 1)
        {
            return Error.Validation("Promo.Invalid", "A campaign needs a code, a validity window, a positive discount (at most 100%), at least one use per code and some codes.");
        }

        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count
            || await _db.Set<PromoCampaign>().Where(c => c.CompanyId == companyId).SelectMany(c => c.Codes).AnyAsync(c => codes.Contains(c.Code), ct).ConfigureAwait(false))
        {
            return Error.Conflict("Promo.DuplicateCode", "Every promotion code must be unique.");
        }

        PromoCampaign campaign = new(companyId, request.Code, request.Name, request.DiscountType, request.DiscountValue, request.ValidFrom, request.ValidTo,
            request.MinimumSpend, request.UsesPerCode, request.MaxRedemptions)
        {
            CustomerTypes = string.IsNullOrWhiteSpace(request.CustomerTypes) ? null : request.CustomerTypes.Trim(),
            ItemCategories = string.IsNullOrWhiteSpace(request.ItemCategories) ? null : request.ItemCategories.Trim(),
        };
        foreach (string code in codes)
        {
            campaign.AddCode(code);
        }

        _db.Set<PromoCampaign>().Add(campaign);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return campaign.Id;
    }

    /// <inheritdoc />
    public async Task<Result<PromoQuote>> QuoteAsync(long companyId, string code, DateOnly date, decimal spend, PromoEligibility? eligibility = null, CancellationToken ct = default)
    {
        Result<(PromoCampaign Campaign, PromoCode Code)> found = await FindAsync(companyId, code, tracked: false, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        return Check(found.Value.Campaign, found.Value.Code, date, spend, eligibility);
    }

    /// <inheritdoc />
    public Task<Result<PromoQuote>> RedeemAsync(long companyId, string code, DateOnly date, decimal spend, string reference, PromoEligibility? eligibility = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => RedeemCoreAsync(companyId, code, date, spend, reference, eligibility, ct), ct);

    // One unit, with the campaign locked so concurrent redemptions cannot overrun its caps.
    private async Task<Result<PromoQuote>> RedeemCoreAsync(long companyId, string code, DateOnly date, decimal spend, string reference, PromoEligibility? eligibility, CancellationToken ct)
    {
        string normalised = code.Trim().ToUpperInvariant();
        long? campaignId = await _db.Set<PromoCampaign>().Where(c => c.CompanyId == companyId && c.Codes.Any(p => p.Code == normalised))
            .Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (campaignId is long id)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM vou.promo_campaign WHERE id = {id} FOR UPDATE", ct).ConfigureAwait(false);
        }

        Result<(PromoCampaign Campaign, PromoCode Code)> found = await FindAsync(companyId, code, tracked: true, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        (PromoCampaign campaign, PromoCode promo) = found.Value;
        Result<PromoQuote> quote = Check(campaign, promo, date, spend, eligibility);
        if (quote.IsFailure)
        {
            return quote.Error;
        }

        campaign.Redeem(promo);
        _db.Set<PromoRedemption>().Add(new PromoRedemption(companyId, campaign.Id, promo.Id, date, spend, quote.Value.Discount, reference.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return quote.Value;
    }

    /// <inheritdoc />
    public async Task<Result<PromoAnalysis>> AnalysisAsync(long companyId, long campaignId, CancellationToken ct = default)
    {
        PromoCampaign? campaign = await _db.Set<PromoCampaign>().AsNoTracking().Include(c => c.Codes)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (campaign is null)
        {
            return Error.NotFound("Promo.CampaignNotFound", $"Campaign {campaignId} not found.");
        }

        var totals = await _db.Set<PromoRedemption>().Where(r => r.PromoCampaignId == campaignId)
            .GroupBy(r => 1).Select(g => new { Count = g.Count(), Spend = g.Sum(r => r.Spend), Discount = g.Sum(r => r.Discount) })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new PromoAnalysis(campaign.Code, campaign.Codes.Count, campaign.Codes.Count(c => c.TimesUsed > 0), totals?.Count ?? 0, totals?.Spend ?? 0m, totals?.Discount ?? 0m);
    }

    private static Result<PromoQuote> Check(PromoCampaign campaign, PromoCode code, DateOnly date, decimal spend, PromoEligibility? eligibility)
    {
        if (!campaign.IsFor(eligibility?.CustomerType))
        {
            return Error.Rule("Promo.CustomerNotEligible", $"Code {code.Code} is only for {campaign.CustomerTypes} customers.");
        }

        if (campaign.ItemCategories is not null)
        {
            // Limited to categories: the spend that counts (for the minimum and the discount) is the covered lines only.
            if (eligibility is null || eligibility.Lines.Count == 0)
            {
                return Error.Validation("Promo.BasketNeeded", $"Code {code.Code} applies to {campaign.ItemCategories} only; give the basket lines.");
            }

            spend = eligibility.Lines.Where(l => campaign.Covers(l.Category)).Sum(l => l.Amount);
            if (spend <= 0m)
            {
                return Error.Rule("Promo.NoEligibleItems", $"Code {code.Code} applies to {campaign.ItemCategories} only, and the basket has none.");
            }
        }

        if (!campaign.IsActive)
        {
            return Error.Rule("Promo.Inactive", $"Campaign {campaign.Code} is not running.");
        }

        if (date < campaign.ValidFrom || date > campaign.ValidTo)
        {
            return Error.Rule("Promo.OutOfDates", $"Code {code.Code} is valid from {campaign.ValidFrom:yyyy-MM-dd} to {campaign.ValidTo:yyyy-MM-dd}.");
        }

        if (spend < campaign.MinimumSpend)
        {
            return Error.Rule("Promo.MinimumSpend", $"Code {code.Code} needs a spend of at least {campaign.MinimumSpend:0.00}.");
        }

        if (code.TimesUsed >= campaign.UsesPerCode)
        {
            return Error.Rule("Promo.CodeUsed", $"Code {code.Code} has already been used.");
        }

        if (campaign.MaxRedemptions is int cap && campaign.Redemptions >= cap)
        {
            return Error.Rule("Promo.CampaignExhausted", $"Campaign {campaign.Code} has reached its {cap} redemptions.");
        }

        return new PromoQuote(code.Code, campaign.Code, PromoPricing.Discount(campaign.DiscountType, campaign.DiscountValue, spend));
    }

    private async Task<Result<(PromoCampaign Campaign, PromoCode Code)>> FindAsync(long companyId, string code, bool tracked, CancellationToken ct)
    {
        string normalised = code.Trim().ToUpperInvariant();
        IQueryable<PromoCampaign> campaigns = _db.Set<PromoCampaign>().Include(c => c.Codes);
        if (!tracked)
        {
            campaigns = campaigns.AsNoTracking();
        }

        PromoCampaign? campaign = await campaigns.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Codes.Any(p => p.Code == normalised), ct).ConfigureAwait(false);
        PromoCode? promo = campaign?.Codes.FirstOrDefault(p => p.Code == normalised);
        if (campaign is null || promo is null)
        {
            return Error.NotFound("Promo.Unknown", $"There is no promotion code {normalised}.");
        }

        return (campaign, promo);
    }
}
