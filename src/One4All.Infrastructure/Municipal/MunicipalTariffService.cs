using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <inheritdoc cref="IMunicipalTariffService" />
public sealed class MunicipalTariffService : IMunicipalTariffService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public MunicipalTariffService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> SaveVersionAsync(long companyId, TariffInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        string code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            return Error.Validation("Mun.TariffInvalid", "A tariff needs a code.");
        }

        Tariff? previous = await _db.Tariffs.Include(t => t.Blocks).Include(t => t.Seasons).Where(t => t.CompanyId == companyId && t.Code == code && t.EffectiveFrom <= input.EffectiveFrom)
            .OrderByDescending(t => t.EffectiveFrom).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await _db.Tariffs.Include(t => t.Blocks).Include(t => t.Seasons).Where(t => t.CompanyId == companyId && t.Code == code).OrderBy(t => t.EffectiveFrom).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (previous is not null && previous.EffectiveFrom == input.EffectiveFrom)
        {
            return Error.Conflict("Mun.TariffVersionExists", $"Tariff {code} already has a version from {input.EffectiveFrom:yyyy-MM-dd}; choose another date or withdraw it first.");
        }

        if (previous is null && (string.IsNullOrWhiteSpace(input.Name) || input.ServiceType is null || input.TariffType is null || string.IsNullOrWhiteSpace(input.RevenueAccountCode)))
        {
            return Error.Validation("Mun.TariffInvalid", "A new tariff needs a name, a service, a type and a revenue account.");
        }

        string account = (input.RevenueAccountCode ?? previous!.RevenueAccountCode).Trim().ToUpperInvariant();
        if (!await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == account, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Mun.AccountNotFound", $"Revenue account {account} does not exist.");
        }

        if (new[] { input.FixedCharge, input.PerUnitRate, input.MinimumCharge, input.RatePerValuationCents, input.RatePerSquareMetre }.Any(v => v is < 0m)
            || (input.Blocks?.Any(b => b.FromUnit < 0m || b.Rate < 0m || (b.ToUnit is decimal to && to < b.FromUnit)) ?? false)
            || (input.Seasons?.Any(s => s.FromMonth is < 1 or > 12 || s.ToMonth is < 1 or > 12 || s.PerUnitRate < 0m) ?? false))
        {
            return Error.Validation("Mun.TariffInvalid", "Charges, rates and blocks cannot be negative; a block's top is not below its bottom; season months are 1–12.");
        }

        Tariff version = new(companyId, code, (input.Name ?? previous!.Name).Trim(), input.ServiceType ?? previous!.ServiceType, input.TariffType ?? previous!.TariffType, input.EffectiveFrom, account)
        {
            FixedCharge = input.FixedCharge ?? previous?.FixedCharge ?? 0m, PerUnitRate = input.PerUnitRate ?? previous?.PerUnitRate ?? 0m, MinimumCharge = input.MinimumCharge ?? previous?.MinimumCharge ?? 0m,
            RatePerValuationCents = input.RatePerValuationCents ?? previous?.RatePerValuationCents ?? 0m, RatePerSquareMetre = input.RatePerSquareMetre ?? previous?.RatePerSquareMetre ?? 0m,
            Category = input.Category ?? previous?.Category ?? ConsumerCategory.Domestic, Ward = input.Ward is null ? previous?.Ward : (input.Ward.Trim().Length == 0 ? null : input.Ward.Trim()),
        };
        if (previous is not null)
        {
            version.SetCycle(previous.Cycle, previous.CycleStartMonth, previous.Instalments);
        }

        foreach (TariffBlockInput block in input.Blocks ?? previous?.Blocks.Select(b => new TariffBlockInput(b.FromUnit, b.ToUnit, b.Rate)).ToList() ?? [])
        {
            version.AddBlock(block.FromUnit, block.ToUnit, block.Rate);
        }

        foreach (TariffSeasonInput season in input.Seasons ?? previous?.Seasons.Select(s => new TariffSeasonInput(s.FromMonth, s.ToMonth, s.PerUnitRate)).ToList() ?? [])
        {
            version.AddSeason(season.FromMonth, season.ToMonth, season.PerUnitRate);
        }

        _db.Tariffs.Add(version);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return version.Id;
    }

    /// <inheritdoc />
    public async Task<Result> WithdrawVersionAsync(long companyId, string code, DateOnly effectiveFrom, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        Tariff? version = await _db.Tariffs.FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == key && t.EffectiveFrom == effectiveFrom, ct).ConfigureAwait(false);
        if (version is null)
        {
            return Result.Failure(Error.NotFound("Mun.TariffVersionNotFound", $"Tariff {key} has no version from {effectiveFrom:yyyy-MM-dd}."));
        }

        version.IsActive = false;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
