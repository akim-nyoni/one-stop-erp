namespace One4All.Licensing.Core;

/// <summary>The overall status of a licence after full validation.</summary>
public enum LicenseStatus
{
    /// <summary>Valid and fully usable.</summary>
    Valid = 0,

    /// <summary>Expired but within the grace period — still usable, warn the user.</summary>
    Grace = 1,

    /// <summary>Past grace on a non-hard-stop licence: usable read-only (view/print), no posting.</summary>
    ReadOnly = 2,

    /// <summary>Past grace on a hard-stop licence: not usable.</summary>
    Expired = 3,

    /// <summary>Not yet valid (valid_from is in the future).</summary>
    NotYetValid = 4,

    /// <summary>Signature invalid or payload tampered.</summary>
    Invalid = 5,

    /// <summary>Signature/clock tampering detected.</summary>
    TamperDetected = 6,

    /// <summary>The licence is for a different product.</summary>
    WrongProduct = 7,

    /// <summary>The machine does not match the licence's binding.</summary>
    MachineMismatch = 8,

    /// <summary>The vendor has revoked the licence (it appears on an installed revocation list): read-only until a new licence is installed.</summary>
    Revoked = 9,
}

/// <summary>The result of validating a licence, with helpers for module gating and limits.</summary>
public sealed class LicenseValidationResult
{
    private LicenseValidationResult(LicenseStatus status, LicensePayload? payload, string message, IClock clock)
    {
        Status = status;
        Payload = payload;
        Message = message;
        Clock = clock;
    }

    /// <summary>The overall status.</summary>
    public LicenseStatus Status { get; }

    /// <summary>The validated payload, when the signature was valid; otherwise null.</summary>
    public LicensePayload? Payload { get; }

    /// <summary>A human-readable status message (for the licence status screen).</summary>
    public string Message { get; }

    private IClock Clock { get; }

    /// <summary>True when the product may run and post (Valid or Grace).</summary>
    public bool IsUsable => Status is LicenseStatus.Valid or LicenseStatus.Grace;

    /// <summary>True when the product may run only in read-only mode.</summary>
    public bool IsReadOnly => Status is LicenseStatus.ReadOnly or LicenseStatus.Revoked;

    /// <summary>Creates a failed result (no usable payload).</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="message">The message.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The result.</returns>
    public static LicenseValidationResult Failure(LicenseStatus status, string message, IClock clock) =>
        new(status, null, message, clock);

    /// <summary>Creates a result carrying a validated payload.</summary>
    /// <param name="status">The status.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="message">The message.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The result.</returns>
    public static LicenseValidationResult ForPayload(LicenseStatus status, LicensePayload payload, string message, IClock clock) =>
        new(status, payload, message, clock);

    /// <summary>
    /// Returns true when a module is licensed and not expired. The <c>CORE</c> module is always licensed
    /// while the licence itself is usable or read-only. A locked module must be hidden and its API return 403.
    /// </summary>
    /// <param name="moduleCode">The module code (e.g. GL, POS, MUNI, HSE, BUD, PAY).</param>
    /// <returns>True when licensed.</returns>
    public bool IsModuleLicensed(string moduleCode)
    {
        if (Payload is null || Status is LicenseStatus.Invalid or LicenseStatus.TamperDetected
            or LicenseStatus.WrongProduct or LicenseStatus.MachineMismatch
            or LicenseStatus.Expired or LicenseStatus.NotYetValid)
        {
            return false;
        }

        if (string.Equals(moduleCode, LicenseValidator.CoreModuleCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        LicenceModuleGrant? grant = Payload.Modules
            .FirstOrDefault(m => string.Equals(m.Code, moduleCode, StringComparison.OrdinalIgnoreCase));
        if (grant is null)
        {
            return false;
        }

        if (grant.ValidTo is { } moduleExpiry)
        {
            DateOnly today = DateOnly.FromDateTime(Clock.UtcNow.UtcDateTime);
            if (today > moduleExpiry.AddDays(Payload.GraceDays))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns a global limit value, or null when unlimited/undefined.</summary>
    /// <param name="key">The limit key (e.g. companies, concurrent_users, pos_terminals).</param>
    /// <returns>The limit, or null.</returns>
    public int? GetLimit(string key) =>
        Payload is not null && Payload.Limits.TryGetValue(key, out int? value) ? value : null;

    /// <summary>Returns a module-specific limit value, or null when unlimited/undefined.</summary>
    /// <param name="moduleCode">The module code.</param>
    /// <param name="key">The limit key.</param>
    /// <returns>The limit, or null.</returns>
    public int? GetModuleLimit(string moduleCode, string key)
    {
        LicenceModuleGrant? grant = Payload?.Modules
            .FirstOrDefault(m => string.Equals(m.Code, moduleCode, StringComparison.OrdinalIgnoreCase));
        return grant is not null && grant.Limits.TryGetValue(key, out int? value) ? value : null;
    }

    /// <summary>Returns true when a feature flag is present.</summary>
    /// <param name="feature">The feature flag (e.g. MULTI_CURRENCY).</param>
    /// <returns>True when enabled.</returns>
    public bool HasFeature(string feature) =>
        Payload is not null && Payload.Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Validates a licence token end to end (brief §11.5): signature, product, clock-rollback, validity
/// window with grace, hard-stop vs read-only degradation, and machine binding. Fully offline and
/// deterministic — no network call is ever made.
/// </summary>
public sealed class LicenseValidator
{
    /// <summary>The always-licensed core module code.</summary>
    public const string CoreModuleCode = "CORE";

    private readonly LicenseVerifier _verifier;
    private readonly IClock _clock;
    private readonly string _expectedProduct;
    private readonly ILicenseHighWaterMarkStore? _highWaterMarkStore;
    private readonly TimeSpan _clockRollbackTolerance;

    /// <summary>Creates a validator.</summary>
    /// <param name="verifier">The signature verifier holding trusted public keys.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="expectedProduct">The product string this ERP expects (e.g. "One4All ERP").</param>
    /// <param name="highWaterMarkStore">Optional clock-rollback high-water store.</param>
    /// <param name="clockRollbackTolerance">How far the clock may legitimately move back (default 1 day).</param>
    public LicenseValidator(
        LicenseVerifier verifier,
        IClock clock,
        string expectedProduct,
        ILicenseHighWaterMarkStore? highWaterMarkStore = null,
        TimeSpan? clockRollbackTolerance = null)
    {
        _verifier = verifier;
        _clock = clock;
        _expectedProduct = expectedProduct;
        _highWaterMarkStore = highWaterMarkStore;
        _clockRollbackTolerance = clockRollbackTolerance ?? TimeSpan.FromDays(1);
    }

    /// <summary>Validates a licence token for the current machine.</summary>
    /// <param name="token">The compact licence token.</param>
    /// <param name="currentFingerprint">The current machine's fingerprint, required when the licence is machine-bound.</param>
    /// <param name="revokedLicenceIds">Licence ids on the installed revocation list.</param>
    /// <returns>The validation result.</returns>
    public LicenseValidationResult Validate(string token, MachineFingerprint? currentFingerprint = null, IReadOnlyCollection<string>? revokedLicenceIds = null)
    {
        SignatureVerification verification = _verifier.Verify(token);
        if (!verification.IsValid || verification.Payload is null)
        {
            return LicenseValidationResult.Failure(LicenseStatus.Invalid, verification.Error ?? "Invalid licence.", _clock);
        }

        LicensePayload payload = verification.Payload;

        if (!string.Equals(payload.Product, _expectedProduct, StringComparison.Ordinal))
        {
            return LicenseValidationResult.Failure(
                LicenseStatus.WrongProduct, $"Licence is for '{payload.Product}', not '{_expectedProduct}'.", _clock);
        }

        if (revokedLicenceIds is not null && revokedLicenceIds.Contains(payload.LicenceId, StringComparer.Ordinal))
        {
            return LicenseValidationResult.ForPayload(LicenseStatus.Revoked, payload, "The vendor has revoked this licence; the system is read-only until a new licence is installed.", _clock);
        }

        DateTimeOffset now = _clock.UtcNow;

        // Clock-rollback detection.
        if (_highWaterMarkStore is not null)
        {
            DateTimeOffset? mark = _highWaterMarkStore.Read();
            if (mark is { } hw && now < hw - _clockRollbackTolerance)
            {
                return LicenseValidationResult.Failure(
                    LicenseStatus.TamperDetected, "System clock appears to have been set back.", _clock);
            }

            _highWaterMarkStore.Advance(now);
        }

        // Machine binding.
        if (payload.MachineBinding.Mode != MachineBindingMode.None)
        {
            if (currentFingerprint is null)
            {
                return LicenseValidationResult.Failure(
                    LicenseStatus.MachineMismatch, "Licence is machine-bound but no machine fingerprint was supplied.", _clock);
            }

            bool matches = payload.MachineBinding.Fingerprints
                .Select(MachineFingerprint.Parse)
                .Any(allowed => currentFingerprint.FuzzyMatches(allowed));

            if (!matches)
            {
                return LicenseValidationResult.Failure(
                    LicenseStatus.MachineMismatch, "This machine is not licensed to run the product.", _clock);
            }
        }

        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        if (today < payload.ValidFrom)
        {
            return LicenseValidationResult.ForPayload(
                LicenseStatus.NotYetValid, payload, $"Licence is not valid until {payload.ValidFrom:yyyy-MM-dd}.", _clock);
        }

        if (payload.ValidTo is not { } validTo)
        {
            return LicenseValidationResult.ForPayload(LicenseStatus.Valid, payload, "Licence is valid (perpetual).", _clock);
        }

        if (today <= validTo)
        {
            return LicenseValidationResult.ForPayload(LicenseStatus.Valid, payload, $"Licence valid until {validTo:yyyy-MM-dd}.", _clock);
        }

        DateOnly graceEnd = validTo.AddDays(payload.GraceDays);
        if (today <= graceEnd)
        {
            return LicenseValidationResult.ForPayload(
                LicenseStatus.Grace, payload, $"Licence expired on {validTo:yyyy-MM-dd}; in grace until {graceEnd:yyyy-MM-dd}.", _clock);
        }

        return payload.HardStop
            ? LicenseValidationResult.ForPayload(LicenseStatus.Expired, payload, $"Licence expired on {validTo:yyyy-MM-dd}.", _clock)
            : LicenseValidationResult.ForPayload(LicenseStatus.ReadOnly, payload, $"Licence expired on {validTo:yyyy-MM-dd}; running read-only.", _clock);
    }
}
