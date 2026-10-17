using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;
using One4All.Core.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using CoreFingerprint = One4All.Licensing.Core.MachineFingerprint;

namespace One4All.Infrastructure.Licensing;

/// <summary>
/// Loads the installed licence, validates it offline (signature, dates, machine binding, clock rollback)
/// and caches the result in memory (brief §11.5). A singleton so the cached status is shared; database
/// access happens through a scope. Menu visibility, API authorisation and jobs all consult this.
/// </summary>
public sealed class LicenceService : ILicenceService
{
    /// <summary>The product string this ERP validates licences against.</summary>
    public const string Product = "One4All ERP";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LicenseValidator _validator;
    private readonly IClock _clock;
    private readonly Lazy<CoreFingerprint> _fingerprint;
    private readonly bool _enforce;
    private readonly IReadOnlyDictionary<string, byte[]> _trustedKeys;
    private volatile IReadOnlyCollection<string> _revoked = [];
    private volatile LicenseValidationResult _current;

    /// <summary>Creates the service and performs an initial (empty) validation.</summary>
    /// <param name="scopeFactory">Factory used to open a scope for database access.</param>
    /// <param name="trustedKeys">The trusted vendor public keys.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="machineId">Supplies this machine's raw fingerprint components.</param>
    public LicenceService(
        IServiceScopeFactory scopeFactory,
        ITrustedLicenceKeys trustedKeys,
        IClock clock,
        IMachineIdProvider machineId,
        LicensingOptions? options = null)
    {
        _scopeFactory = scopeFactory;
        _enforce = options?.Enforce ?? false;
        _clock = clock;
        // Fingerprint the machine once, lazily: the hardware identity is stable for the process lifetime.
        _fingerprint = new Lazy<CoreFingerprint>(
            () => CoreFingerprint.FromComponents(machineId.GetComponents()),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _trustedKeys = trustedKeys.PublicKeysByKid;
        // Clock rollback: the latest time seen, kept in the database and a file; a clock set back beyond a day is tampering.
        ILicenseHighWaterMarkStore? mark = options?.DetectClockRollback ?? _enforce ? new ClockHighWaterMarkStore(scopeFactory, machineId, options?.ClockMarkPath) : null;
        _validator = new LicenseValidator(new LicenseVerifier(trustedKeys.PublicKeysByKid), clock, Product, mark);
        _current = LicenseValidationResult.Failure(LicenseStatus.Invalid, "No licence installed.", clock);
    }

    /// <inheritdoc />
    public LicenseValidationResult Current => _current;

    /// <inheritdoc />
    public string MachineFingerprint => _fingerprint.Value.ToString();

    /// <inheritdoc />
    public bool IsModuleLicensed(string moduleCode) => _current.IsModuleLicensed(moduleCode);

    /// <inheritdoc />
    public int? GetLimit(string key) => _current.GetLimit(key);

    /// <inheritdoc />
    public bool HasFeature(string feature) => _current.HasFeature(feature);

    /// <inheritdoc />
    public bool Enforced => _enforce;

    /// <inheritdoc />
    public int? DaysUntilExpiry => _current.Payload?.ValidTo is DateOnly to ? to.DayNumber - DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime).DayNumber : null;

    /// <inheritdoc />
    public string? Warning => _current.Status switch
    {
        LicenseStatus.Grace => $"The licence has expired and is in its grace period: {_current.Message}",
        LicenseStatus.ReadOnly => "The licence has expired: the system is read-only until a new licence is installed.",
        LicenseStatus.Valid when DaysUntilExpiry is int days && days <= 30 => $"The licence expires in {days} day(s).",
        LicenseStatus.Valid => null,
        _ => $"No usable licence: {_current.Message}",
    };

    /// <inheritdoc />
    public Result CanPost()
    {
        if (!_enforce || _current.IsUsable)
        {
            return Result.Success();
        }

        return Result.Failure(_current.IsReadOnly
            ? Error.Rule("Licence.ReadOnly", "The licence has expired; the system is read-only and nothing can be posted.")
            : Error.Rule("Licence.NotUsable", $"No usable licence ({_current.Status}); nothing can be posted."));
    }

    /// <inheritdoc />
    public Result WithinLimit(string key, int count, string? moduleCode = null)
    {
        int? limit = moduleCode is null ? _current.GetLimit(key) : ModuleLimit(moduleCode, key) ?? _current.GetLimit(key);
        return !_enforce || limit is null || count <= limit
            ? Result.Success()
            : Result.Failure(Error.Rule("Licence.LimitReached", $"The licence allows {limit} {key.Replace('_', ' ')}; this would be {count}. Free one up or contact your vendor."));
    }

    // A module's limit is keyed either as the global key ("pos_terminals") or, as the brief's payload writes it, without the module prefix ("terminals" under POS).
    private int? ModuleLimit(string moduleCode, string key)
        => _current.GetModuleLimit(moduleCode, key)
            ?? (key.StartsWith(moduleCode + "_", StringComparison.OrdinalIgnoreCase) ? _current.GetModuleLimit(moduleCode, key[(moduleCode.Length + 1)..]) : null);

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        string? revocations = await db.Set<InstalledRevocationList>().OrderByDescending(r => r.Sequence).Select(r => r.Token).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        _revoked = revocations is not null && RevocationListCodec.Verify(revocations, _trustedKeys).List is RevocationList list ? list.Revoked : [];

        InstalledLicence? active = await db.Set<InstalledLicence>()
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        _current = active is null
            ? LicenseValidationResult.Failure(LicenseStatus.Invalid, "No licence installed.", _clock)
            : TrialPeriod.Apply(_validator.Validate(active.Token, _fingerprint.Value, _revoked), await FirstActivatedAsync(db, active.Token, active.InstalledOnUtc, ct).ConfigureAwait(false), _clock);
    }

    // When this licence was first installed on this database (a trial runs from then).
    private static async Task<DateTimeOffset> FirstActivatedAsync(One4AllDbContext db, string token, DateTimeOffset fallback, CancellationToken ct)
        => await db.Set<InstalledLicence>().Where(l => l.Token == token).OrderBy(l => l.InstalledOnUtc).Select(l => (DateTimeOffset?)l.InstalledOnUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? fallback;

    /// <inheritdoc />
    public async Task<LicenseValidationResult> InstallAsync(string licenceToken, CancellationToken ct = default)
    {
        LicenseValidationResult validation = _validator.Validate(licenceToken.Trim(), _fingerprint.Value, _revoked);

        // Only persist a licence whose signature and product are acceptable (it may still be expired/grace).
        if (validation.Status is LicenseStatus.Invalid or LicenseStatus.TamperDetected or LicenseStatus.WrongProduct)
        {
            _current = validation;
            return validation;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        ICurrentUser currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        List<InstalledLicence> existing = await db.Set<InstalledLicence>()
            .Where(l => l.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (InstalledLicence licence in existing)
        {
            licence.Deactivate();
        }

        db.Set<InstalledLicence>().Add(new InstalledLicence(licenceToken.Trim(), _clock.UtcNow, currentUser.UserId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // A trial counts from its first installation here: installing it again does not start it over.
        validation = TrialPeriod.Apply(validation, await FirstActivatedAsync(db, licenceToken.Trim(), _clock.UtcNow, ct).ConfigureAwait(false), _clock);
        await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync(new AuditEvent(null, currentUser.UserId, currentUser.UserName, "Licensing", "Licence",
            validation.Payload?.LicenceId, Core.Audit.AuditAction.LicenceChange, $"Installed: {validation.Status}",
            System.Text.Json.JsonSerializer.Serialize(new { Customer = validation.Payload?.Customer.Name, Edition = validation.Payload?.Edition.ToString(), validation.Payload?.ValidTo })), ct).ConfigureAwait(false);

        _current = validation;
        return validation;
    }

    /// <inheritdoc />
    public async Task<Result<int>> InstallRevocationListAsync(string token, CancellationToken ct = default)
    {
        (RevocationList? list, string? error) = RevocationListCodec.Verify(token, _trustedKeys);
        if (list is null)
        {
            return Error.Validation("Licence.RevocationListInvalid", error ?? "The revocation list is not valid.");
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        int installed = await db.Set<InstalledRevocationList>().Select(r => (int?)r.Sequence).MaxAsync(ct).ConfigureAwait(false) ?? 0;
        if (list.Sequence <= installed)
        {
            return Error.Rule("Licence.RevocationListOld", $"Revocation list {list.Sequence} is not newer than the installed list {installed}.");
        }

        ICurrentUser currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        db.Add(new InstalledRevocationList(token.Trim(), list.Sequence, list.Revoked.Count, _clock.UtcNow, currentUser.UserId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync(new AuditEvent(null, currentUser.UserId, currentUser.UserName, "Licensing", "RevocationList",
            list.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), Core.Audit.AuditAction.LicenceChange, $"Revocation list {list.Sequence} installed ({list.Revoked.Count} revoked)",
            null), ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);
        return list.Revoked.Count;
    }
}
