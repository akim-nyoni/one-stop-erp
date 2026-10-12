using System.Reflection;
using System.Security.Cryptography;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.System;
using One4All.Core.Common;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.SystemServices;

/// <summary>The auto-update channel: a vendor-signed manifest and a SHA-256-checked installer.</summary>
public sealed class UpdateService : IUpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly UpdateOptions _options;
    private readonly ITrustedLicenceKeys _keys;

    /// <summary>Creates the service.</summary>
    /// <param name="options">Where the channel is.</param>
    /// <param name="keys">The vendor's trusted public keys.</param>
    public UpdateService(UpdateOptions options, ITrustedLicenceKeys keys)
    {
        _options = options;
        _keys = keys;
    }

    private Version Current => _options.CurrentVersion ?? Assembly.GetEntryAssembly()?.GetName().Version ?? typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);

    /// <inheritdoc />
    public async Task<UpdateCheck> CheckAsync(CancellationToken ct = default)
    {
        string current = Current.ToString(3);
        if (string.IsNullOrWhiteSpace(_options.Channel))
        {
            return new UpdateCheck(UpdateStatus.UpToDate, current, null, "No update channel is configured (Updates:Channel).");
        }

        string token;
        try
        {
            token = await ReadTextAsync(_options.Channel.Trim(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or TaskCanceledException)
        {
            return new UpdateCheck(UpdateStatus.UpToDate, current, null, $"The update channel cannot be read: {ex.Message}");
        }

        (UpdateManifest? manifest, string? error) = UpdateManifestCodec.Verify(token, _keys.PublicKeysByKid);
        return manifest is null
            ? new UpdateCheck(UpdateStatus.UpToDate, current, null, error)
            : new UpdateCheck(UpdateManifestCodec.Decide(Current, manifest), current, manifest, null);
    }

    /// <inheritdoc />
    public async Task<Result<string>> DownloadAsync(UpdateManifest manifest, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string folder = Path.Combine(Path.GetTempPath(), "One4All-update");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, $"One4All-{manifest.Version}-setup.exe");
        try
        {
            if (Uri.TryCreate(manifest.InstallerLocation, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                await using Stream source = await Http.GetStreamAsync(uri, ct).ConfigureAwait(false);
                await using FileStream file = File.Create(target);
                await source.CopyToAsync(file, ct).ConfigureAwait(false);
            }
            else
            {
                File.Copy(manifest.InstallerLocation, target, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or TaskCanceledException)
        {
            return Error.Failure("Sys.UpdateDownload", $"The installer could not be downloaded: {ex.Message}");
        }

        string hash;
        await using (FileStream check = File.OpenRead(target))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(check, ct).ConfigureAwait(false));
        }

        if (!string.Equals(hash, manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            return Error.Rule("Sys.UpdateTampered", "The downloaded installer is not the one the vendor signed; it was deleted and will not run.");
        }

        return target;
    }

    private static async Task<string> ReadTextAsync(string location, CancellationToken ct)
        => Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? await Http.GetStringAsync(uri, ct).ConfigureAwait(false)
            : await File.ReadAllTextAsync(location, ct).ConfigureAwait(false);
}
