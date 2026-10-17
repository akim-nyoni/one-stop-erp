using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Restaurant;
using One4All.Application.Restaurant;
using One4All.Core.Common;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Pos;

namespace One4All.Infrastructure.Restaurant;

/// <summary>Sends kitchen order tickets to their stations' printers.</summary>
public sealed class KitchenPrintService : IKitchenPrintService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public KitchenPrintService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetStationPrinterAsync(long companyId, StationPrinterSetting setting, bool remove = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        string station = Station(setting.Station);
        KitchenStationPrinter? existing = await _db.Set<KitchenStationPrinter>().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Station == station, ct).ConfigureAwait(false);
        if (remove)
        {
            if (existing is not null)
            {
                _db.Remove(existing);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return Result.Success();
        }

        bool byName = !string.IsNullOrWhiteSpace(setting.PrinterName);
        bool byHost = !string.IsNullOrWhiteSpace(setting.Host);
        if (byName == byHost || setting.Port is < 1 or > 65535 || setting.Copies is < 1 or > 5)
        {
            return Result.Failure(Error.Validation("Res.StationPrinter", "A station printer is a Windows printer name or a network host (not both), with a valid port and 1 to 5 copies."));
        }

        existing ??= _db.Set<KitchenStationPrinter>().Add(new KitchenStationPrinter(companyId, station)).Entity;
        existing.PrinterName = byName ? setting.PrinterName!.Trim() : null;
        existing.Host = byHost ? setting.Host!.Trim() : null;
        existing.Port = setting.Port;
        existing.Copies = setting.Copies;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StationPrinterSetting>> StationPrintersAsync(long companyId, CancellationToken ct = default)
        => [.. (await _db.Set<KitchenStationPrinter>().AsNoTracking().Where(p => p.CompanyId == companyId).OrderBy(p => p.Station).ToListAsync(ct).ConfigureAwait(false))
            .Select(p => new StationPrinterSetting(p.Station, p.PrinterName, p.Host, p.Port, p.Copies))];

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<KitchenPrintOutcome>>> PrintAsync(long companyId, string ticketNumber, CancellationToken ct = default)
    {
        string number = (ticketNumber ?? string.Empty).Trim();
        string prefix = number + "-";
        List<KitchenTicket> tickets = await _db.Set<KitchenTicket>().Include(t => t.Lines)
            .Where(t => t.CompanyId == companyId && (t.TicketNumber == number || t.TicketNumber.StartsWith(prefix))).OrderBy(t => t.TicketNumber).ToListAsync(ct).ConfigureAwait(false);
        if (number.Length == 0 || tickets.Count == 0)
        {
            return Error.NotFound("Res.TicketNotFound", $"No kitchen ticket {number}.");
        }

        Dictionary<string, KitchenStationPrinter> printers = await _db.Set<KitchenStationPrinter>().AsNoTracking().Where(p => p.CompanyId == companyId)
            .ToDictionaryAsync(p => p.Station, ct).ConfigureAwait(false);
        List<long> orderIds = [.. tickets.Select(t => t.TableOrderId).Distinct()];
        var orders = await _db.Set<TableOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderNumber, o.OrderType, o.CustomerName, Table = _db.Set<RestaurantTable>().Where(t => t.Id == o.TableId).Select(t => t.TableNumber).FirstOrDefault() })
            .ToDictionaryAsync(o => o.Id, ct).ConfigureAwait(false);

        List<KitchenPrintOutcome> outcomes = [];
        foreach (KitchenTicket ticket in tickets)
        {
            string station = ticket.Station is null ? KitchenStationPrinter.MainKitchen : Station(ticket.Station);
            var order = orders[ticket.TableOrderId];
            string where = order.Table is string table ? $"Table {table}" : $"{order.OrderType}{(order.CustomerName is null ? string.Empty : " - " + order.CustomerName)}";
            KitchenTicketDocument document = new(station, ticket.TicketNumber, order.OrderNumber, where, ticket.Course?.ToString(), ticket.CreatedOnUtc,
                [.. ticket.Lines.Select(l => (l.Quantity, l.Note is null ? l.Description : $"{l.Description} ** {l.Note}"))], ticket.PrintCount > 0);
            string text = KitchenTicketFormatter.ToPlainText(document);
            if (!printers.TryGetValue(station, out KitchenStationPrinter? printer))
            {
                outcomes.Add(new KitchenPrintOutcome(ticket.TicketNumber, station, null, null, text));
                continue;
            }

            string target = printer.PrinterName ?? $"{printer.Host}:{printer.Port}";
            string? error = null;
            byte[] bytes = KitchenTicketFormatter.ToEscPos(document);
            for (int copy = 0; copy < printer.Copies && error is null; copy++)
            {
                error = await Device(printer) is IReceiptPrinter device ? await device.PrintAsync(bytes, ct).ConfigureAwait(false) : "Windows printers print only on Windows.";
            }

            if (error is null)
            {
                ticket.MarkPrinted(DateTimeOffset.UtcNow);
            }

            outcomes.Add(new KitchenPrintOutcome(ticket.TicketNumber, station, target, error, text));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return outcomes;
    }

    private static string Station(string? station) => string.IsNullOrWhiteSpace(station) ? KitchenStationPrinter.MainKitchen : station.Trim().ToUpperInvariant();

    private static Task<IReceiptPrinter?> Device(KitchenStationPrinter printer)
    {
        if (printer.Host is string host)
        {
            return Task.FromResult<IReceiptPrinter?>(new NetworkRawPrinter(new DeviceSettings { Connection = DeviceConnection.Network, Host = host, Port = printer.Port }));
        }

        return Task.FromResult<IReceiptPrinter?>(OperatingSystem.IsWindows() ? new WindowsRawPrinter(printer.PrinterName!) : null);
    }
}
