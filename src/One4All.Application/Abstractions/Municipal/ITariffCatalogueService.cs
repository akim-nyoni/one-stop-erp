using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A tariff in the catalogue.</summary>
/// <param name="TariffId">The tariff.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Category">The consumer category it applies to.</param>
/// <param name="Ward">The ward it applies to, or null for the whole council.</param>
/// <param name="EffectiveFrom">When it takes effect.</param>
public sealed record CatalogueTariff(long TariffId, string Code, string Name, ConsumerCategory Category, string? Ward, DateOnly EffectiveFrom);

/// <summary>
/// The tariff catalogue (brief §4.9: tariffs vary by consumer category and ward): lists the tariffs offered for a
/// service and resolves the one that applies to a consumer, so services are linked to the right tariff. Billing still
/// uses the tariff a service is linked to; this chooses it.
/// </summary>
public interface ITariffCatalogueService
{
    /// <summary>The active tariffs in force for a service on a date, newest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="serviceType">The service.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The catalogue.</returns>
    Task<IReadOnlyList<CatalogueTariff>> ListAsync(long companyId, ServiceType serviceType, DateOnly date, CancellationToken ct = default);

    /// <summary>Resolves the tariff that applies to a consumer of a service, by category and ward.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="serviceType">The service.</param>
    /// <param name="category">The consumer's category.</param>
    /// <param name="ward">The consumer's ward.</param>
    /// <param name="date">The date it is needed for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tariff, or why none applies.</returns>
    Task<Result<CatalogueTariff>> ResolveAsync(long companyId, ServiceType serviceType, ConsumerCategory category, string? ward, DateOnly date, CancellationToken ct = default);
}
