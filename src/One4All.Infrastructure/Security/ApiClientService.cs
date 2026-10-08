using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Security;

/// <summary>API client credentials: issued, rotated, revoked and checked; only the secret's SHA-256 is stored.</summary>
public sealed class ApiClientService : IApiClientService
{
    private readonly One4AllDbContext _db;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="clock">The clock.</param>
    public ApiClientService(One4AllDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<ApiCredential>> CreateAsync(string name, string actsAsUserName, DateOnly? expiresOn, int? requestsPerMinute, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || requestsPerMinute is <= 0)
        {
            return Error.Validation("Api.Client", "A client needs a name, and a rate limit (if any) above zero.");
        }

        string normalized = (actsAsUserName ?? string.Empty).Trim().ToUpperInvariant();
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, ct).ConfigureAwait(false);
        if (user is not { IsActive: true })
        {
            return Error.NotFound("Api.User", $"No active user {actsAsUserName} for the client to act as.");
        }

        string clientId = "o4a_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        string secret = Secret();
        _db.ApiClients.Add(new ApiClient(name.Trim(), clientId, Hash(secret), user.Id, expiresOn, requestsPerMinute));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ApiCredential(clientId, secret, $"{clientId}.{secret}");
    }

    /// <inheritdoc />
    public async Task<Result<ApiCredential>> RotateAsync(string clientId, CancellationToken ct = default)
    {
        ApiClient? client = await _db.ApiClients.FirstOrDefaultAsync(c => c.ClientId == clientId, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound("Api.ClientNotFound", $"No API client {clientId}.");
        }

        string secret = Secret();
        client.Rotate(Hash(secret));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ApiCredential(client.ClientId, secret, $"{client.ClientId}.{secret}");
    }

    /// <inheritdoc />
    public async Task<Result> RevokeAsync(string clientId, CancellationToken ct = default)
    {
        ApiClient? client = await _db.ApiClients.FirstOrDefaultAsync(c => c.ClientId == clientId, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Result.Failure(Error.NotFound("Api.ClientNotFound", $"No API client {clientId}."));
        }

        client.IsActive = false;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiClientRow>> ListAsync(CancellationToken ct = default)
        => await (from c in _db.ApiClients.AsNoTracking()
                  join u in _db.Users.AsNoTracking() on c.ActsAsUserId equals u.Id
                  orderby c.Name
                  select new ApiClientRow(c.ClientId, c.Name, u.Username, c.IsActive, c.ExpiresOn, c.LastUsedOnUtc, c.RequestsPerMinute)).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<ApiClientIdentity>> AuthenticateAsync(string apiKey, CancellationToken ct = default)
    {
        Error refused = Error.Forbidden("Api.KeyInvalid", "The API key is not valid.");
        string key = (apiKey ?? string.Empty).Trim();
        int dot = key.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == key.Length - 1)
        {
            return refused;
        }

        string clientId = key[..dot];
        ApiClient? client = await _db.ApiClients.FirstOrDefaultAsync(c => c.ClientId == clientId, ct).ConfigureAwait(false);
        DateTimeOffset now = _clock.UtcNow;
        if (client is not { IsActive: true } || (client.ExpiresOn is DateOnly expires && expires < DateOnly.FromDateTime(now.UtcDateTime))
            || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(client.SecretHash), Convert.FromHexString(Hash(key[(dot + 1)..]))))
        {
            return refused;
        }

        User? user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == client.ActsAsUserId, ct).ConfigureAwait(false);
        if (user is not { IsActive: true })
        {
            return refused;
        }

        // Recording every call would write on every request: once a minute is enough to show the client is in use.
        if (client.LastUsedOnUtc is not DateTimeOffset last || now - last > TimeSpan.FromMinutes(1))
        {
            client.LastUsedOnUtc = now;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new ApiClientIdentity(client.ClientId, user.Id, user.Username, client.RequestsPerMinute);
    }

    private static string Secret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(secret)));
}
