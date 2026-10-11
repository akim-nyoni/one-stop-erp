using System.Globalization;
using One4All.Licensing.Core;
using One4All.Licensing.Generator.Vendor;

namespace One4All.Licensing.Generator;

/// <summary>The licence-desk commands (customer register, issue/renew/change/reissue/revoke, revocation list, bulk, expiry, certificate, log).</summary>
internal static class DeskCommands
{
    /// <summary>The desk command names.</summary>
    public static readonly string[] Names = ["customer", "customers", "grant", "renew", "change", "reissue", "revoke", "crl", "expiring", "history", "bulk", "certificate", "log", "request"];

    /// <summary>Runs a desk command.</summary>
    /// <param name="command">The command.</param>
    /// <param name="a">The arguments.</param>
    /// <param name="loadKey">Loads the private key from <c>--key</c> and <c>--passphrase</c>.</param>
    /// <returns>The exit code.</returns>
    public static int Run(string command, IReadOnlyDictionary<string, string> a, Func<string, string?, string> loadKey)
    {
        if (command == "request")
        {
            string fingerprint = MachineFingerprint.FromComponents(HostMachineId.GatherComponents()).ToString();
            Console.WriteLine(new LicenceRequestCode(fingerprint, a.GetValueOrDefault("customer"), a.GetValueOrDefault("site", Environment.MachineName), DateTimeOffset.UtcNow).Encode());
            return 0;
        }

        string keyPath = Need(a, "key");
        string kid = a.GetValueOrDefault("kid", Path.GetFileNameWithoutExtension(keyPath).Replace(".key", string.Empty, StringComparison.Ordinal));
        LicenceDesk desk = new(a.GetValueOrDefault("register", "register"), loadKey(keyPath, a.GetValueOrDefault("passphrase")), kid);

        switch (command)
        {
            case "customer":
                VendorCustomer customer = desk.SaveCustomer(Need(a, "id"), Need(a, "name"), a.GetValueOrDefault("contact"), a.TryGetValue("sites", out string? sites) ? sites.Split(';') : null);
                Console.WriteLine($"Customer {customer.Id}: {customer.Name}");
                return 0;
            case "customers":
                foreach (VendorCustomer c in desk.Customers)
                {
                    IssuedLicence? live = desk.History(c.Id).FirstOrDefault(l => l.IsCurrent);
                    Console.WriteLine($"{c.Id,-12} {c.Name,-32} {(live is null ? "no current licence" : $"{live.Edition} to {Date(live.ValidTo)} ({live.LicenceId[..8]})")}");
                }

                return 0;
            case "grant":
                return Show(desk.Issue(new IssueRequest
                {
                    CustomerId = Need(a, "customer"), Edition = Enum.Parse<LicenceEdition>(a.GetValueOrDefault("edition", "Standard"), true),
                    LicenceType = Enum.Parse<LicenceType>(a.GetValueOrDefault("type", "Subscription"), true), Term = a.GetValueOrDefault("term", "1y"),
                    Modules = a.TryGetValue("modules", out string? modules) ? modules.Split(' ', ',', StringSplitOptions.RemoveEmptyEntries) : null,
                    ModuleTerms = a.TryGetValue("module-terms", out string? terms)
                        ? terms.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Split('=')).ToDictionary(p => p[0].Trim().ToUpperInvariant(), p => p[1].Trim())
                        : new Dictionary<string, string>(),
                    Fingerprints = a.TryGetValue("fingerprint", out string? fp) ? [fp] : [], RequestCode = a.GetValueOrDefault("request"),
                    GraceDays = int.Parse(a.GetValueOrDefault("grace", "14"), CultureInfo.InvariantCulture), HardStop = a.ContainsKey("hard-stop"), Notes = a.GetValueOrDefault("notes"),
                }), a);
            case "renew":
                return Show(desk.Renew(Need(a, "licence"), Need(a, "term")), a);
            case "change":
                return Show(desk.Change(Need(a, "licence"), a.TryGetValue("edition", out string? edition) ? Enum.Parse<LicenceEdition>(edition, true) : null,
                    a.GetValueOrDefault("add", string.Empty).Split(' ', ',', StringSplitOptions.RemoveEmptyEntries), a.GetValueOrDefault("remove", string.Empty).Split(' ', ',', StringSplitOptions.RemoveEmptyEntries)), a);
            case "reissue":
                return Show(desk.Reissue(Need(a, "licence"), [a.GetValueOrDefault("request") ?? Need(a, "fingerprint")]), a);
            case "revoke":
                desk.Revoke(Need(a, "licence"), Need(a, "reason"));
                Console.WriteLine("Revoked. Publish a new revocation list (crl) and send it to the customer.");
                return 0;
            case "crl":
                (string token, int sequence) = desk.PublishRevocationList();
                File.WriteAllText(a.GetValueOrDefault("out", $"revocations-{sequence}.crl"), token);
                Console.WriteLine($"Revocation list {sequence} written to {a.GetValueOrDefault("out", $"revocations-{sequence}.crl")}");
                return 0;
            case "expiring":
                foreach (ExpiringItem item in desk.Expiring(int.Parse(a.GetValueOrDefault("days", "30"), CultureInfo.InvariantCulture)))
                {
                    Console.WriteLine($"{item.ExpiresOn:yyyy-MM-dd} {(item.DaysLeft < 0 ? "EXPIRED" : $"{item.DaysLeft,4} d")} {item.CustomerId,-12} {item.CustomerName,-30} {item.What}");
                }

                return 0;
            case "history":
                foreach (IssuedLicence l in desk.History(Need(a, "customer")))
                {
                    string state = l.RevokedAt is not null ? $"REVOKED ({l.RevokedReason})" : l.SupersededById is not null ? $"replaced by {l.SupersededById[..8]}" : "current";
                    Console.WriteLine($"{l.IssuedAt:yyyy-MM-dd} {l.LicenceId[..8]} {l.Action,-9} {l.Edition,-12} {Date(l.ValidFrom)}..{Date(l.ValidTo)} {state}");
                }

                return 0;
            case "bulk":
                IReadOnlyList<BulkIssueRow> rows = desk.BulkIssue(File.ReadAllText(Need(a, "csv")));
                foreach (BulkIssueRow row in rows)
                {
                    Console.WriteLine($"line {row.Line}: {row.CustomerId} {(row.Error is null ? $"issued {row.LicenceId}" : $"NOT issued — {row.Error}")}");
                }

                return rows.Any(r => r.Error is not null) ? 4 : 0;
            case "certificate":
                string path = a.GetValueOrDefault("out", "certificate.pdf");
                if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllBytes(path, desk.CertificatePdf(Need(a, "licence")));
                }
                else
                {
                    File.WriteAllText(path, desk.Certificate(Need(a, "licence")));
                }

                Console.WriteLine($"Certificate written to {path}.");
                return 0;
            default: // log
                (int entries, int? broken) = desk.VerifyLog();
                if (a.TryGetValue("export", out string? export))
                {
                    File.WriteAllLines(export, desk.ReadLog().Select(e => $"{e.Sequence},{e.At:O},{e.Action},{e.CustomerId},{e.LicenceId},\"{e.Detail.Replace("\"", "'", StringComparison.Ordinal)}\",{e.Hash}")
                        .Prepend("sequence,at,action,customer,licence,detail,hash"));
                }

                Console.WriteLine(broken is null ? $"Issue log intact: {entries} entries." : $"Issue log BROKEN at entry {broken} of {entries}.");
                return broken is null ? 0 : 5;
        }
    }

    private static int Show(IssuedLicence licence, IReadOnlyDictionary<string, string> a)
    {
        string path = a.GetValueOrDefault("out", $"{licence.CustomerId}-{licence.LicenceId[..8]}.lic");
        File.WriteAllText(path, licence.Token);
        Console.WriteLine($"{licence.Action} {licence.LicenceId} for {licence.CustomerId}: {licence.Edition} {licence.LicenceType} {Date(licence.ValidFrom)}..{Date(licence.ValidTo)}");
        Console.WriteLine($"Modules: {string.Join(", ", licence.Modules.Select(m => m.ValidTo == licence.ValidTo ? m.Code : $"{m.Code} (to {Date(m.ValidTo)})"))}");
        Console.WriteLine($"Written to {path}. Activation string:");
        Console.WriteLine(licence.Token);
        return 0;
    }

    private static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual";

    private static string Need(IReadOnlyDictionary<string, string> a, string key)
        => a.TryGetValue(key, out string? value) ? value : throw new ArgumentException($"Missing required argument --{key}.");
}
