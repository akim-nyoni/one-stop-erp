using System.Globalization;
using One4All.Application.Abstractions.Licensing;
using One4All.Licensing.Core;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>The licence status screen (brief §11.5): status, install a licence or revocation list, and the offline activation request code.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Licence()
    {
        const string module = "System Manager";
        yield return new Operation
        {
            Module = module, Title = "Licence", ListTitle = "Licensed modules",
            Description = "Shows the licence; installs a new licence or the vendor's revocation list (paste the text or give the file path); or gives this machine's activation request code to send to the vendor.",
            Fields = [Pick("Action", "Action", ["Show status", "Install licence", "Install revocation list", "Activation request code", "Activate online", "Renew online"]), Opt("Token", "Licence / list text or file path"),
                Opt("Customer", "Customer reference (request code)")],
            Run = async (c, i) =>
            {
                ILicenceService licence = c.Get<ILicenceService>();
                string? text = i.Optional("Token") is string given && System.IO.File.Exists(given) ? (await System.IO.File.ReadAllTextAsync(given).ConfigureAwait(true)).Trim() : i.Optional("Token");
                switch (i.Text("Action"))
                {
                    case "Install licence":
                        LicenseValidationResult installed = await licence.InstallAsync(text ?? throw new OperationException("Paste the licence or give its file.")).ConfigureAwait(true);
                        return $"{installed.Status}: {installed.Message}";
                    case "Install revocation list":
                        int revoked = Check(await licence.InstallRevocationListAsync(text ?? throw new OperationException("Paste the revocation list or give its file.")).ConfigureAwait(true));
                        return $"Revocation list installed ({revoked} licence(s) revoked). This licence: {licence.Current.Status}.";
                    case "Activate online":
                    case "Renew online":
                        IOnlineActivationClient online = c.Get<IOnlineActivationClient>();
                        OnlineActivationRequest request = new(new LicenceRequestCode(licence.MachineFingerprint, i.Optional("Customer"), Environment.MachineName, DateTimeOffset.UtcNow).Encode(),
                            text, licence.Current.Payload?.LicenceId);
                        OnlineActivationResponse answer = i.Text("Action") == "Renew online"
                            ? await online.RenewAsync(request).ConfigureAwait(true)
                            : await online.ActivateAsync(request).ConfigureAwait(true);
                        if (!answer.Granted || answer.LicenceToken is null)
                        {
                            return answer.Message ?? "Not granted.";
                        }

                        if (answer.RevocationListToken is string list)
                        {
                            _ = await licence.InstallRevocationListAsync(list).ConfigureAwait(true);
                        }

                        LicenseValidationResult activated = await licence.InstallAsync(answer.LicenceToken).ConfigureAwait(true);
                        return $"{activated.Status}: {activated.Message}" + (answer.Message is null ? string.Empty : $" ({answer.Message})");
                    case "Activation request code":
                        string code = new LicenceRequestCode(licence.MachineFingerprint, i.Optional("Customer"), Environment.MachineName, DateTimeOffset.UtcNow).Encode();
                        System.Windows.Clipboard.SetText(code);
                        return $"Copied to the clipboard — send it to your vendor:{Environment.NewLine}{code}";
                    default:
                        LicensePayload? p = licence.Current.Payload;
                        return string.Join(Environment.NewLine, new[]
                        {
                            $"Status: {licence.Current.Status} — {licence.Current.Message}",
                            p is null ? "No licence." : $"Customer: {p.Customer.Name} ({p.Customer.Id}) · {p.Edition} {p.LicenceType} · licence {p.LicenceId}",
                            p is null ? string.Empty : $"Valid {p.ValidFrom:yyyy-MM-dd} to {p.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual"}, grace {p.GraceDays} day(s)",
                            p is null || p.Limits.Count == 0 ? string.Empty : "Limits: " + string.Join(", ", p.Limits.Select(l => $"{l.Key} {l.Value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited"}")),
                            licence.Warning ?? string.Empty,
                            $"Machine: {licence.MachineFingerprint}",
                            "Usage: " + string.Join(", ", (await c.Get<ILicenceUsageService>().UsageAsync().ConfigureAwait(true))
                                .Select(u => $"{u.Key} {u.Used}{(u.Limit is int limit ? $"/{limit}" + (u.PercentUsed >= 90m ? " (nearly full)" : string.Empty) : string.Empty)}")),
                        }.Where(l => l.Length > 0));
                }
            },
            List = c =>
            {
                ILicenceService licence = c.Get<ILicenceService>();
                DateOnly today = DateOnly.FromDateTime(DateTime.Today);
                return Task.FromResult(Table(licence.Current.Payload?.Modules ?? [], ("Module", m => m.Code), ("Valid to", m => m.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "with the licence"),
                    ("Days left", m => m.ValidTo is DateOnly to ? to.DayNumber - today.DayNumber : (int?)null), ("Usable", m => licence.IsModuleLicensed(m.Code)),
                    ("Limits", m => string.Join(", ", m.Limits.Select(l => $"{l.Key} {l.Value}")))));
            },
        };
    }
}
