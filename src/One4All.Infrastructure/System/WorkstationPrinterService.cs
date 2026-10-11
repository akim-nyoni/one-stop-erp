using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.System;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SystemServices;

/// <summary>Printers per workstation.</summary>
public sealed class WorkstationPrinterService : IWorkstationPrinterService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public WorkstationPrinterService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetAsync(long companyId, PrinterSetting setting, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        string workstation = Key(setting.Workstation);
        if (workstation.Length == 0 || string.IsNullOrWhiteSpace(setting.PrinterName) || setting.Copies is < 1 or > 99 || !Enum.IsDefined(setting.Purpose))
        {
            return Result.Failure(Error.Validation("Sys.Printer", "A printer setting needs a workstation (or *), a printer, a known purpose and 1 to 99 copies."));
        }

        WorkstationPrinter? existing = await _db.Set<WorkstationPrinter>()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Workstation == workstation && p.Purpose == setting.Purpose, ct).ConfigureAwait(false);
        if (existing is null)
        {
            existing = new WorkstationPrinter(companyId, workstation, setting.Purpose, setting.PrinterName.Trim());
            _db.Set<WorkstationPrinter>().Add(existing);
        }

        existing.PrinterName = setting.PrinterName.Trim();
        existing.Tray = string.IsNullOrWhiteSpace(setting.Tray) ? null : setting.Tray.Trim();
        existing.Copies = setting.Copies;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(long companyId, string workstation, PrintPurpose purpose, CancellationToken ct = default)
    {
        string key = Key(workstation);
        WorkstationPrinter? existing = await _db.Set<WorkstationPrinter>()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Workstation == key && p.Purpose == purpose, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Result.Failure(Error.NotFound("Sys.PrinterNotFound", $"No {purpose} printer is set for {key}."));
        }

        _db.Set<WorkstationPrinter>().Remove(existing);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<PrinterSetting?> ResolveAsync(long companyId, string workstation, PrintPurpose purpose, CancellationToken ct = default)
    {
        string key = Key(workstation);
        List<WorkstationPrinter> candidates = await _db.Set<WorkstationPrinter>().AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Purpose == purpose && (p.Workstation == key || p.Workstation == WorkstationPrinter.AnyWorkstation)).ToListAsync(ct).ConfigureAwait(false);
        WorkstationPrinter? chosen = candidates.FirstOrDefault(p => p.Workstation == key) ?? candidates.FirstOrDefault();
        return chosen is null ? null : Setting(chosen);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrinterSetting>> ListAsync(long companyId, CancellationToken ct = default)
        => [.. (await _db.Set<WorkstationPrinter>().AsNoTracking().Where(p => p.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(p => p.Workstation, StringComparer.Ordinal).ThenBy(p => p.Purpose).Select(Setting)];

    private static PrinterSetting Setting(WorkstationPrinter p) => new(p.Workstation, p.Purpose, p.PrinterName, p.Tray, p.Copies);

    private static string Key(string? workstation) => (workstation ?? string.Empty).Trim().ToUpperInvariant();
}
