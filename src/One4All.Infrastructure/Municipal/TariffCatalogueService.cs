using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>The tariff catalogue: lists and resolves tariffs by consumer category and ward.</summary>
public sealed class TariffCatalogueService : ITariffCatalogueService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public TariffCatalogueService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogueTariff>> ListAsync(long companyId, ServiceType serviceType, DateOnly date, CancellationToken ct = default)
        => await _db.Tariffs.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.ServiceType == serviceType && t.IsActive && t.EffectiveFrom <= date)
            .OrderByDescending(t => t.EffectiveFrom).ThenBy(t => t.Code)
            .Select(t => new CatalogueTariff(t.Id, t.Code, t.Name, t.Category, t.Ward, t.EffectiveFrom))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<CatalogueTariff>> ResolveAsync(long companyId, ServiceType serviceType, ConsumerCategory category, string? ward, DateOnly date, CancellationToken ct = default)
    {
        List<CatalogueTariff> catalogue = [.. await ListAsync(companyId, serviceType, date, ct).ConfigureAwait(false)];
        TariffCandidate? best = TariffCatalogue.Best(
            catalogue.Select(t => new TariffCandidate(t.TariffId, t.Code, t.Category, t.Ward, t.EffectiveFrom)), category, ward, date);
        if (best is not TariffCandidate match)
        {
            return Error.NotFound("Mun.NoTariff", $"No active {serviceType} tariff for a {category} consumer in {ward ?? "any ward"} on {date:yyyy-MM-dd}.");
        }

        return catalogue.First(t => t.TariffId == match.TariffId);
    }
}
