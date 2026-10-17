using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>What linking did.</summary>
public enum MunicipalLinkOutcome
{
    /// <summary>Nothing: no settings, or a different trigger is configured.</summary>
    NotTriggered = 0,

    /// <summary>A billable property was created with its owner and services.</summary>
    PropertyCreated = 1,

    /// <summary>The stand was already a property; nothing changed (ownership moves by transfer).</summary>
    AlreadyLinked = 2,
}

/// <summary>A link result.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="PropertyId">The property, when there is one.</param>
public sealed record MunicipalLinkResult(MunicipalLinkOutcome Outcome, long? PropertyId);

/// <summary>
/// Housing → municipality (brief §4.10.10): at the configured event - an allocation accepted, a sale or title registration -
/// a stand becomes a billable property on the municipal register, its holder registered as owner and ratepayer and the
/// configured services started from the effective date (so the first bill is pro-rata). The housing services call it at
/// each event; only the configured one acts.
/// </summary>
public interface IHousingMunicipalLinkService
{
    /// <summary>Sets when and how stands are linked.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="trigger">When stands are linked.</param>
    /// <param name="ward">The ward new properties are placed in.</param>
    /// <param name="tariffIds">The tariffs whose services start on linking.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ConfigureAsync(long companyId, MunicipalLinkTrigger trigger, string ward, IReadOnlyList<long> tariffIds, CancellationToken ct = default);

    /// <summary>Links a stand to the municipal register if this event is the configured trigger.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="holderCustomerId">The holder (owner and ratepayer).</param>
    /// <param name="occurred">The event that occurred.</param>
    /// <param name="date">The effective date.</param>
    /// <param name="deedsReference">The deed, when title was registered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened, or why it failed.</returns>
    Task<Result<MunicipalLinkResult>> OnEventAsync(long companyId, long standId, long holderCustomerId, MunicipalLinkTrigger occurred, DateOnly date, string? deedsReference, CancellationToken ct = default);
}
