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
        if (Invalid(setting) is Error invalid)
        {
            return Result.Failure(invalid);
        }

        // Setting replaces the purpose's printers on the workstation with this one; Add keeps the others.
        string workstation = Key(setting.Workstation);
        List<WorkstationPrinter> current = await _db.Set<WorkstationPrinter>()
            .Where(p => p.CompanyId == companyId && p.Workstation == workstation && p.Purpose == setting.Purpose).ToListAsync(ct).ConfigureAwait(false);
        WorkstationPrinter? existing = current.FirstOrDefault();
        _db.Set<WorkstationPrinter>().RemoveRange(current.Skip(1));
        if (existing is null)
        {
            existing = new WorkstationPrinter(companyId, workstation, setting.Purpose, setting.PrinterName.Trim());
            _db.Set<WorkstationPrinter>().Add(existing);
        }

        Apply(existing, setting);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> AddAsync(long companyId, PrinterSetting setting, CancellationToken ct = default)
    {
        if (Invalid(setting) is Error invalid)
        {
            return Result.Failure(invalid);
        }

        string workstation = Key(setting.Workstation);
        string name = setting.PrinterName.Trim();
        WorkstationPrinter? existing = await _db.Set<WorkstationPrinter>()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Workstation == workstation && p.Purpose == setting.Purpose && p.PrinterName == name, ct).ConfigureAwait(false);
        if (existing is null)
        {
            existing = new WorkstationPrinter(companyId, workstation, setting.Purpose, name);
            _db.Set<WorkstationPrinter>().Add(existing);
        }

        Apply(existing, setting);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RemovePrinterAsync(long companyId, string workstation, PrintPurpose purpose, string printerName, CancellationToken ct = default)
    {
        string key = Key(workstation);
        string name = (printerName ?? string.Empty).Trim();
        WorkstationPrinter? existing = await _db.Set<WorkstationPrinter>()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Workstation == key && p.Purpose == purpose && p.PrinterName == name, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Result.Failure(Error.NotFound("Sys.PrinterNotFound", $"{key} has no {purpose} printer \"{name}\"."));
        }

        _db.Set<WorkstationPrinter>().Remove(existing);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrinterSetting>> ResolveAllAsync(long companyId, string workstation, PrintPurpose purpose, CancellationToken ct = default)
    {
        string key = Key(workstation);
        List<WorkstationPrinter> candidates = await _db.Set<WorkstationPrinter>().AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Purpose == purpose && (p.Workstation == key || p.Workstation == WorkstationPrinter.AnyWorkstation)).OrderBy(p => p.Id).ToListAsync(ct)
            .ConfigureAwait(false);
        List<WorkstationPrinter> own = [.. candidates.Where(p => p.Workstation == key)];
        return [.. (own.Count > 0 ? own : candidates).Select(Setting)];
    }

    private static Error? Invalid(PrinterSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (Key(setting.Workstation).Length == 0 || string.IsNullOrWhiteSpace(setting.PrinterName) || setting.Copies is < 1 or > 99 || !Enum.IsDefined(setting.Purpose))
        {
            return Error.Validation("Sys.Printer", "A printer setting needs a workstation (or *), a printer, a known purpose and 1 to 99 copies.");
        }

        return setting.Connection == PrinterConnection.Network && (string.IsNullOrWhiteSpace(setting.Host) || setting.Port is < 1 or > 65535)
            ? Error.Validation("Sys.PrinterAddress", "A network printer needs its address and port (usually 9100).")
            : null;
    }

    private static void Apply(WorkstationPrinter printer, PrinterSetting setting)
    {
        printer.PrinterName = setting.PrinterName.Trim();
        printer.Tray = string.IsNullOrWhiteSpace(setting.Tray) ? null : setting.Tray.Trim();
        printer.Copies = setting.Copies;
        printer.Connection = setting.Connection;
        printer.Host = setting.Connection == PrinterConnection.Network ? setting.Host!.Trim() : null;
        printer.Port = setting.Port;
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(long companyId, string workstation, PrintPurpose purpose, CancellationToken ct = default)
    {
        string key = Key(workstation);
        List<WorkstationPrinter> existing = await _db.Set<WorkstationPrinter>()
            .Where(p => p.CompanyId == companyId && p.Workstation == key && p.Purpose == purpose).ToListAsync(ct).ConfigureAwait(false);
        if (existing.Count == 0)
        {
            return Result.Failure(Error.NotFound("Sys.PrinterNotFound", $"No {purpose} printer is set for {key}."));
        }

        _db.Set<WorkstationPrinter>().RemoveRange(existing);
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

    private static PrinterSetting Setting(WorkstationPrinter p) => new(p.Workstation, p.Purpose, p.PrinterName, p.Tray, p.Copies, p.Connection, p.Host, p.Port);

    private static string Key(string? workstation) => (workstation ?? string.Empty).Trim().ToUpperInvariant();
}
