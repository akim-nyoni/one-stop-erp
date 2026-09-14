using One4All.Licensing.Core;

namespace One4All.Application.Abstractions.Licensing;

/// <summary>
/// Provides the installed licence's validated status and gates modules, features and limits (brief §11.5).
/// The result is validated at startup and cached in memory; <see cref="Reload"/> re-reads after a new
/// licence is installed. Menu visibility, API authorisation and job scheduling all consult this.
/// </summary>
public interface ILicenceService
{
    /// <summary>The current validation result (never null; an absent/invalid licence yields a failed result).</summary>
    LicenseValidationResult Current { get; }

    /// <summary>True when a module is licensed and not expired.</summary>
    /// <param name="moduleCode">The module code (e.g. GL, POS, MUNI, HSE, BUD, PAY).</param>
    /// <returns>True when licensed.</returns>
    bool IsModuleLicensed(string moduleCode);

    /// <summary>Returns a global limit, or null when unlimited/undefined.</summary>
    /// <param name="key">The limit key.</param>
    /// <returns>The limit, or null.</returns>
    int? GetLimit(string key);

    /// <summary>True when a feature flag is enabled.</summary>
    /// <param name="feature">The feature flag.</param>
    /// <returns>True when enabled.</returns>
    bool HasFeature(string feature);

    /// <summary>Installs a new licence token (validates and persists it), then reloads.</summary>
    /// <param name="licenceToken">The compact licence token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The validation result of the newly installed licence.</returns>
    Task<LicenseValidationResult> InstallAsync(string licenceToken, CancellationToken ct = default);

    /// <summary>Re-reads and re-validates the installed licence.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task ReloadAsync(CancellationToken ct = default);
}
