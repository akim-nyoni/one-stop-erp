using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Security;
using One4All.Application.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Audit;
using One4All.Core.Licensing;
using One4All.Core.Reporting;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the SYS-numbered control and system reports (brief §14 P, R): every data command whose name starts with <c>Sysr</c>.</summary>
public sealed class ControlReportDataProvider : IReportDataProvider
{
    // The records whose changes the master-data report follows (CLR class names, as the audit trail records them).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UserActivityAsync(long companyId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        List<LoginEvent> events = await _db.LoginEvents.AsNoTracking().Where(l => l.OccurredOnUtc >= start && l.OccurredOnUtc <= end).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, int> reportRuns = await _db.Set<ReportRun>().AsNoTracking().Where(r => r.CompanyId == companyId && r.RunAt >= start && r.RunAt <= end && r.UserId != null)
            .GroupBy(r => r.UserId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct).ConfigureAwait(false);
        return [.. events.GroupBy(l => l.UserId is null ? $"(unknown) {l.Username}" : l.Username).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            List<LoginEvent> ok = [.. g.Where(l => l.Outcome == "Success")];
            double hours = ok.Where(l => l.EndedOnUtc is not null).Sum(l => (l.EndedOnUtc!.Value - l.OccurredOnUtc).TotalHours);
            long? userId = g.Select(l => l.UserId).FirstOrDefault(id => id is not null);
            return Row(("User", g.Key), ("Logins", ok.Count), ("Failed", g.Count(l => l.Outcome == "InvalidCredentials")), ("Lockouts", g.Count(l => l.Outcome == "LockedOut")),
                ("First", ok.Count == 0 ? "—" : ok.Min(l => l.OccurredOnUtc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                ("Last", ok.Count == 0 ? "—" : ok.Max(l => l.OccurredOnUtc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Hours", hours.ToString("0.0", CultureInfo.InvariantCulture)),
                ("Terminals", string.Join(", ", g.Select(l => l.Terminal ?? "—").Distinct().Order(StringComparer.Ordinal))), ("Reports", userId is long u ? reportRuns.GetValueOrDefault(u) : 0));
        })];
    }

    // A session still open is treated as running to the end of the report window.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SessionsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        List<LoginEvent> sessions = await _db.LoginEvents.AsNoTracking().Where(l => l.Outcome == "Success" && l.OccurredOnUtc >= start && l.OccurredOnUtc <= end).OrderBy(l => l.OccurredOnUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        DateTimeOffset Finish(LoginEvent l) => l.EndedOnUtc ?? end;
        return [.. sessions.Select(s =>
        {
            List<string> overlapping = [.. sessions.Where(o => o.Id != s.Id && o.UserId == s.UserId && !string.Equals(o.Terminal, s.Terminal, StringComparison.OrdinalIgnoreCase)
                    && o.OccurredOnUtc < Finish(s) && s.OccurredOnUtc < Finish(o)).Select(o => o.Terminal ?? "—").Distinct()];
            return Row(("User", s.Username), ("Terminal", s.Terminal ?? "—"), ("Method", s.Method), ("Start", s.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                ("End", s.EndedOnUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "open"),
                ("Minutes", s.EndedOnUtc is DateTimeOffset e ? ((int)(e - s.OccurredOnUtc).TotalMinutes).ToString(CultureInfo.InvariantCulture) : "—"), ("Concurrent", overlapping.Count == 0 ? "—" : string.Join(", ", overlapping)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OverridesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        string User(long? id) => id is long u ? users.GetValueOrDefault(u, $"user {u}") : "—";
        List<(DateTimeOffset When, IReadOnlyDictionary<string, object?> Row)> rows = [];
        Core.Pos.PosEventKind[] kinds = [Core.Pos.PosEventKind.PriceOverride, Core.Pos.PosEventKind.Refund, Core.Pos.PosEventKind.NoSale];
        foreach (Core.Pos.PosEvent e in await _db.PosEvents.AsNoTracking().Where(e => e.CompanyId == companyId && kinds.Contains(e.Kind) && e.AuthorisedByUserId != null && e.EventDate >= fromDate && e.EventDate <= toDate)
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            string detail = e.Kind switch
            {
                Core.Pos.PosEventKind.PriceOverride => $"{e.SaleNumber}: {e.Description} {e.OriginalAmount:0.00} → {e.Amount:0.00}",
                Core.Pos.PosEventKind.Refund => $"{e.SaleNumber}: {e.Description} refunded {e.Amount:0.00}",
                _ => "Drawer opened without a sale",
            };
            rows.Add((e.OccurredOnUtc, Row(("When", e.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Module", "POS"), ("Override", e.Kind.ToString()), ("Detail", detail),
                ("Operator", User(e.CashierUserId)), ("AuthorisedBy", User(e.AuthorisedByUserId)), ("Reason", e.Reason ?? string.Empty))));
        }

        foreach (Core.JobCosting.WeighbridgeTicket t in await _db.WeighbridgeTickets.AsNoTracking().Where(t => t.CompanyId == companyId && t.IsManualWeight && t.Date >= fromDate && t.Date <= toDate)
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            DateTimeOffset when = t.TimeInUtc ?? new DateTimeOffset(t.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            rows.Add((when, Row(("When", when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Module", "Weighbridge"), ("Override", "Manual weight"),
                ("Detail", $"{t.TicketNumber} {t.VehicleRegistration} net {t.NetTonnes:0.###} t"), ("Operator", User(t.OperatorUserId)), ("AuthorisedBy", User(t.ManualAuthorisedByUserId)), ("Reason", t.ManualReason ?? string.Empty))));
        }

        return [.. rows.OrderBy(r => r.When).Select(r => r.Row)];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HashChainAsync(long companyId, CancellationToken ct)
    {
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach ((string scope, long? id) in new (string, long?)[] { ("This company", companyId), ("System-wide", null) })
        {
            AuditChainVerification check = await _audit.VerifyChainAsync(id, ct).ConfigureAwait(false);
            rows.Add(Row(("Scope", scope), ("Checked", check.EntriesChecked), ("Intact", check.IsIntact ? "Yes" : "NO"), ("FirstBroken", check.FirstBrokenEntryId?.ToString(CultureInfo.InvariantCulture) ?? "—"),
                ("Message", check.Message)));
        }

        return rows;
    }

    private static readonly string[] MasterData =
    [
        "Customer", "Supplier", "Item", "LedgerAccount", "Tariff", "Property", "Employee", "PriceList", "PriceListLine", "BankAccount", "User", "Role", "RolePermission",
        "Company", "TaxCode", "TaxRate", "Warehouse", "PayElement", "Meter", "HousingScheme",
    ];

    private readonly One4AllDbContext _db;
    private readonly Application.Abstractions.Accounting.IDataHealthService? _health;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="permissions">The permission resolver (for separation of duties).</param>
    /// <param name="audit">The audit trail (for hash-chain verification).</param>
    public ControlReportDataProvider(One4AllDbContext db, IPermissionService permissions, IAuditService audit, Application.Abstractions.Accounting.IDataHealthService? health = null)
    {
        _health = health;
        _db = db;
        _permissions = permissions;
        _audit = audit;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Sysr", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        DateTimeOffset start = new(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(toDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        return command.ToUpperInvariant() switch
        {
            "SYSRPERMISSIONMATRIX" => await PermissionMatrixAsync(ct).ConfigureAwait(false),
            "SYSRAUDITTRAIL" => await AuditTrailAsync(companyId, fromDate, toDate, parameters, ct).ConfigureAwait(false),
            "SYSRHEALTH" => await HealthAsync(companyId, DateParameter(parameters, "AsAtDate") ?? today, ct).ConfigureAwait(false),
            "SYSRMEMBERSHIP" => await MembershipAsync(ct).ConfigureAwait(false),
            "SYSRSOD" => await SodAsync(ct).ConfigureAwait(false),
            "SYSRMASTERCHANGES" => await MasterChangesAsync(companyId, start, end, ct).ConfigureAwait(false),
            "SYSRUSERACTIVITY" => await UserActivityAsync(companyId, start, end, ct).ConfigureAwait(false),
            "SYSRSESSIONS" => await SessionsAsync(start, end, ct).ConfigureAwait(false),
            "SYSROVERRIDES" => await OverridesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "SYSRHASHCHAIN" => await HashChainAsync(companyId, ct).ConfigureAwait(false),
            "SYSRBACKDATED" => await BackdatedAsync(companyId, start, end, parameters, ct).ConfigureAwait(false),
            "SYSRDELETED" => await DeletedAsync(companyId, start, end, ct).ConfigureAwait(false),
            "SYSRGAPS" => await GapsAsync(companyId, ct).ConfigureAwait(false),
            "SYSRUNPOSTED" => await UnpostedAsync(companyId, ct).ConfigureAwait(false),
            "SYSRLICENCES" => await LicencesAsync(ct).ConfigureAwait(false),
            "SYSRREPORTRUNS" => await ReportRunsAsync(companyId, start, end, ct).ConfigureAwait(false),
            "SYSRPRICELISTS" => await PriceListsAsync(companyId, ct).ConfigureAwait(false),
            "SYSRRATES" => await RatesAsync(ct).ConfigureAwait(false),
            "SYSRSEQUENCES" => await SequencesAsync(companyId, ct).ConfigureAwait(false),
            "SYSRCOMPANY" => await CompanyAsync(companyId, ct).ConfigureAwait(false),
            "SYSRREPORTINVENTORY" => await ReportInventoryAsync(companyId, ct).ConfigureAwait(false),
            "SYSRJOBS" => await JobsAsync(companyId, start, end, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PermissionMatrixAsync(CancellationToken ct)
    {
        Dictionary<string, Permission> permissions = await _db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);
        var grants = await (from rp in _db.Set<RolePermission>().AsNoTracking() join r in _db.Roles.AsNoTracking() on rp.RoleId equals r.Id
                            select new { Role = r.Name, rp.PermissionCode, rp.Operations }).ToListAsync(ct).ConfigureAwait(false);
        return [.. grants.OrderBy(g => g.Role, StringComparer.Ordinal).ThenBy(g => g.PermissionCode, StringComparer.Ordinal).Select(g =>
        {
            Permission? permission = permissions.GetValueOrDefault(g.PermissionCode);
            return Row(("Role", g.Role), ("Module", permission?.Module ?? "?"), ("Permission", g.PermissionCode), ("Name", permission?.Name ?? "—"), ("Operations", g.Operations.ToString()));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MembershipAsync(CancellationToken ct)
    {
        List<User> users = await _db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> roles = await _db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct).ConfigureAwait(false);
        Dictionary<long, string> groups = await _db.Groups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct).ConfigureAwait(false);
        ILookup<long, long> userRoles = (await _db.Set<UserRole>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).ToLookup(x => x.UserId, x => x.RoleId);
        ILookup<long, long> userGroups = (await _db.Set<UserGroupMember>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).ToLookup(x => x.UserId, x => x.GroupId);
        ILookup<long, long> groupRoles = (await _db.Set<GroupRole>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).ToLookup(x => x.GroupId, x => x.RoleId);
        Dictionary<long, int> overrides = (await _db.Set<UserPermissionOverrideEntity>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).GroupBy(o => o.UserId).ToDictionary(g => g.Key, g => g.Count());
        static string List(IEnumerable<string> names) => string.Join(", ", names.Distinct().Order(StringComparer.Ordinal).DefaultIfEmpty("—"));
        return [.. users.Select(u => Row(("User", u.Username), ("Name", u.DisplayName), ("Status", u.IsSystemAdministrator ? "Administrator" : u.IsActive ? "Active" : "Inactive"),
            ("Roles", List(userRoles[u.Id].Select(r => roles.GetValueOrDefault(r, "?")))), ("Groups", List(userGroups[u.Id].Select(g => groups.GetValueOrDefault(g, "?")))),
            ("GroupRoles", List(userGroups[u.Id].SelectMany(g => groupRoles[g]).Select(r => roles.GetValueOrDefault(r, "?")))), ("Overrides", overrides.GetValueOrDefault(u.Id))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SodAsync(CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking().Where(u => u.IsActive && !u.IsSystemAdministrator).OrderBy(u => u.Username).Select(u => new { u.Id, u.Username }).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (var user in users)
        {
            rows.AddRange((await _permissions.GetSodViolationsAsync(user.Id, ct).ConfigureAwait(false))
                .Select(r => Row(("User", user.Username), ("Rule", r.Code), ("Conflict", r.Name), ("Severity", r.Severity.ToString()))));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MasterChangesAsync(long companyId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
        => [.. (await _db.ChangeLog.AsNoTracking().Where(c => (c.CompanyId == companyId || c.CompanyId == null) && MasterData.Contains(c.EntityType) && c.OccurredOnUtc >= start && c.OccurredOnUtc <= end)
                .OrderBy(c => c.Id).Take(10000).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => Row(("When", Stamp(c.OccurredOnUtc)), ("User", c.UserName), ("Entity", c.EntityType), ("Id", c.EntityId ?? "—"), ("Action", c.Action.ToString()),
                ("Changed", c.ChangedFields.Count == 0 ? "—" : string.Join(", ", c.ChangedFields))))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BackdatedAsync(long companyId, DateTimeOffset start, DateTimeOffset end, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        int days = parameters.TryGetValue("Days", out object? value) && value is not null and not "" ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 7;
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        List<JournalEntry> journals = await _db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.CreatedOnUtc >= start && j.CreatedOnUtc <= end).ToListAsync(ct).ConfigureAwait(false);
        return [.. journals.Select(j => (Journal: j, Entered: DateOnly.FromDateTime(j.CreatedOnUtc.UtcDateTime))).Where(x => x.Entered.DayNumber - x.Journal.PostingDate.DayNumber > days)
            .OrderBy(x => x.Entered).ThenBy(x => x.Journal.JournalNumber)
            .Select(x => Row(("Journal", x.Journal.JournalNumber), ("Posting", Date(x.Journal.PostingDate)), ("Entered", Date(x.Entered)), ("DaysBack", x.Entered.DayNumber - x.Journal.PostingDate.DayNumber),
                ("Source", x.Journal.SourceModule), ("Document", x.Journal.DocumentNumber ?? "—"),
                ("PreparedBy", users.GetValueOrDefault(x.Journal.PreparedByUserId ?? x.Journal.CreatedByUserId, "?")), ("Narrative", x.Journal.Narrative ?? string.Empty)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DeletedAsync(long companyId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        List<(DateTimeOffset When, string Kind, string Entity, string Id, string User, string Reason)> rows = [.. (await _db.ChangeLog.AsNoTracking()
                .Where(c => (c.CompanyId == companyId || c.CompanyId == null) && c.OccurredOnUtc >= start && c.OccurredOnUtc <= end
                    && (c.Action == AuditAction.Delete || (c.NewValues != null && c.NewValues.Contains("\"IsDeleted\":true"))))
                .OrderBy(c => c.Id).Take(5000).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => (c.OccurredOnUtc, c.Action == AuditAction.Delete ? "Deleted" : "Retired", c.EntityType, c.EntityId ?? "—", c.UserName, c.Reason ?? "—"))];
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        rows.AddRange((await _db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && (j.Status == JournalStatus.Reversed || j.Status == JournalStatus.Cancelled)
                && j.ModifiedOnUtc >= start && j.ModifiedOnUtc <= end).ToListAsync(ct).ConfigureAwait(false))
            .Select(j => (j.ModifiedOnUtc!.Value, j.Status == JournalStatus.Reversed ? "Journal reversed" : "Journal cancelled", "JournalEntry", j.JournalNumber,
                j.ModifiedByUserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "—", j.RejectionReason ?? "—")));
        return [.. rows.OrderBy(r => r.When).Select(r => Row(("When", Stamp(r.When)), ("Kind", r.Kind), ("Entity", r.Entity), ("Id", r.Id), ("User", r.User), ("Reason", r.Reason)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GapsAsync(long companyId, CancellationToken ct)
    {
        List<string> numbers = await _db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).Select(j => j.JournalNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. JournalNumberGaps.Find(numbers).Select(g => Row(("Year", g.Year), ("From", g.From), ("To", g.To), ("Missing", g.To - g.From + 1), ("Description", JournalNumberGaps.Describe(g))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UnpostedAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        List<JournalEntry> journals = await _db.Journals.AsNoTracking()
            .Where(j => j.CompanyId == companyId && (j.Status == JournalStatus.Draft || j.Status == JournalStatus.Deferred || j.AwaitingApproval))
            .OrderBy(j => j.PostingDate).ThenBy(j => j.JournalNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. journals.Select(j => Row(("Journal", j.JournalNumber), ("Posting", Date(j.PostingDate)), ("Status", j.AwaitingApproval ? "Awaiting approval" : j.Status.ToString()),
            ("Source", j.SourceModule), ("Document", j.DocumentNumber ?? "—"), ("PreparedBy", users.GetValueOrDefault(j.PreparedByUserId ?? j.CreatedByUserId, "?")), ("Narrative", j.Narrative ?? string.Empty)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LicencesAsync(CancellationToken ct)
    {
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        return [.. (await _db.InstalledLicences.AsNoTracking().OrderBy(l => l.InstalledOnUtc).ToListAsync(ct).ConfigureAwait(false))
            .Select(l => Row(("Installed", Stamp(l.InstalledOnUtc)), ("By", l.InstalledByUserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "—"), ("Active", l.IsActive ? "Yes" : "No"),
                ("Fingerprint", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(l.Token)))[..16])))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReportRunsAsync(long companyId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        return [.. (await _db.Set<ReportRun>().AsNoTracking().Where(r => r.CompanyId == companyId && r.RunAt >= start && r.RunAt <= end).OrderBy(r => r.RunAt).Take(10000).ToListAsync(ct).ConfigureAwait(false))
            .Select(r => Row(("When", Stamp(r.RunAt)), ("Report", r.ReportCode), ("User", r.UserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "system"), ("Purpose", r.Purpose.ToString()),
                ("Format", r.Format), ("Bytes", r.Bytes), ("Copy", r.CopyNumber?.ToString(CultureInfo.InvariantCulture) ?? "—"), ("Parameters", r.Parameters)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RatesAsync(CancellationToken ct)
        => [.. (await _db.ExchangeRates.AsNoTracking().ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(r => r.FromCurrency.Value, StringComparer.Ordinal).ThenBy(r => r.ToCurrency.Value, StringComparer.Ordinal).ThenBy(r => r.RateType).ThenBy(r => r.EffectiveFrom)
            .Select(r => Row(("From", r.FromCurrency.Value), ("To", r.ToCurrency.Value), ("Type", r.RateType.ToString()), ("Effective", Date(r.EffectiveFrom)), ("Quotation", r.Quotation.ToString()),
                ("Rate", r.Rate.ToString("0.######", CultureInfo.InvariantCulture))))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SequencesAsync(long companyId, CancellationToken ct)
        => [.. (await _db.NumberSequences.AsNoTracking().Include(s => s.Years).Where(s => s.CompanyId == companyId).OrderBy(s => s.DocumentType).ThenBy(s => s.BranchId).ToListAsync(ct).ConfigureAwait(false))
            .Select(s => Row(("Document", s.DocumentType), ("Prefix", s.Prefix), ("Branch", s.BranchId?.ToString(CultureInfo.InvariantCulture) ?? "—"), ("Padding", s.Padding),
                ("Yearly", s.ResetYearly ? "Yes" : "No"), ("Gapless", s.Gapless ? "Yes" : "No"),
                ("Next", s.ResetYearly && s.Years.Count > 0 ? string.Join(", ", s.Years.OrderBy(y => y.Year).Select(y => $"{y.Year}: {y.NextValue}")) : s.NextValue.ToString(CultureInfo.InvariantCulture))))];

    // Every simple public property of the company record, by reflection, so new settings appear without code changes.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CompanyAsync(long companyId, CancellationToken ct)
    {
        Company? company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return [];
        }

        static bool Simple(Type type)
        {
            Type t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateOnly) || t == typeof(DateTimeOffset) || t.Name == "CurrencyCode";
        }

        return [.. typeof(Company).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && Simple(p.PropertyType))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => Row(("Setting", p.Name), ("Value", Convert.ToString(p.GetValue(company), CultureInfo.InvariantCulture) ?? "—")))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReportInventoryAsync(long companyId, CancellationToken ct)
        => [.. (await _db.Set<ReportDefinitionRecord>().AsNoTracking().Where(r => r.CompanyId == null || r.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(r => r.Scope).ThenBy(r => r.Code, StringComparer.Ordinal)
            .Select(r => Row(("Code", r.Code), ("Name", r.Name), ("Module", r.Module ?? "—"), ("Scope", r.Scope.ToString()), ("Version", r.Version), ("Changed", Stamp(r.ModifiedOnUtc ?? r.CreatedOnUtc))))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> JobsAsync(long companyId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        List<IReadOnlyDictionary<string, object?>> rows = [.. (await _db.Set<ScheduledJob>().AsNoTracking().Where(j => j.CompanyId == companyId).OrderBy(j => j.Code).ToListAsync(ct).ConfigureAwait(false))
            .Select(j => Row(("Kind", "Job"), ("Name", $"{j.Code} ({j.TaskKey})"), ("When", j.LastRunAt is DateTimeOffset at ? Stamp(at) : "never run"),
                ("Status", !j.IsEnabled ? "Disabled" : j.LastSucceeded switch { true => "Succeeded", false => "Failed", null => "Pending" }),
                ("Detail", $"{j.Frequency}, next {Stamp(j.NextRunAt)}")))];
        Dictionary<long, string> subscriptions = await _db.Set<ReportSubscription>().AsNoTracking().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);
        rows.AddRange((await _db.Set<ReportDelivery>().AsNoTracking().Where(d => d.CompanyId == companyId && d.RunAt >= start && d.RunAt <= end).OrderBy(d => d.RunAt).ToListAsync(ct).ConfigureAwait(false))
            .Select(d => Row(("Kind", "Delivery"), ("Name", subscriptions.GetValueOrDefault(d.SubscriptionId, "?")), ("When", Stamp(d.RunAt)), ("Status", d.Succeeded ? "Sent" : "Failed"),
                ("Detail", $"{d.FileName} to {d.Recipient}: {d.Detail}"))));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PriceListsAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> items = await _db.Items.AsNoTracking().IgnoreQueryFilters().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.Description, ct).ConfigureAwait(false);
        return [.. (await _db.PriceLists.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId).OrderBy(p => p.Code).ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(p => p.Lines.OrderBy(l => items.GetValueOrDefault(l.ItemId), StringComparer.Ordinal).ThenBy(l => l.MinimumQuantity).Select(l => Row(("PriceList", $"{p.Code} {p.Name}"), ("Currency", p.Currency.Value),
                ("Status", (p.IsActive ? "Active" : "Inactive") + (p.IsDefault ? ", default" : string.Empty)), ("Item", items.GetValueOrDefault(l.ItemId, "?")),
                ("MinimumQuantity", l.MinimumQuantity.ToString("0.###", CultureInfo.InvariantCulture)), ("UnitPrice", l.UnitPrice.ToString("N2", CultureInfo.InvariantCulture)))))];
    }

    private Task<Dictionary<long, string>> UsersAsync(CancellationToken ct) => _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct);

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AuditTrailAsync(long companyId, DateOnly fromDate, DateOnly toDate, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string? Text(string name) => parameters.GetValueOrDefault(name) is string text && text.Trim().Length > 0 ? text.Trim() : null;
        IReadOnlyList<AuditEntryView> entries = await _audit.QueryAsync(new AuditQuery
        {
            CompanyId = companyId, From = fromDate, To = toDate, EntityType = Text("EntityType"), UserName = Text("UserName"), Take = 5_000, IncludeUnscoped = true,
        }, ct).ConfigureAwait(false);
        return [.. entries.Select(e => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["When"] = e.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), ["User"] = e.UserName, ["Action"] = e.Action.ToString(),
            ["Record"] = string.IsNullOrEmpty(e.EntityId) ? e.EntityType : $"{e.EntityType} #{e.EntityId}", ["Fields"] = string.Join(", ", e.ChangedFields), ["Reason"] = e.Reason ?? string.Empty,
            ["EntityType"] = e.EntityType, ["EntityId"] = e.EntityId,
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HealthAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        if (_health is null || await _health.SnapshotAsync(companyId, asAt, ct).ConfigureAwait(false) is not { IsSuccess: true } snapshot)
        {
            return [];
        }

        return [.. snapshot.Value.Indicators.Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["Check"] = i.Kind.ToString(), ["Status"] = i.Status.ToString(), ["Count"] = i.Count, ["Value"] = i.Value, ["Findings"] = string.Join("; ", i.Items.Take(5)),
        })];
    }
}
