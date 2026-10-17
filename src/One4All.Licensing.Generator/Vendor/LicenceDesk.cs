using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using One4All.Licensing.Core;

namespace One4All.Licensing.Generator.Vendor;

/// <summary>A customer in the vendor's register.</summary>
public sealed class VendorCustomer
{
    /// <summary>Gets or sets the customer reference (e.g. CUST-0142).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the contact.</summary>
    public string? Contact { get; set; }

    /// <summary>Gets or sets the sites.</summary>
    public List<string> Sites { get; set; } = [];
}

/// <summary>A module on an issued licence.</summary>
/// <param name="Code">The module.</param>
/// <param name="ValidTo">Its last day (null: as long as the licence).</param>
public sealed record IssuedModule(string Code, DateOnly? ValidTo);

/// <summary>A licence the vendor issued, with what became of it.</summary>
public sealed class IssuedLicence
{
    /// <summary>Gets or sets the licence id.</summary>
    public string LicenceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the customer reference.</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Gets or sets why it was issued (Issue, Renew, Upgrade, Downgrade, Change, Reissue, Bulk).</summary>
    public string Action { get; set; } = "Issue";

    /// <summary>Gets or sets when.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Gets or sets the edition.</summary>
    public LicenceEdition Edition { get; set; }

    /// <summary>Gets or sets the licence type.</summary>
    public LicenceType LicenceType { get; set; }

    /// <summary>Gets or sets the first valid day.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Gets or sets the last valid day (null: perpetual).</summary>
    public DateOnly? ValidTo { get; set; }

    /// <summary>Gets or sets the modules.</summary>
    public List<IssuedModule> Modules { get; set; } = [];

    /// <summary>Gets or sets the bound machine fingerprints.</summary>
    public List<string> Fingerprints { get; set; } = [];

    /// <summary>Gets or sets the licence this one replaces.</summary>
    public string? SupersedesId { get; set; }

    /// <summary>Gets or sets the licence that replaced this one.</summary>
    public string? SupersededById { get; set; }

    /// <summary>Gets or sets when it was revoked.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Gets or sets why.</summary>
    public string? RevokedReason { get; set; }

    /// <summary>Gets or sets the signed token (the activation string).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it is the customer's live licence (neither replaced nor revoked).</summary>
    public bool IsCurrent => SupersededById is null && RevokedAt is null;
}

/// <summary>One entry of the issue log: each chained to the one before by hash, so an edited or deleted entry shows.</summary>
/// <param name="Sequence">Its place.</param>
/// <param name="At">When.</param>
/// <param name="Action">What happened.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="LicenceId">The licence, if any.</param>
/// <param name="Detail">Detail.</param>
/// <param name="PreviousHash">The previous entry's hash.</param>
/// <param name="Hash">This entry's hash.</param>
public sealed record VendorAuditEntry(int Sequence, DateTimeOffset At, string Action, string? CustomerId, string? LicenceId, string Detail, string PreviousHash, string Hash);

/// <summary>A licence to issue.</summary>
public sealed class IssueRequest
{
    /// <summary>Gets or sets the customer reference (registered first).</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the edition.</summary>
    public LicenceEdition Edition { get; set; } = LicenceEdition.Standard;

    /// <summary>Gets or sets the licence type.</summary>
    public LicenceType LicenceType { get; set; } = LicenceType.Subscription;

    /// <summary>Gets or sets the term (perpetual, 30d, 6m, 1y or an end date).</summary>
    public string Term { get; set; } = "1y";

    /// <summary>Gets or sets the first valid day (null: today).</summary>
    public DateOnly? ValidFrom { get; set; }

    /// <summary>Gets or sets the modules (null: the edition's template).</summary>
    public IReadOnlyList<string>? Modules { get; set; }

    /// <summary>Gets or sets the feature flags (null: the edition's).</summary>
    public IReadOnlyList<string>? Features { get; set; }

    /// <summary>Gets or sets per-module terms that differ from the licence's (module code → term).</summary>
    public IReadOnlyDictionary<string, string> ModuleTerms { get; set; } = new Dictionary<string, string>();

    /// <summary>Gets or sets the limits.</summary>
    public IReadOnlyDictionary<string, int?> Limits { get; set; } = new Dictionary<string, int?>();

    /// <summary>Gets or sets per-module limits (module code → limit → value), e.g. PAY payrolls or POS terminals.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int?>> ModuleLimits { get; set; } = new Dictionary<string, IReadOnlyDictionary<string, int?>>();

    /// <summary>Gets or sets machine fingerprints to bind to.</summary>
    public IReadOnlyList<string> Fingerprints { get; set; } = [];

    /// <summary>Gets or sets an offline activation request code (its fingerprint is bound).</summary>
    public string? RequestCode { get; set; }

    /// <summary>Gets or sets the grace days after expiry.</summary>
    public int GraceDays { get; set; } = 14;

    /// <summary>Gets or sets a value indicating whether expiry stops the system (false: read-only).</summary>
    public bool HardStop { get; set; }

    /// <summary>Gets or sets the support expiry.</summary>
    public DateOnly? SupportExpiry { get; set; }

    /// <summary>Gets or sets notes.</summary>
    public string? Notes { get; set; }
}

/// <summary>Something about to expire.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="CustomerName">Its name.</param>
/// <param name="LicenceId">The licence.</param>
/// <param name="What">"Licence" or the module code.</param>
/// <param name="ExpiresOn">The last valid day.</param>
/// <param name="DaysLeft">Days left (negative: already expired).</param>
public sealed record ExpiringItem(string CustomerId, string CustomerName, string LicenceId, string What, DateOnly ExpiresOn, int DaysLeft);

/// <summary>A row of a bulk issue.</summary>
/// <param name="Line">The CSV line.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="LicenceId">The licence issued, if it was.</param>
/// <param name="Error">Why not, if it was not.</param>
public sealed record BulkIssueRow(int Line, string CustomerId, string? LicenceId, string? Error);

/// <summary>
/// The vendor's licence desk (brief §11.4): a customer register and the history of every licence issued to each, kept in a folder beside
/// the private key; issue (edition templates, per-module terms, machine binding from fingerprints or an offline request code), renew,
/// upgrade or downgrade, reissue to new machines, revoke and publish the signed revocation list, bulk issue from CSV, the expiry dashboard,
/// a printable certificate, and an append-only issue log whose hash chain shows any edit.
/// </summary>
public sealed class LicenceDesk
{
    private const string Product = "One4All ERP";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _folder;
    private readonly string _privateKeyBase64;
    private readonly string _keyId;
    private readonly Func<DateTimeOffset> _now;
    private readonly RegisterState _state;

    /// <summary>Opens (or starts) the register in a folder.</summary>
    /// <param name="folder">The register folder.</param>
    /// <param name="privateKeyBase64">The signing key.</param>
    /// <param name="keyId">Its key id.</param>
    /// <param name="now">The clock (null: the system clock).</param>
    public LicenceDesk(string folder, string privateKeyBase64, string keyId, Func<DateTimeOffset>? now = null)
    {
        _folder = folder;
        _privateKeyBase64 = privateKeyBase64;
        _keyId = keyId;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(folder);
        _state = File.Exists(RegisterPath) ? JsonSerializer.Deserialize<RegisterState>(File.ReadAllText(RegisterPath), Json) ?? new RegisterState() : new RegisterState();
    }

    /// <summary>Gets the customers.</summary>
    public IReadOnlyList<VendorCustomer> Customers => _state.Customers;

    /// <summary>Gets every licence issued.</summary>
    public IReadOnlyList<IssuedLicence> Licences => _state.Licences;

    private string RegisterPath => Path.Combine(_folder, "register.json");

    private string AuditPath => Path.Combine(_folder, "issue-log.jsonl");

    private DateOnly Today => DateOnly.FromDateTime(_now().UtcDateTime);

    /// <summary>Adds a customer, or updates its name, contact and sites.</summary>
    /// <param name="id">The reference.</param>
    /// <param name="name">The name.</param>
    /// <param name="contact">The contact.</param>
    /// <param name="sites">The sites.</param>
    /// <returns>The customer.</returns>
    public VendorCustomer SaveCustomer(string id, string name, string? contact = null, IEnumerable<string>? sites = null)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A customer needs a reference and a name.");
        }

        VendorCustomer? customer = Customer(id.Trim());
        bool added = customer is null;
        customer ??= new VendorCustomer { Id = id.Trim().ToUpperInvariant() };
        customer.Name = name.Trim();
        customer.Contact = contact ?? customer.Contact;
        customer.Sites = sites is null ? customer.Sites : [.. sites];
        if (added)
        {
            _state.Customers.Add(customer);
        }

        Log(added ? "CustomerAdded" : "CustomerUpdated", customer.Id, null, customer.Name);
        Save();
        return customer;
    }

    /// <summary>Issues a licence.</summary>
    /// <param name="request">What to issue.</param>
    /// <returns>The licence.</returns>
    public IssuedLicence Issue(IssueRequest request) => IssueCore(request, "Issue", null);

    /// <summary>Renews a licence for another term from where it ends (or today, if it already has), valid from today; module dates move with it.</summary>
    /// <param name="licenceId">The licence.</param>
    /// <param name="term">The new term.</param>
    /// <returns>The renewal.</returns>
    public IssuedLicence Renew(string licenceId, string term)
    {
        IssuedLicence old = Current(licenceId);
        LicensePayload payload = Decode(old);
        DateOnly start = old.ValidTo is DateOnly end && end >= Today ? end.AddDays(1) : Today;
        DateOnly? newEnd = LicenceTerm.EndOf(term, start);
        foreach (LicenceModuleGrant module in payload.Modules)
        {
            module.ValidTo = module.ValidTo is null ? null : newEnd;
        }

        // Valid from today, so the renewal can be installed as soon as it arrives; the new term runs on from where the old one ends.
        payload.ValidFrom = Today;
        payload.ValidTo = newEnd;
        return Replace(old, payload, "Renew", $"to {(newEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual")}");
    }

    /// <summary>Changes a licence's edition or modules (upgrade or downgrade) for the rest of its term.</summary>
    /// <param name="licenceId">The licence.</param>
    /// <param name="edition">The new edition (null: unchanged).</param>
    /// <param name="add">Modules to add.</param>
    /// <param name="remove">Modules to remove.</param>
    /// <returns>The replacement.</returns>
    public IssuedLicence Change(string licenceId, LicenceEdition? edition, IEnumerable<string>? add = null, IEnumerable<string>? remove = null)
    {
        IssuedLicence old = Current(licenceId);
        LicensePayload payload = Decode(old);
        HashSet<string> removing = [.. (remove ?? []).Select(m => m.Trim().ToUpperInvariant())];
        payload.Modules.RemoveAll(m => removing.Contains(m.Code));
        foreach (string code in (add ?? []).Select(m => m.Trim().ToUpperInvariant()).Where(c => payload.Modules.All(m => m.Code != c)))
        {
            payload.Modules.Add(new LicenceModuleGrant { Code = code, ValidTo = payload.ValidTo });
        }

        int before = EditionTemplates.Rank(payload.Edition) * 100 + old.Modules.Count;
        payload.Edition = edition ?? payload.Edition;
        int after = EditionTemplates.Rank(payload.Edition) * 100 + payload.Modules.Count;
        string action = after > before ? "Upgrade" : after < before ? "Downgrade" : "Change";
        payload.ValidFrom = Today > payload.ValidFrom ? Today : payload.ValidFrom;
        return Replace(old, payload, action, $"{payload.Edition}: {string.Join(' ', payload.Modules.Select(m => m.Code))}");
    }

    /// <summary>Reissues a licence on the same terms to other machines (a server replaced, a site moved).</summary>
    /// <param name="licenceId">The licence.</param>
    /// <param name="fingerprints">The new fingerprints (or request codes).</param>
    /// <returns>The replacement.</returns>
    public IssuedLicence Reissue(string licenceId, IEnumerable<string> fingerprints)
    {
        IssuedLicence old = Current(licenceId);
        LicensePayload payload = Decode(old);
        List<string> bound = [.. fingerprints.Select(Fingerprint)];
        if (bound.Count == 0)
        {
            throw new ArgumentException("Give at least one machine fingerprint or request code.");
        }

        payload.MachineBinding.Fingerprints = bound;
        payload.MachineBinding.Mode = payload.MachineBinding.Mode == MachineBindingMode.None ? MachineBindingMode.Machine : payload.MachineBinding.Mode;
        return Replace(old, payload, "Reissue", $"{bound.Count} machine(s)");
    }

    /// <summary>Revokes a licence; it takes effect where the next revocation list is installed.</summary>
    /// <param name="licenceId">The licence.</param>
    /// <param name="reason">Why.</param>
    public void Revoke(string licenceId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Give the reason for revoking.");
        }

        IssuedLicence licence = Find(licenceId);
        if (licence.RevokedAt is not null)
        {
            throw new InvalidOperationException($"Licence {licence.LicenceId} is already revoked.");
        }

        licence.RevokedAt = _now();
        licence.RevokedReason = reason.Trim();
        Log("Revoke", licence.CustomerId, licence.LicenceId, licence.RevokedReason);
        Save();
    }

    /// <summary>Signs the next revocation list (every licence revoked so far) for customers to install.</summary>
    /// <returns>The token and its sequence.</returns>
    public (string Token, int Sequence) PublishRevocationList()
    {
        _state.RevocationSequence++;
        RevocationList list = new()
        {
            KeyId = _keyId, Sequence = _state.RevocationSequence, IssuedAt = _now(), Revoked = [.. _state.Licences.Where(l => l.RevokedAt is not null).Select(l => l.LicenceId)],
        };
        string token = RevocationListCodec.Sign(list, _privateKeyBase64);
        Log("RevocationList", null, null, $"sequence {list.Sequence}, {list.Revoked.Count} revoked");
        Save();
        return (token, list.Sequence);
    }

    /// <summary>Current licences (and their modules) that expire within some days, soonest first, including any already past.</summary>
    /// <param name="withinDays">The window.</param>
    /// <returns>What expires.</returns>
    public IReadOnlyList<ExpiringItem> Expiring(int withinDays)
    {
        DateOnly today = Today;
        List<ExpiringItem> items = [];
        foreach (IssuedLicence licence in _state.Licences.Where(l => l.IsCurrent))
        {
            string name = Customer(licence.CustomerId)?.Name ?? licence.CustomerId;
            IEnumerable<(string What, DateOnly? On)> dates = licence.Modules.Where(m => m.ValidTo != licence.ValidTo).Select(m => (m.Code, m.ValidTo)).Prepend(("Licence", licence.ValidTo));
            items.AddRange(dates.Where(d => d.On is DateOnly on && on.DayNumber - today.DayNumber <= withinDays)
                .Select(d => new ExpiringItem(licence.CustomerId, name, licence.LicenceId, d.What, d.On!.Value, d.On.Value.DayNumber - today.DayNumber)));
        }

        return [.. items.OrderBy(i => i.ExpiresOn).ThenBy(i => i.CustomerId, StringComparer.Ordinal)];
    }

    /// <summary>A customer's licences, newest first.</summary>
    /// <param name="customerId">The customer.</param>
    /// <returns>The history.</returns>
    public IReadOnlyList<IssuedLicence> History(string customerId)
        => [.. _state.Licences.Where(l => string.Equals(l.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.IssuedAt)];

    /// <summary>
    /// Issues licences from CSV: <c>customer_id,customer_name,edition,type,term[,modules]</c> (modules space-separated; blank for the
    /// edition's). Customers not yet registered are added. Each row stands alone: a bad row is reported and the others still issue.
    /// </summary>
    /// <param name="csv">The CSV, with a header row.</param>
    /// <returns>Each row's outcome.</returns>
    public IReadOnlyList<BulkIssueRow> BulkIssue(string csv)
    {
        List<BulkIssueRow> rows = [];
        string[] lines = (csv ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        for (int n = 1; n < lines.Length; n++)
        {
            if (string.IsNullOrWhiteSpace(lines[n]))
            {
                continue;
            }

            string[] f = [.. lines[n].Split(',').Select(x => x.Trim())];
            string customerId = f.Length > 0 ? f[0] : string.Empty;
            try
            {
                if (f.Length < 5)
                {
                    throw new FormatException("Expected customer_id,customer_name,edition,type,term[,modules].");
                }

                // Check the row before registering anyone.
                LicenceEdition edition = Enum.TryParse(f[2], ignoreCase: true, out LicenceEdition e) && Enum.IsDefined(e) ? e : throw new FormatException($"Unknown edition '{f[2]}'.");
                LicenceType type = Enum.TryParse(f[3], ignoreCase: true, out LicenceType t) && Enum.IsDefined(t) ? t : throw new FormatException($"Unknown licence type '{f[3]}'.");
                _ = LicenceTerm.EndOf(f[4], Today);
                if (Customer(customerId) is null)
                {
                    SaveCustomer(customerId, f[1]);
                }

                IssuedLicence licence = IssueCore(new IssueRequest
                {
                    CustomerId = customerId, Edition = edition, LicenceType = type, Term = f[4],
                    Modules = f.Length > 5 && f[5].Length > 0 ? f[5].Split(' ', StringSplitOptions.RemoveEmptyEntries) : null,
                }, "Bulk", null);
                rows.Add(new BulkIssueRow(n + 1, customerId, licence.LicenceId, null));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                rows.Add(new BulkIssueRow(n + 1, customerId, null, ex.Message));
            }
        }

        return rows;
    }

    /// <summary>The text a certificate's QR code carries: licence, customer, end date and the first 16 hex digits of the token's SHA-256.</summary>
    /// <param name="licence">The licence.</param>
    /// <returns>The code.</returns>
    public static string VerificationCode(IssuedLicence licence)
    {
        ArgumentNullException.ThrowIfNull(licence);
        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(licence.Token)))[..16];
        return $"O4A-LIC|{licence.LicenceId}|{licence.CustomerId}|{licence.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual"}|{fingerprint}";
    }

    private static byte[] Qr(string text)
    {
        using QRCoder.QRCodeGenerator generator = new();
        using QRCoder.QRCodeData data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        return new QRCoder.PngByteQRCode(data).GetGraphic(4);
    }

    /// <summary>
    /// Publishes a release on the update channel: the installer's SHA-256 with its version and location, signed with the vendor key (and logged).
    /// </summary>
    /// <param name="version">The version released.</param>
    /// <param name="installerPath">The installer file (hashed).</param>
    /// <param name="installerLocation">Where installations download it from (https URL or share path).</param>
    /// <param name="minimumVersion">The oldest version still allowed to run, or null.</param>
    /// <param name="notes">Release notes.</param>
    /// <returns>The signed manifest token to place on the channel.</returns>
    public string PublishUpdate(string version, string installerPath, string installerLocation, string? minimumVersion, string? notes)
    {
        if (!System.Version.TryParse(version, out _))
        {
            throw new ArgumentException($"'{version}' is not a version.", nameof(version));
        }

        string sha;
        using (FileStream file = File.OpenRead(installerPath))
        {
            sha = Convert.ToHexString(SHA256.HashData(file));
        }

        string token = UpdateManifestCodec.Sign(new UpdateManifest
        {
            KeyId = _keyId, Version = version, InstallerLocation = installerLocation, Sha256 = sha, MinimumVersion = minimumVersion, Notes = notes, PublishedAt = _now(),
        }, _privateKeyBase64);
        Log("Update", null, null, $"Version {version} ({sha[..12]}…) at {installerLocation}");
        return token;
    }

    /// <summary>The licence certificate as a PDF: customer, licence, modules with their end dates, the activation string and the verification QR code.</summary>
    /// <param name="licenceId">The licence.</param>
    /// <returns>The PDF bytes.</returns>
    public byte[] CertificatePdf(string licenceId)
    {
        IssuedLicence licence = Find(licenceId);
        VendorCustomer? customer = Customer(licence.CustomerId);
        List<CertificateLine> lines =
        [
            new("One4All ERP - Licence Certificate", CertificateStyle.Title),
            new($"{customer?.Name} ({licence.CustomerId})", CertificateStyle.Heading),
            new(customer?.Contact ?? string.Empty),
            new(string.Empty),
            new($"Licence {licence.LicenceId}"),
            new($"{licence.Edition} edition, {licence.LicenceType}, issued {licence.IssuedAt:yyyy-MM-dd} ({licence.Action})"),
            new($"Valid {licence.ValidFrom:yyyy-MM-dd} to {licence.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual"}"
                + (licence.Fingerprints.Count > 0 ? $", bound to {licence.Fingerprints.Count} machine(s)" : string.Empty)),
            new(string.Empty),
            new("Modules", CertificateStyle.Heading),
            .. licence.Modules.Select(m => new CertificateLine($"{m.Code,-12} valid to {m.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "the end of the licence"}")),
            new(string.Empty),
            new("Activation string", CertificateStyle.Heading),
            .. CertificateLayout.Wrap(licence.Token, 64).Select(piece => new CertificateLine(piece, CertificateStyle.Code)),
            new(string.Empty),
            new("Verification", CertificateStyle.Heading),
            .. CertificateLayout.Wrap(VerificationCode(licence), 64).Select(piece => new CertificateLine(piece, CertificateStyle.Code)),
        ];
        if (licence.RevokedAt is not null)
        {
            lines.Add(new CertificateLine(string.Empty));
            lines.Add(new CertificateLine($"REVOKED {licence.RevokedAt:yyyy-MM-dd}: {licence.RevokedReason}", CertificateStyle.Warning));
        }

        using QRCoder.QRCodeGenerator generator = new();
        using QRCoder.QRCodeData data = generator.CreateQrCode(VerificationCode(licence), QRCoder.QRCodeGenerator.ECCLevel.M);
        List<IReadOnlyList<bool>> modules = [.. data.ModuleMatrix.Select(row => (IReadOnlyList<bool>)[.. row.Cast<bool>()])];
        return CertificateLayout.Build(lines, modules);
    }

    /// <summary>A printable licence certificate (HTML: print to PDF from any browser).</summary>
    /// <param name="licenceId">The licence.</param>
    /// <returns>The HTML.</returns>
    public string Certificate(string licenceId)
    {
        IssuedLicence licence = Find(licenceId);
        VendorCustomer? customer = Customer(licence.CustomerId);
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        StringBuilder html = new();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Licence certificate</title><style>body{font-family:Segoe UI,Arial,sans-serif;margin:40px;color:#222}"
            + "h1{color:#1b5e20}table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:4px 10px;text-align:left}code{word-break:break-all;font-size:11px}</style></head><body>");
        html.Append(CultureInfo.InvariantCulture, $"<h1>One4All ERP - Licence Certificate</h1><p><b>{E(customer?.Name)}</b> ({E(licence.CustomerId)})<br>{E(customer?.Contact)}</p>");
        html.Append(CultureInfo.InvariantCulture, $"<p>Licence <b>{E(licence.LicenceId)}</b> · {licence.Edition} · {licence.LicenceType} · issued {licence.IssuedAt:yyyy-MM-dd} ({E(licence.Action)})<br>");
        html.Append(CultureInfo.InvariantCulture, $"Valid {licence.ValidFrom:yyyy-MM-dd} to {(licence.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual")}"
            + $"{(licence.Fingerprints.Count > 0 ? $" · bound to {licence.Fingerprints.Count} machine(s)" : string.Empty)}</p>");
        html.Append("<table><tr><th>Module</th><th>Valid to</th></tr>");
        foreach (IssuedModule module in licence.Modules)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{E(module.Code)}</td><td>{(module.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "with the licence")}</td></tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"</table><h3>Activation string</h3><p><code>{E(licence.Token)}</code></p>");

        // A QR code a phone can read to check the certificate against the vendor's register: the licence, customer, end date and a fingerprint of the token.
        string check = VerificationCode(licence);
        html.Append(CultureInfo.InvariantCulture, $"<h3>Verification</h3><p><img alt=\"Licence QR code\" src=\"data:image/png;base64,{Convert.ToBase64String(Qr(check))}\"><br><code>{E(check)}</code></p>");
        if (licence.RevokedAt is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"color:#b71c1c\"><b>REVOKED</b> {licence.RevokedAt:yyyy-MM-dd}: {E(licence.RevokedReason)}</p>");
        }

        return html.Append("</body></html>").ToString();
    }

    /// <summary>Checks the issue log's hash chain.</summary>
    /// <returns>The entries, and the first broken sequence (null when intact).</returns>
    public (int Entries, int? BrokenAt) VerifyLog()
    {
        List<VendorAuditEntry> entries = ReadLog();
        string previous = string.Empty;
        foreach (VendorAuditEntry entry in entries)
        {
            if (entry.PreviousHash != previous || entry.Hash != HashOf(entry with { Hash = string.Empty }))
            {
                return (entries.Count, entry.Sequence);
            }

            previous = entry.Hash;
        }

        return (entries.Count, null);
    }

    /// <summary>Reads the issue log (for export).</summary>
    /// <returns>The entries in order.</returns>
    public List<VendorAuditEntry> ReadLog()
        => File.Exists(AuditPath) ? [.. File.ReadAllLines(AuditPath).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<VendorAuditEntry>(l)!)] : [];

    private IssuedLicence IssueCore(IssueRequest request, string action, string? supersedes)
    {
        VendorCustomer customer = Customer(request.CustomerId) ?? throw new InvalidOperationException($"Customer {request.CustomerId} is not registered.");
        DateOnly start = request.ValidFrom ?? Today;
        DateOnly? end = LicenceTerm.EndOf(request.Term, start);
        List<string> fingerprints = [.. request.Fingerprints.Select(Fingerprint)];
        if (request.RequestCode is not null)
        {
            fingerprints.Add((LicenceRequestCode.TryParse(request.RequestCode) ?? throw new FormatException("The request code is not valid.")).Fingerprint);
        }

        LicensePayload payload = new()
        {
            KeyId = _keyId, IssuedAt = _now(), Customer = new LicenceCustomer { Id = customer.Id, Name = customer.Name, Contact = customer.Contact }, Product = Product,
            Edition = request.Edition, LicenceType = request.LicenceType, ValidFrom = start, ValidTo = end, GraceDays = request.GraceDays, HardStop = request.HardStop,
            SupportExpiry = request.SupportExpiry, Notes = request.Notes,
            MachineBinding = new MachineBinding { Mode = fingerprints.Count == 0 ? MachineBindingMode.None : MachineBindingMode.Machine, Fingerprints = fingerprints, AllowRebindCount = 2 },
        };
        foreach ((string key, int? value) in request.Limits)
        {
            payload.Limits[key] = value;
        }

        payload.Features = [.. (request.Features ?? LicenceFeatures.ForEdition(request.Edition)).Select(f => f.Trim().ToUpperInvariant()).Where(f => f.Length > 0).Distinct()];

        foreach (string code in (request.Modules ?? EditionTemplates.ModulesFor(request.Edition)).Select(m => m.Trim().ToUpperInvariant()).Distinct())
        {
            DateOnly? moduleEnd = request.ModuleTerms.TryGetValue(code, out string? term) ? LicenceTerm.EndOf(term, start) : end;
            if (end is DateOnly licenceEnd && (moduleEnd is null || moduleEnd > licenceEnd))
            {
                moduleEnd = licenceEnd;
            }

            LicenceModuleGrant grant = new() { Code = code, ValidTo = moduleEnd };
            foreach ((string key, int? value) in request.ModuleLimits.FirstOrDefault(m => string.Equals(m.Key, code, StringComparison.OrdinalIgnoreCase)).Value ?? new Dictionary<string, int?>())
            {
                grant.Limits[key.Trim().ToLowerInvariant()] = value;
            }

            payload.Modules.Add(grant);
        }

        return Record(payload, action, supersedes, $"{payload.Edition} {payload.LicenceType} to {(end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual")}");
    }

    private IssuedLicence Replace(IssuedLicence old, LicensePayload payload, string action, string detail)
    {
        payload.LicenceId = Guid.NewGuid().ToString();
        payload.IssuedAt = _now();
        payload.KeyId = _keyId;
        IssuedLicence replacement = Record(payload, action, old.LicenceId, detail);
        old.SupersededById = replacement.LicenceId;
        Save();
        return replacement;
    }

    private IssuedLicence Record(LicensePayload payload, string action, string? supersedes, string detail)
    {
        IssuedLicence licence = new()
        {
            LicenceId = payload.LicenceId, CustomerId = payload.Customer.Id, Action = action, IssuedAt = payload.IssuedAt, Edition = payload.Edition, LicenceType = payload.LicenceType,
            ValidFrom = payload.ValidFrom, ValidTo = payload.ValidTo, Modules = [.. payload.Modules.Select(m => new IssuedModule(m.Code, m.ValidTo))],
            Fingerprints = [.. payload.MachineBinding.Fingerprints], SupersedesId = supersedes, Token = new LicenseSigner(_privateKeyBase64).Sign(payload),
        };
        _state.Licences.Add(licence);
        Log(action, licence.CustomerId, licence.LicenceId, detail);
        Save();
        return licence;
    }

    private LicensePayload Decode(IssuedLicence licence)
        => CanonicalJson.Deserialize(Encoding.UTF8.GetString(Base64Url.Decode(licence.Token.Split('.')[0]))) ?? throw new InvalidOperationException("The stored licence cannot be read.");

    private IssuedLicence Current(string licenceId)
    {
        IssuedLicence licence = Find(licenceId);
        return licence.IsCurrent ? licence
            : throw new InvalidOperationException($"Licence {licence.LicenceId} has been {(licence.RevokedAt is not null ? "revoked" : $"replaced by {licence.SupersededById}")}.");
    }

    private IssuedLicence Find(string licenceId)
        => _state.Licences.FirstOrDefault(l => l.LicenceId.StartsWith(licenceId.Trim(), StringComparison.OrdinalIgnoreCase) && licenceId.Trim().Length >= 8)
            ?? throw new InvalidOperationException($"No licence {licenceId} (give at least the first 8 characters).");

    private VendorCustomer? Customer(string id) => _state.Customers.FirstOrDefault(c => string.Equals(c.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Fingerprint(string value)
        => LicenceRequestCode.TryParse(value)?.Fingerprint
            ?? (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? value.Trim() : throw new FormatException($"'{value}' is neither a machine fingerprint nor a request code."));

    private void Log(string action, string? customerId, string? licenceId, string detail)
    {
        List<VendorAuditEntry> entries = ReadLog();
        VendorAuditEntry entry = new(entries.Count + 1, _now(), action, customerId, licenceId, detail, entries.Count == 0 ? string.Empty : entries[^1].Hash, string.Empty);
        entry = entry with { Hash = HashOf(entry) };
        File.AppendAllText(AuditPath, JsonSerializer.Serialize(entry) + "\n");
    }

    private static string HashOf(VendorAuditEntry entry)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry with { Hash = string.Empty }))));

    private void Save() => File.WriteAllText(RegisterPath, JsonSerializer.Serialize(_state, Json));

    private sealed class RegisterState
    {
        public List<VendorCustomer> Customers { get; set; } = [];

        public List<IssuedLicence> Licences { get; set; } = [];

        public int RevocationSequence { get; set; }
    }
}
