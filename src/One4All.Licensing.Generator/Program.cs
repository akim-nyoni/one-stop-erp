using System.Text;
using One4All.Licensing.Core;

namespace One4All.Licensing.Generator;

/// <summary>
/// Vendor-only licence key issuer (brief §11.1). Holds the private key (passphrase-protected) and signs
/// licence tokens. NEVER shipped to customers. Commands: <c>keygen</c>, <c>issue</c>, <c>verify</c>,
/// <c>machineid</c>.
/// </summary>
public static class Program
{
    /// <summary>Entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>0 on success, non-zero on error.</returns>
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            return args[0].ToLowerInvariant() switch
            {
                "keygen" => KeyGen(Args(args)),
                "issue" => Issue(Args(args)),
                "verify" => Verify(Args(args)),
                "machineid" => MachineId(),
                string desk when DeskCommands.Names.Contains(desk) => DeskCommands.Run(desk, Args(args), LoadPrivateKey),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static int KeyGen(IReadOnlyDictionary<string, string> a)
    {
        string kid = a.GetValueOrDefault("kid", $"key-{DateTime.UtcNow:yyyy-MM}");
        string outDir = a.GetValueOrDefault("out", ".");
        string? passphrase = a.GetValueOrDefault("passphrase");
        if (string.IsNullOrEmpty(passphrase) && !a.ContainsKey("plaintext"))
        {
            passphrase = Prompt("Passphrase to protect the private key: ");
            if (Prompt("Repeat the passphrase: ") != passphrase || passphrase.Length < 8)
            {
                Console.Error.WriteLine("The passphrases differ or are shorter than 8 characters; nothing was written.");
                return 1;
            }
        }

        Directory.CreateDirectory(outDir);

        Ed25519KeyPair pair = Ed25519KeyPair.Generate();

        string pubPath = Path.Combine(outDir, $"{kid}.pub.txt");
        File.WriteAllText(pubPath, pair.PublicKeyBase64);

        string privPath = Path.Combine(outDir, $"{kid}.key.json");
        if (!string.IsNullOrEmpty(passphrase))
        {
            File.WriteAllText(privPath, ProtectedPrivateKey.Protect(pair.PrivateKeyBase64, passphrase).ToJson());
            Console.WriteLine($"Private key (encrypted) : {privPath}");
        }
        else
        {
            File.WriteAllText(privPath, pair.PrivateKeyBase64);
            Console.WriteLine($"Private key (PLAINTEXT - protect with --passphrase!) : {privPath}");
        }

        Console.WriteLine($"Key id                  : {kid}");
        Console.WriteLine($"Public key (embed in ERP): {pubPath}");
        Console.WriteLine();
        Console.WriteLine(pair.PublicKeyBase64);
        return 0;
    }

    private static int Issue(IReadOnlyDictionary<string, string> a)
    {
        string keyPath = Require(a, "key");
        string templatePath = Require(a, "template");
        string outPath = a.GetValueOrDefault("out", "licence.lic");
        string kid = a.GetValueOrDefault("kid", Path.GetFileNameWithoutExtension(keyPath).Replace(".key", string.Empty));
        string? passphrase = a.GetValueOrDefault("passphrase");

        string privateKeyBase64 = LoadPrivateKey(keyPath, passphrase);

        LicensePayload payload = CanonicalJson.Deserialize(File.ReadAllText(templatePath))
            ?? throw new InvalidOperationException("Template is not a valid licence payload.");
        payload.KeyId = kid;
        if (payload.IssuedAt == default)
        {
            payload.IssuedAt = DateTimeOffset.UtcNow;
        }

        string token = new LicenseSigner(privateKeyBase64).Sign(payload);
        File.WriteAllText(outPath, token);

        Console.WriteLine($"Licence issued          : {outPath}");
        Console.WriteLine($"Customer                : {payload.Customer.Name}");
        Console.WriteLine($"Edition / type          : {payload.Edition} / {payload.LicenceType}");
        Console.WriteLine($"Valid                   : {payload.ValidFrom} .. {(payload.ValidTo?.ToString() ?? "perpetual")}");
        Console.WriteLine($"Modules                 : {string.Join(", ", payload.Modules.Select(m => m.Code))}");
        return 0;
    }

    private static int Verify(IReadOnlyDictionary<string, string> a)
    {
        string licPath = Require(a, "lic");
        string pubPath = Require(a, "pub");
        string kid = a.GetValueOrDefault("kid", Path.GetFileNameWithoutExtension(pubPath).Replace(".pub", string.Empty));
        string product = a.GetValueOrDefault("product", "One4All ERP");

        string publicKey = File.ReadAllText(pubPath).Trim();
        LicenseVerifier verifier = LicenseVerifier.ForSingleKey(kid, publicKey);
        LicenseValidator validator = new(verifier, new SystemClock(), product);

        LicenseValidationResult result = validator.Validate(File.ReadAllText(licPath).Trim());
        Console.WriteLine($"Status : {result.Status}");
        Console.WriteLine($"Message: {result.Message}");
        if (result.Payload is not null)
        {
            Console.WriteLine($"Customer: {result.Payload.Customer.Name}");
            Console.WriteLine($"Modules : {string.Join(", ", result.Payload.Modules.Select(m => m.Code))}");
        }

        return result.Status is LicenseStatus.Valid or LicenseStatus.Grace ? 0 : 3;
    }

    private static int MachineId()
    {
        MachineFingerprint fingerprint = MachineFingerprint.FromComponents(HostMachineId.GatherComponents());
        Console.WriteLine(fingerprint.ToString());
        return 0;
    }

    // Reads a secret from the console without echoing it.
    private static string Prompt(string label)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException($"{label.TrimEnd(' ', ':')} is needed: supply it with --passphrase.");
        }

        Console.Write(label);
        StringBuilder typed = new();
        for (ConsoleKeyInfo key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(intercept: true))
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                typed.Length = Math.Max(0, typed.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }

        Console.WriteLine();
        return typed.ToString();
    }

    private static string LoadPrivateKey(string keyPath, string? passphrase)
    {
        string content = File.ReadAllText(keyPath).Trim();
        if (content.StartsWith('{'))
        {
            ProtectedPrivateKey protectedKey = ProtectedPrivateKey.FromJson(content)
                ?? throw new InvalidOperationException("Key file is not a valid protected key.");
            if (string.IsNullOrEmpty(passphrase))
            {
                passphrase = Console.IsInputRedirected ? throw new InvalidOperationException("The private key is encrypted; supply --passphrase.") : Prompt("Key passphrase: ");
            }

            return protectedKey.Unprotect(passphrase);
        }

        return content;
    }

    private static Dictionary<string, string> Args(string[] args)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                string key = args[i][2..];
                string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : "true";
                map[key] = value;
            }
        }

        return map;
    }

    private static string Require(IReadOnlyDictionary<string, string> a, string key) =>
        a.TryGetValue(key, out string? value)
            ? value
            : throw new ArgumentException($"Missing required argument --{key}.");

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("One4All Licence Generator (vendor only)");
        Console.WriteLine();
        Console.WriteLine("  keygen    --kid <id> --out <dir> [--passphrase <pass>]");
        Console.WriteLine("  issue     --key <keyfile> [--passphrase <pass>] --template <payload.json> --out <file.lic> [--kid <id>]");
        Console.WriteLine("  verify    --lic <file.lic> --pub <pubfile> [--kid <id>] [--product \"One4All ERP\"]");
        Console.WriteLine("  machineid");
        Console.WriteLine();
        Console.WriteLine("Licence desk (all take --key <keyfile> [--passphrase <pass>] [--register <folder>]):");
        Console.WriteLine("  customer    --id <ref> --name <name> [--contact <c>] [--sites \"a;b\"]      customers");
        Console.WriteLine("  grant       --customer <ref> [--edition Standard] [--type Subscription] [--term 1y|6m|30d|yyyy-MM-dd|perpetual]");
        Console.WriteLine("              [--modules \"GL AR\"] [--module-terms \"POS=6m,PAY=2026-12-31\"] [--fingerprint sha256:..|--request O4A-REQ-..] [--grace 14] [--hard-stop] [--out f.lic]");
        Console.WriteLine("  renew       --licence <id> --term <term>          change  --licence <id> [--edition E] [--add \"A B\"] [--remove \"C\"]");
        Console.WriteLine("  reissue     --licence <id> --fingerprint <fp>|--request <code>");
        Console.WriteLine("  revoke      --licence <id> --reason <why>         crl [--out file.crl]   (signed revocation list)");
        Console.WriteLine("  expiring    [--days 30]                            history --customer <ref>");
        Console.WriteLine("  bulk        --csv <customer_id,customer_name,edition,type,term[,modules]>");
        Console.WriteLine("  update      --version <x.y.z> --file <setup.exe> --url <where installs download it> [--minimum <x.y.z>] [--notes ...] [--out update.manifest]");
        Console.WriteLine("  certificate --licence <id> [--out cert.pdf|.html]     log [--export log.csv]   (verifies the hash chain)");
        Console.WriteLine("  request     [--customer <ref>] [--site <name>]     (offline activation request code for this machine)");
    }
}
