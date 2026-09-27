using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
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
        IMachineIdProvider machineId)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        // Fingerprint the machine once, lazily: the hardware identity is stable for the process lifetime.
        _fingerprint = new Lazy<CoreFingerprint>(
            () => CoreFingerprint.FromComponents(machineId.GetComponents()),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _validator = new LicenseValidator(new LicenseVerifier(trustedKeys.PublicKeysByKid), clock, Product);
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
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

        InstalledLicence? active = await db.Set<InstalledLicence>()
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        _current = active is null
            ? LicenseValidationResult.Failure(LicenseStatus.Invalid, "No licence installed.", _clock)
            : _validator.Validate(active.Token, _fingerprint.Value);
    }

    /// <inheritdoc />
    public async Task<LicenseValidationResult> InstallAsync(string licenceToken, CancellationToken ct = default)
    {
        LicenseValidationResult validation = _validator.Validate(licenceToken.Trim(), _fingerprint.Value);

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

        _current = validation;
        return validation;
    }
}
