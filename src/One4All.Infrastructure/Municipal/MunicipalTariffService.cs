using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <inheritdoc cref="IMunicipalTariffService" />
public sealed class MunicipalTariffService : IMunicipalTariffService
{
    private readonly One4AllDbContext _db;
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="workflow">The approval workflow tariff changes go through when the company defines one.</param>
    public MunicipalTariffService(One4AllDbContext db, Lazy<IWorkflowService>? workflow = null)
    {
        _db = db;
        _workflow = workflow;
    }

    /// <summary>The workflow reference of a tariff version.</summary>
    /// <param name="versionId">The version.</param>
    /// <returns>The reference.</returns>
    public static string Reference(long versionId) => "TV-" + versionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The last day of the latest period billed (posted) for a service, or null when none has been.
    private async Task<DateOnly?> BilledThroughAsync(long companyId, ServiceType service, CancellationToken ct)
        => await _db.Set<BillingRun>().AsNoTracking().Where(r => r.CompanyId == companyId && r.ServiceType == service && r.Status == BillingRunStatus.Posted)
            .MaxAsync(r => (DateOnly?)r.Period, ct).ConfigureAwait(false) is DateOnly period ? period.AddMonths(1).AddDays(-1) : null;

    /// <inheritdoc />
    public Task<Result<long>> SaveVersionAsync(long companyId, TariffInput input, CancellationToken ct = default)
        => _db.AtomicAsync(() => SaveVersionCoreAsync(companyId, input, ct), ct);

    private async Task<Result<long>> SaveVersionCoreAsync(long companyId, TariffInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        string code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            return Error.Validation("Mun.TariffInvalid", "A tariff needs a code.");
        }

        Tariff? previous = await _db.Tariffs.Include(t => t.Blocks).Include(t => t.Seasons).Where(t => t.CompanyId == companyId && t.Code == code && t.EffectiveFrom <= input.EffectiveFrom)
            .OrderByDescending(t => t.EffectiveFrom).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await _db.Tariffs.Include(t => t.Blocks).Include(t => t.Seasons).Where(t => t.CompanyId == companyId && t.Code == code).OrderBy(t => t.EffectiveFrom).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (previous is not null && previous.EffectiveFrom == input.EffectiveFrom)
        {
            return Error.Conflict("Mun.TariffVersionExists", $"Tariff {code} already has a version from {input.EffectiveFrom:yyyy-MM-dd}; choose another date or withdraw it first.");
        }

        if (previous is null && (string.IsNullOrWhiteSpace(input.Name) || input.ServiceType is null || input.TariffType is null || string.IsNullOrWhiteSpace(input.RevenueAccountCode)))
        {
            return Error.Validation("Mun.TariffInvalid", "A new tariff needs a name, a service, a type and a revenue account.");
        }

        string account = (input.RevenueAccountCode ?? previous!.RevenueAccountCode).Trim().ToUpperInvariant();
        if (!await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == account, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Mun.AccountNotFound", $"Revenue account {account} does not exist.");
        }

        if (new[] { input.FixedCharge, input.PerUnitRate, input.MinimumCharge, input.RatePerValuationCents, input.RatePerSquareMetre }.Any(v => v is < 0m)
            || (input.Blocks?.Any(b => b.FromUnit < 0m || b.Rate < 0m || (b.ToUnit is decimal to && to < b.FromUnit)) ?? false)
            || (input.Seasons?.Any(s => s.FromMonth is < 1 or > 12 || s.ToMonth is < 1 or > 12 || s.PerUnitRate < 0m) ?? false))
        {
            return Error.Validation("Mun.TariffInvalid", "Charges, rates and blocks cannot be negative; a block's top is not below its bottom; season months are 1–12.");
        }

        Tariff version = new(companyId, code, (input.Name ?? previous!.Name).Trim(), input.ServiceType ?? previous!.ServiceType, input.TariffType ?? previous!.TariffType, input.EffectiveFrom, account)
        {
            FixedCharge = input.FixedCharge ?? previous?.FixedCharge ?? 0m, PerUnitRate = input.PerUnitRate ?? previous?.PerUnitRate ?? 0m, MinimumCharge = input.MinimumCharge ?? previous?.MinimumCharge ?? 0m,
            RatePerValuationCents = input.RatePerValuationCents ?? previous?.RatePerValuationCents ?? 0m, RatePerSquareMetre = input.RatePerSquareMetre ?? previous?.RatePerSquareMetre ?? 0m,
            Category = input.Category ?? previous?.Category ?? ConsumerCategory.Domestic, Ward = input.Ward is null ? previous?.Ward : (input.Ward.Trim().Length == 0 ? null : input.Ward.Trim()),
        };
        if (previous is not null)
        {
            version.SetCycle(previous.Cycle, previous.CycleStartMonth, previous.Instalments);
        }

        foreach (TariffBlockInput block in input.Blocks ?? previous?.Blocks.Select(b => new TariffBlockInput(b.FromUnit, b.ToUnit, b.Rate)).ToList() ?? [])
        {
            version.AddBlock(block.FromUnit, block.ToUnit, block.Rate);
        }

        foreach (TariffSeasonInput season in input.Seasons ?? previous?.Seasons.Select(s => new TariffSeasonInput(s.FromMonth, s.ToMonth, s.PerUnitRate)).ToList() ?? [])
        {
            version.AddSeason(season.FromMonth, season.ToMonth, season.PerUnitRate);
        }

        // Not back into a billed period: those bills were raised under the version then in force and must stay explainable by it.
        if (await BilledThroughAsync(companyId, version.ServiceType, ct).ConfigureAwait(false) is DateOnly billed && input.EffectiveFrom <= billed)
        {
            return Error.Rule("Mun.TariffBackdated", $"{version.ServiceType} is billed to {billed:yyyy-MM-dd}; a new version of {code} takes effect after that.");
        }

        // With a tariff workflow, the version waits out of force until it is approved.
        bool governed = _workflow is not null && await _db.WorkflowDefinitions.AnyAsync(d => d.CompanyId == companyId && d.DocumentType == WorkflowDocumentTypes.TariffVersion && d.IsActive, ct)
            .ConfigureAwait(false);
        version.IsActive = !governed;
        _db.Tariffs.Add(version);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (governed)
        {
            Result<bool> started = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, companyId, WorkflowDocumentTypes.TariffVersion, Reference(version.Id), 0m, input.RequestedByUserId,
                [("Tariff", code), ("Service", version.ServiceType.ToString())], ct).ConfigureAwait(false);
            if (started.IsFailure)
            {
                return started.Error;
            }
        }

        return version.Id;
    }

    /// <summary>Applies the end of a tariff version's workflow: approved puts it in force; otherwise it stays out of force.</summary>
    internal async Task<Result> ConcludeAsync(long companyId, string reference, bool approved, CancellationToken ct)
    {
        if (!reference.StartsWith("TV-", StringComparison.Ordinal)
            || !long.TryParse(reference.AsSpan(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long id))
        {
            return Result.Failure(Error.Validation("Mun.TariffReference", $"{reference} is not a tariff version reference."));
        }

        Tariff? version = await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (version is null)
        {
            return Result.Failure(Error.NotFound("Mun.TariffVersionNotFound", $"Tariff version {id} not found."));
        }

        version.IsActive = approved;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> WithdrawVersionAsync(long companyId, string code, DateOnly effectiveFrom, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        Tariff? version = await _db.Tariffs.FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == key && t.EffectiveFrom == effectiveFrom, ct).ConfigureAwait(false);
        if (version is null)
        {
            return Result.Failure(Error.NotFound("Mun.TariffVersionNotFound", $"Tariff {key} has no version from {effectiveFrom:yyyy-MM-dd}."));
        }

        // A version bills have been raised under stays: withdrawing it would leave those bills unexplained.
        if (await BilledThroughAsync(companyId, version.ServiceType, ct).ConfigureAwait(false) is DateOnly billed && version.EffectiveFrom <= billed)
        {
            return Result.Failure(Error.Rule("Mun.TariffVersionUsed", $"{version.ServiceType} has been billed to {billed:yyyy-MM-dd} under {key} from {effectiveFrom:yyyy-MM-dd}; it cannot be withdrawn."));
        }

        version.IsActive = false;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Applies the end of a tariff version's workflow.</summary>
internal sealed class TariffVersionWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly MunicipalTariffService _tariffs;

    public TariffVersionWorkflowHandler(MunicipalTariffService tariffs) => _tariffs = tariffs;

    public string DocumentType => WorkflowDocumentTypes.TariffVersion;

    public Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
        => _tariffs.ConcludeAsync(companyId, documentReference, outcome == Core.Workflow.WorkflowStatus.Approved, ct);
}
