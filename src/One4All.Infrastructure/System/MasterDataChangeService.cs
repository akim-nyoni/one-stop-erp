using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using One4All.Licensing.Core;
using One4All.Application.Abstractions.System;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Workflow;

namespace One4All.Infrastructure.SystemServices;

/// <summary>Master-data changes held for approval and applied once approved.</summary>
public sealed class MasterDataChangeService : IMasterDataChangeService
{
    /// <summary>The workflow document type the changes go through.</summary>
    public const string DocumentType = "MasterDataChange";

    // The fields each record allows changing this way — public settable properties only.
    private static readonly Dictionary<MasterDataTarget, string[]> Allowed = new()
    {
        [MasterDataTarget.Tariff] = ["FixedCharge", "PerUnitRate", "RatePerValuationCents", "RatePerSquareMetre", "MinimumCharge", "IsActive"],
        [MasterDataTarget.Customer] = ["Name", "PaymentTermsDays", "SettlementDiscountPercent", "SettlementDiscountDays", "OnHold", "InterestExempt", "TaxRegistration", "Email", "IsActive"],
        [MasterDataTarget.Supplier] = ["Name", "PaymentTermsDays", "OnHold", "TaxRegistration", "TaxClearanceExpiry", "IsActive"],
        [MasterDataTarget.Item] = ["Description", "TaxCode", "StandardCost", "CostOfSalesAccountCode", "RevenueAccountCode", "IsActive"],
    };

    private readonly One4AllDbContext _db;
    private readonly IClock _clock;
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="workflow">The approval workflow (optional).</param>
    public MasterDataChangeService(One4AllDbContext db, IClock clock, Lazy<IWorkflowService>? workflow = null)
    {
        _db = db;
        _clock = clock;
        _workflow = workflow;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Fields(MasterDataTarget target) => Allowed.TryGetValue(target, out string[]? fields) ? fields : [];

    /// <inheritdoc />
    public Task<Result<MasterDataChangeRow>> RequestAsync(long companyId, MasterDataTarget target, string key, string field, string? newValue, long userId, CancellationToken ct = default)
        => _db.AtomicAsync(() => RequestCoreAsync(companyId, target, key, field, newValue, userId, ct), ct);

    private async Task<Result<MasterDataChangeRow>> RequestCoreAsync(long companyId, MasterDataTarget target, string key, string field, string? newValue, long userId, CancellationToken ct)
    {
        string code = (key ?? string.Empty).Trim().ToUpperInvariant();
        string? name = Fields(target).FirstOrDefault(f => string.Equals(f, (field ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return Error.Validation("Mdc.Field", $"{target} changes may set only: {string.Join(", ", Fields(target))}.");
        }

        if (await RecordAsync(companyId, target, code, ct).ConfigureAwait(false) is not object record)
        {
            return Error.NotFound("Mdc.NotFound", $"{target} {code} not found.");
        }

        PropertyInfo property = record.GetType().GetProperty(name)!;
        if (Parse(property.PropertyType, newValue) is not { IsSuccess: true } parsed)
        {
            return Error.Validation("Mdc.Value", $"'{newValue}' is not a valid {name}.");
        }

        if (await _db.MasterDataChanges.AnyAsync(c => c.CompanyId == companyId && c.Target == target && c.Key == code && c.Field == name && c.Status == MasterDataChangeStatus.Pending, ct)
            .ConfigureAwait(false))
        {
            return Error.Conflict("Mdc.Pending", $"A change to {target} {code} {name} is already waiting for approval.");
        }

        MasterDataChange change = new(companyId, target, code, name, Text(property.GetValue(record)), Text(parsed.Value), userId, _clock.UtcNow);
        _db.MasterDataChanges.Add(change);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        change.Reference = $"MDC-{change.Id}";
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        Result<bool> governed = await WorkflowGate.StartIfDefinedAsync(_db, _workflow, companyId, DocumentType, change.Reference, 0m, userId, ct).ConfigureAwait(false);
        if (governed.IsFailure)
        {
            return governed.Error;
        }

        if (!governed.Value)
        {
            Result applied = await ApplyAsync(change, userId, ct).ConfigureAwait(false);
            if (applied.IsFailure)
            {
                return applied.Error;
            }
        }

        return Row(change);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MasterDataChangeRow>> ListAsync(long companyId, CancellationToken ct = default)
        => [.. (await _db.MasterDataChanges.AsNoTracking().Where(c => c.CompanyId == companyId).OrderByDescending(c => c.Id).Take(500).ToListAsync(ct).ConfigureAwait(false)).Select(Row)];

    /// <summary>Applies or rejects a change on its workflow's outcome.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The change's reference.</param>
    /// <param name="approved">Whether it was approved.</param>
    /// <param name="userId">Who decided.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    internal async Task<Result> ConcludeAsync(long companyId, string reference, bool approved, long userId, CancellationToken ct)
    {
        MasterDataChange? change = await _db.MasterDataChanges.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Reference == reference, ct).ConfigureAwait(false);
        if (change is not { Status: MasterDataChangeStatus.Pending })
        {
            return Result.Failure(Error.NotFound("Mdc.NotPending", $"No pending master-data change {reference}."));
        }

        if (!approved)
        {
            change.Rejected(userId, _clock.UtcNow);
            return Result.Success();
        }

        return await ApplyAsync(change, userId, ct).ConfigureAwait(false);
    }

    // Sets the field (the record may have changed since the request; the value replaced is what it holds now).
    private async Task<Result> ApplyAsync(MasterDataChange change, long userId, CancellationToken ct)
    {
        if (await RecordAsync(change.CompanyId, change.Target, change.Key, ct).ConfigureAwait(false) is not object record)
        {
            return Result.Failure(Error.NotFound("Mdc.NotFound", $"{change.Target} {change.Key} no longer exists."));
        }

        PropertyInfo property = record.GetType().GetProperty(change.Field)!;
        Result<object?> value = Parse(property.PropertyType, change.NewValue);
        if (value.IsFailure)
        {
            return Result.Failure(value.Error);
        }

        string? before = Text(property.GetValue(record));
        property.SetValue(record, value.Value);
        change.Applied(before, userId, _clock.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<object?> RecordAsync(long companyId, MasterDataTarget target, string code, CancellationToken ct) => target switch
    {
        MasterDataTarget.Tariff => await _db.Tariffs.Where(t => t.CompanyId == companyId && t.Code == code).OrderByDescending(t => t.EffectiveFrom).FirstOrDefaultAsync(ct).ConfigureAwait(false),
        MasterDataTarget.Customer => await _db.Customers.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Code == code, ct).ConfigureAwait(false),
        MasterDataTarget.Supplier => await _db.Suppliers.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Code == code, ct).ConfigureAwait(false),
        MasterDataTarget.Item => await _db.Set<Core.Inventory.Item>().FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Code == code, ct).ConfigureAwait(false),
        _ => null,
    };

    // The text a value is entered as, turned into the field's type.
    private static Result<object?> Parse(Type type, string? text)
    {
        Type? underlying = Nullable.GetUnderlyingType(type);
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return underlying is not null || type == typeof(string) ? Result<object?>.Success(null) : Error.Validation("Mdc.Value", "A value is required.");
        }

        Type target = underlying ?? type;
        CultureInfo invariant = CultureInfo.InvariantCulture;
        object? value = target == typeof(string) ? trimmed
            : target == typeof(decimal) && decimal.TryParse(trimmed, NumberStyles.Number, invariant, out decimal d) ? d
            : target == typeof(int) && int.TryParse(trimmed, NumberStyles.Integer, invariant, out int n) ? n
            : target == typeof(bool) && bool.TryParse(trimmed, out bool b) ? b
            : target == typeof(DateOnly) && DateOnly.TryParse(trimmed, invariant, out DateOnly date) ? date
            : null;
        return value is null ? Error.Validation("Mdc.Value", $"'{trimmed}' is not a valid {target.Name}.") : Result<object?>.Success(value);
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static MasterDataChangeRow Row(MasterDataChange c) => new(c.Reference, c.Target, c.Key, c.Field, c.OldValue, c.NewValue, c.Status, c.RequestedOnUtc);
}

/// <summary>Applies or rejects a master-data change when its workflow ends.</summary>
internal sealed class MasterDataChangeWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly MasterDataChangeService _changes;

    /// <summary>Creates the handler.</summary>
    /// <param name="changes">The change service.</param>
    public MasterDataChangeWorkflowHandler(MasterDataChangeService changes) => _changes = changes;

    /// <inheritdoc />
    public string DocumentType => MasterDataChangeService.DocumentType;

    /// <inheritdoc />
    public Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
        => _changes.ConcludeAsync(companyId, documentReference, outcome == Core.Workflow.WorkflowStatus.Approved, decidedByUserId, ct);
}
