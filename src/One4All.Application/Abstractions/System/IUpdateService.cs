using One4All.Core.Common;
using One4All.Licensing.Core;

namespace One4All.Application.Abstractions.System;

/// <summary>What the update channel says.</summary>
/// <param name="Status">Up to date, available or required.</param>
/// <param name="CurrentVersion">The running version.</param>
/// <param name="Manifest">The release on the channel, or null.</param>
/// <param name="Problem">Why the channel could not be read or trusted, or null.</param>
public sealed record UpdateCheck(UpdateStatus Status, string CurrentVersion, UpdateManifest? Manifest, string? Problem);

/// <summary>
/// The auto-update channel (brief §2 packaging): reads the vendor-signed manifest from the configured channel (a URL or a file on the network share),
/// decides whether this installation should update, and downloads the installer, checking its SHA-256 before it may run.
/// </summary>
public interface IUpdateService
{
    /// <summary>Reads the channel.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The check.</returns>
    Task<UpdateCheck> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads a release's installer and checks it is the one the manifest names.</summary>
    /// <param name="manifest">The release.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The installer's local path, or why it cannot be used.</returns>
    Task<Result<string>> DownloadAsync(UpdateManifest manifest, CancellationToken ct = default);
}

/// <summary>Where the update channel is.</summary>
public sealed class UpdateOptions
{
    /// <summary>Gets or sets the manifest's location (https URL or file path); blank: updates are not checked.</summary>
    public string? Channel { get; set; } = Environment.GetEnvironmentVariable("ONE4ALL_UPDATE_CHANNEL");

    /// <summary>Gets or sets the running version (defaults to the application's).</summary>
    public Version? CurrentVersion { get; set; }
}
