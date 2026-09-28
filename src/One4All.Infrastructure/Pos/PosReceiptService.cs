using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Common;
using One4All.Core.Pos;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Pos;

/// <summary>Builds a <see cref="PosReceiptModel"/> from a stored sale and formats it for printing.</summary>
public sealed class PosReceiptService : IPosReceiptService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PosReceiptService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<string>> RenderReceiptTextAsync(long companyId, long saleId, int width = PosReceiptFormatter.DefaultWidth, CancellationToken ct = default)
    {
        Sale? sale = await _db.Set<Sale>().Include(s => s.Lines).Include(s => s.Tenders)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.CompanyId == companyId, ct)
            .ConfigureAwait(false);
        if (sale is null)
        {
            return Error.NotFound("Pos.SaleNotFound", $"Sale {saleId} not found.");
        }

        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        string cashierName = await _db.Set<User>().Where(u => u.Id == sale.CashierUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        string terminalName = await _db.Set<PosTerminal>().Where(t => t.Id == sale.TerminalId).Select(t => t.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        PosReceiptModel model = new()
        {
            CompanyName = companyName,
            ReceiptNumber = sale.SaleNumber,
            SoldOn = sale.SaleDate.ToDateTime(TimeOnly.MinValue),
            CashierName = cashierName,
            TerminalName = terminalName,
            Lines = sale.Lines
                .Select(l => new PosReceiptLine(l.Description, l.Quantity, l.LineNet + l.TaxAmount))
                .ToList(),
            TotalNet = sale.TotalNet,
            TotalTax = sale.TotalTax,
            TotalGross = sale.TotalGross,
            Tenders = sale.Tenders
                .Where(t => !t.IsChange)
                .Select(t => new PosReceiptTender(t.TenderType.ToString(), t.BaseAmount))
                .ToList(),
            Change = sale.Tenders.Where(t => t.IsChange).Sum(t => t.BaseAmount),
        };

        return PosReceiptFormatter.ToPlainText(model, width);
    }
}
