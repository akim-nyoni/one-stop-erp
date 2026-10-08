using One4All.Core.Common;
using One4All.Core.System;

namespace One4All.Application.Abstractions.System;

/// <summary>A requested master-data change as listed.</summary>
/// <param name="Reference">The workflow reference.</param>
/// <param name="Target">The kind of record.</param>
/// <param name="Key">The record's code.</param>
/// <param name="Field">The field.</param>
/// <param name="OldValue">The value before.</param>
/// <param name="NewValue">The value asked for.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="RequestedOnUtc">When it was asked for.</param>
public sealed record MasterDataChangeRow(string Reference, MasterDataTarget Target, string Key, string Field, string? OldValue, string? NewValue, MasterDataChangeStatus Status,
    DateTimeOffset RequestedOnUtc);

/// <summary>
/// Changes to tariffs and master records put through approval (brief §7): a change waits for the company's <c>MasterDataChange</c> workflow when one is
/// defined and is applied only on approval; without one it is applied at once — either way with the value before and after on record.
/// </summary>
public interface IMasterDataChangeService
{
    /// <summary>The fields each kind of record allows changing this way.</summary>
    /// <param name="target">The kind of record.</param>
    /// <returns>The field names.</returns>
    IReadOnlyList<string> Fields(MasterDataTarget target);

    /// <summary>Requests a change.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="target">The kind of record.</param>
    /// <param name="key">The record's code.</param>
    /// <param name="field">The field.</param>
    /// <param name="newValue">The new value (blank clears an optional field).</param>
    /// <param name="userId">Who asks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The change as it now stands (pending or applied).</returns>
    Task<Result<MasterDataChangeRow>> RequestAsync(long companyId, MasterDataTarget target, string key, string field, string? newValue, long userId, CancellationToken ct = default);

    /// <summary>The recent changes, newest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The changes.</returns>
    Task<IReadOnlyList<MasterDataChangeRow>> ListAsync(long companyId, CancellationToken ct = default);
}
