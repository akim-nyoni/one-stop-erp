using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Security;
using One4All.Application.Reporting;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Bursts a report by a key and queues each part by email, always as a named user whose rights and scope apply.</summary>
public sealed class ReportBurstService : IReportBurstService
{
    private readonly One4AllDbContext _db;
    private readonly IReportRunner _runner;
    private readonly INotificationService _messages;
    private readonly IPermissionService? _permissions;
    private readonly IDataScopeService? _scopes;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="runner">Runs and splits the report.</param>
    /// <param name="messages">Queues the emails.</param>
    /// <param name="permissions">The running user's report rights.</param>
    /// <param name="scopes">The running user's scope.</param>
    public ReportBurstService(One4AllDbContext db, IReportRunner runner, INotificationService messages, IPermissionService? permissions = null, IDataScopeService? scopes = null)
    {
        _db = db;
        _runner = runner;
        _messages = messages;
        _permissions = permissions;
        _scopes = scopes;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BurstDelivery>>> BurstAsync(BurstRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientField) && request.Ledger is null)
        {
            return Error.Validation("Rpt.BurstRecipients", "Say where each part goes: a field of the rows holding the email, or the ledger whose contacts receive it.");
        }

        // A burst always runs as a user: their report rights, data scope and restricted columns apply to every part.
        if (request.UserId is not long user || _permissions is null)
        {
            return Error.Forbidden("Rpt.BurstOwner", "A burst needs the user it runs as.");
        }

        Core.Security.EffectivePermissions permissions = await _permissions.ResolveAsync(user, ct).ConfigureAwait(false);
        if (!permissions.Has("Reporting.Report", Core.Security.OperationFlags.Export))
        {
            return Error.Forbidden("Rpt.NotAuthorised", $"User {user} may not export reports.");
        }

        ReportAccess access = ReportAccess.For(permissions, _scopes is null ? Application.Security.DataScope.Everything : await _scopes.ForUserAsync(user, ct).ConfigureAwait(false));

        Result<IReadOnlyList<BurstSlice>> slices = await _runner.BurstAsync(request.CompanyId, request.ReportCode.Trim(), request.Parameters, request.Format, request.KeyField, access, ct).ConfigureAwait(false);
        if (slices.IsFailure)
        {
            return slices.Error;
        }

        List<BurstDelivery> deliveries = [];
        foreach (BurstSlice slice in slices.Value)
        {
            IReadOnlyList<string> recipients = await RecipientsAsync(request, slice, ct).ConfigureAwait(false);
            if (recipients.Count == 0)
            {
                deliveries.Add(new BurstDelivery(slice.Key, [], slice.Document.FileName, null));
                continue;
            }

            Result<long> batch = await _messages.QueueDirectAsync(request.CompanyId, $"{request.ReportCode.Trim().ToUpperInvariant()} {slice.Key}", MessageChannel.Email, recipients,
                $"{request.ReportCode.Trim().ToUpperInvariant()} - {slice.Key}", $"Please find attached your {request.ReportCode.Trim().ToUpperInvariant()} ({slice.Key}).",
                new MessageAttachment(slice.Document.FileName, slice.Document.ContentType, slice.Document.Content), ct).ConfigureAwait(false);
            if (batch.IsFailure)
            {
                return batch.Error;
            }

            deliveries.Add(new BurstDelivery(slice.Key, recipients, slice.Document.FileName, batch.Value));
        }

        return deliveries;
    }

    // The address in the rows, else the trader's contacts for the document.
    private async Task<IReadOnlyList<string>> RecipientsAsync(BurstRequest request, BurstSlice slice, CancellationToken ct)
    {
        if (request.RecipientField is string field && field.Trim().Length > 0)
        {
            return [.. slice.Rows.Select(r => Convert.ToString(r.GetValueOrDefault(field.Trim()), System.Globalization.CultureInfo.InvariantCulture)?.Trim())
                .Where(e => !string.IsNullOrEmpty(e) && e.Contains('@', StringComparison.Ordinal)).Select(e => e!).Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        // The key is the trader's code, or (as most reports show it) its name when that is unique.
        string code = slice.Key.Trim().ToUpperInvariant();
        List<long> matches = request.Ledger == TradingLedger.AccountsReceivable
            ? await _db.Customers.Where(c => c.CompanyId == request.CompanyId && c.Code.ToUpper() == code).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false)
            : await _db.Suppliers.Where(s => s.CompanyId == request.CompanyId && s.Code.ToUpper() == code).Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        if (matches.Count == 0)
        {
            matches = request.Ledger == TradingLedger.AccountsReceivable
                ? await _db.Customers.Where(c => c.CompanyId == request.CompanyId && c.Name.ToUpper() == code).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false)
                : await _db.Suppliers.Where(s => s.CompanyId == request.CompanyId && s.Name.ToUpper() == code).Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        }

        long? traderId = matches.Count == 1 ? matches[0] : null;
        return traderId is long id
            ? await Trading.TraderDirectory.RecipientsAsync(_db, request.CompanyId, request.Ledger!.Value, id,
                request.Ledger == TradingLedger.AccountsReceivable ? Application.Abstractions.Trading.TraderDocument.Statement : Application.Abstractions.Trading.TraderDocument.Remittance, ct).ConfigureAwait(false)
            : [];
    }
}
