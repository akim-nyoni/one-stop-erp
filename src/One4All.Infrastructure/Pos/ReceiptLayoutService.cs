using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Common;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Pos;

/// <summary>Receipt layouts per company and till, and their preview.</summary>
public sealed class ReceiptLayoutService : IReceiptLayoutService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ReceiptLayoutService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SaveAsync(long companyId, ReceiptLayoutInput layout, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.PaperWidthMm is not (58 or 80) || layout.Copies is < 1 or > 5)
        {
            return Result.Failure(Error.Validation("Pos.ReceiptLayout", "A receipt layout is for 58mm or 80mm paper and prints 1 to 5 copies."));
        }

        Result<long?> terminal = await TerminalAsync(companyId, layout.TerminalCode, ct).ConfigureAwait(false);
        if (terminal.IsFailure)
        {
            return Result.Failure(terminal.Error);
        }

        ReceiptLayout? saved = await _db.ReceiptLayouts.FirstOrDefaultAsync(l => l.CompanyId == companyId && l.TerminalId == terminal.Value, ct).ConfigureAwait(false);
        if (saved is null)
        {
            saved = new ReceiptLayout(companyId, terminal.Value);
            _db.ReceiptLayouts.Add(saved);
        }

        saved.PaperWidthMm = layout.PaperWidthMm;
        saved.Title = (layout.Title ?? string.Empty).Trim();
        saved.HeaderText = Lines(layout.Header);
        saved.FooterText = Lines(layout.Footer);
        (saved.ShowCashier, saved.ShowTerminal, saved.ShowUnitPrice, saved.ShowTaxSummary, saved.ShowTaxNumber, saved.ShowFiscal, saved.Copies) =
            (layout.ShowCashier, layout.ShowTerminal, layout.ShowUnitPrice, layout.ShowTaxSummary, layout.ShowTaxNumber, layout.ShowFiscal, layout.Copies);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(long companyId, string terminalCode, CancellationToken ct = default)
    {
        Result<long?> terminal = await TerminalAsync(companyId, terminalCode, ct).ConfigureAwait(false);
        if (terminal.IsFailure || terminal.Value is null)
        {
            return Result.Failure(terminal.IsFailure ? terminal.Error : Error.Validation("Pos.ReceiptLayoutTill", "Name the till whose own layout to remove."));
        }

        ReceiptLayout? saved = await _db.ReceiptLayouts.FirstOrDefaultAsync(l => l.CompanyId == companyId && l.TerminalId == terminal.Value, ct).ConfigureAwait(false);
        if (saved is not null)
        {
            _db.ReceiptLayouts.Remove(saved);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<ReceiptOptions> OptionsAsync(long companyId, long? terminalId, CancellationToken ct = default)
    {
        List<ReceiptLayout> layouts = await _db.ReceiptLayouts.AsNoTracking().Where(l => l.CompanyId == companyId && (l.TerminalId == null || l.TerminalId == terminalId))
            .ToListAsync(ct).ConfigureAwait(false);
        ReceiptLayout? chosen = layouts.FirstOrDefault(l => l.TerminalId is not null && l.TerminalId == terminalId) ?? layouts.FirstOrDefault(l => l.TerminalId is null);
        return chosen is null ? new ReceiptOptions(ReceiptOptions.WidthFor(80)) : Options(chosen);
    }

    /// <inheritdoc />
    public async Task<Result<string>> PreviewAsync(long companyId, ReceiptLayoutInput? layout, string? terminalCode = null, CancellationToken ct = default)
    {
        ReceiptOptions options;
        if (layout is null)
        {
            Result<long?> terminal = await TerminalAsync(companyId, terminalCode, ct).ConfigureAwait(false);
            if (terminal.IsFailure)
            {
                return terminal.Error;
            }

            options = await OptionsAsync(companyId, terminal.Value, ct).ConfigureAwait(false);
        }
        else
        {
            options = new ReceiptOptions(ReceiptOptions.WidthFor(layout.PaperWidthMm), (layout.Title ?? string.Empty).Trim(), Split(Lines(layout.Header)), Split(Lines(layout.Footer)),
                layout.ShowCashier, layout.ShowTerminal, layout.ShowUnitPrice, layout.ShowTaxSummary, layout.ShowTaxNumber, layout.ShowFiscal, layout.Copies);
        }

        var company = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => new { c.Name, c.TaxNumber }).FirstAsync(ct).ConfigureAwait(false);
        PosReceiptModel sample = new()
        {
            CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, ReceiptNumber = "POS1-SAMPLE-0001", SoldOn = DateTime.Today.AddHours(10).AddMinutes(30), CashierName = "Cashier",
            TerminalName = terminalCode ?? "Till 1",
            Lines = [new PosReceiptLine("Bread, white 700g", 2m, 3.00m, 1.50m), new PosReceiptLine("Milk 2L", 1m, 2.40m, 2.40m), new PosReceiptLine("Sugar 2kg", 1m, 3.10m, 3.10m)],
            TotalNet = 7.48m, TotalTax = 1.02m, TotalGross = 8.50m, Tenders = [new PosReceiptTender("Cash", 10.00m)], Change = 1.50m, FiscalCode = "SAMPLE-FISCAL-0001",
        };
        string text = PosReceiptFormatter.ToPlainText(sample, options);
        return $"{(options.Width >= 48 ? "80mm" : "58mm")} paper, {options.Width} characters, {options.Copies} cop{(options.Copies == 1 ? "y" : "ies")}" + Environment.NewLine
            + PosReceiptFormatter.Framed(text, options.Width);
    }

    internal static ReceiptOptions Options(ReceiptLayout l)
        => new(ReceiptOptions.WidthFor(l.PaperWidthMm), l.Title, Split(l.HeaderText), Split(l.FooterText), l.ShowCashier, l.ShowTerminal, l.ShowUnitPrice, l.ShowTaxSummary, l.ShowTaxNumber,
            l.ShowFiscal, l.Copies);

    // Lines typed with "|" or new lines between them; blank lines dropped.
    private static string? Lines(string? text)
    {
        List<string> lines = [.. (text ?? string.Empty).Split(['|', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static IReadOnlyList<string>? Split(string? text) => string.IsNullOrEmpty(text) ? null : text.Split('\n');

    private async Task<Result<long?>> TerminalAsync(long companyId, string? terminalCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(terminalCode))
        {
            return Result<long?>.Success(null);
        }

        string code = terminalCode.Trim().ToUpperInvariant();
        long? id = await _db.Set<PosTerminal>().Where(t => t.CompanyId == companyId && t.Code.ToUpper() == code).Select(t => (long?)t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is null)
        {
            return Error.NotFound("Pos.TerminalNotFound", $"No till {code}.");
        }

        return Result<long?>.Success(id);
    }
}
