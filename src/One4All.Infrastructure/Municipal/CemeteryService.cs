using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>The cemetery register: plot sales and interments, invoiced through AR.</summary>
public sealed class CemeteryService : ICemeteryService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    public CemeteryService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result<int>> AddPlotsAsync(long companyId, string cemetery, string section, IReadOnlyList<string> plotNumbers, decimal price, int capacity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plotNumbers);
        if (string.IsNullOrWhiteSpace(cemetery) || string.IsNullOrWhiteSpace(section) || plotNumbers.Count == 0 || plotNumbers.Any(string.IsNullOrWhiteSpace) || price < 0m || capacity < 1)
        {
            return Error.Validation("Mun.PlotDetails", "Plots need a cemetery, a section, numbers, a price of zero or more and room for at least one interment.");
        }

        List<string> references = [.. plotNumbers.Select(n => CemeteryPlot.ReferenceFor(cemetery, section, n))];
        if (references.Distinct().Count() != references.Count)
        {
            return Error.Validation("Mun.PlotDuplicate", "A plot number is listed more than once.");
        }

        List<string> existing = await _db.Set<CemeteryPlot>().Where(p => p.CompanyId == companyId && references.Contains(p.Reference))
            .Select(p => p.Reference).ToListAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return Error.Conflict("Mun.PlotExists", $"Plots already on the register: {string.Join(", ", existing.Order())}.");
        }

        foreach (string number in plotNumbers)
        {
            _db.Set<CemeteryPlot>().Add(new CemeteryPlot(companyId, cemetery.Trim(), section.Trim(), number.Trim(), price, capacity));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return plotNumbers.Count;
    }

    /// <inheritdoc />
    public Task<Result<string>> SellAsync(long companyId, string plotReference, long holderCustomerId, DateOnly date, string revenueAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => SellCoreAsync(companyId, plotReference, holderCustomerId, date, revenueAccountCode, ct), ct);

    private async Task<Result<string>> SellCoreAsync(long companyId, string plotReference, long holderCustomerId, DateOnly date, string revenueAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(revenueAccountCode))
        {
            return Error.Validation("Mun.PlotRevenueAccount", "A plot sale needs a revenue account.");
        }

        Result<CemeteryPlot> found = await FindPlotAsync(companyId, plotReference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        CemeteryPlot plot = found.Value;
        if (plot.Status != CemeteryPlotStatus.Available)
        {
            return Error.Rule("Mun.PlotSold", $"Plot {plot.Reference} has already been sold.");
        }

        Customer? holder = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == holderCustomerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (holder is null)
        {
            return Error.NotFound("Mun.HolderNotFound", $"Customer {holderCustomerId} not found.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "CP", date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        if (plot.Price > 0m)
        {
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = holder.Id, DocumentNumber = number.Value, DocumentDate = date, Currency = holder.Currency,
                NetAmount = plot.Price, SalesAccountCode = revenueAccountCode.Trim(), Narrative = $"Sale of cemetery plot {plot.Reference}",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }
        }

        plot.Sell(holder.Id, number.Value, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result<string>> RecordIntermentAsync(IntermentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => IntermentCoreAsync(request, ct), ct);
    }

    private async Task<Result<string>> IntermentCoreAsync(IntermentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeceasedName) || request.IntermentDate < request.DateOfDeath || request.IntermentFee < 0m)
        {
            return Error.Validation("Mun.IntermentDetails", "An interment needs the deceased's name, a burial date on or after the date of death and a fee of zero or more.");
        }

        if (request.IntermentFee > 0m && string.IsNullOrWhiteSpace(request.FeeAccountCode))
        {
            return Error.Validation("Mun.IntermentFeeAccount", "A burial fee needs a revenue account.");
        }

        Result<CemeteryPlot> found = await FindPlotAsync(request.CompanyId, request.PlotReference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        CemeteryPlot plot = found.Value;
        if (plot.Status == CemeteryPlotStatus.Available)
        {
            return Error.Rule("Mun.PlotNotSold", $"Plot {plot.Reference} must be sold before an interment.");
        }

        if (plot.Status == CemeteryPlotStatus.Full)
        {
            return Error.Rule("Mun.PlotFull", $"Plot {plot.Reference} has no room for another interment.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "BUR", request.IntermentDate, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        if (request.IntermentFee > 0m)
        {
            Customer holder = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == plot.HolderCustomerId, ct).ConfigureAwait(false);
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = request.CompanyId, CustomerId = holder.Id, DocumentNumber = number.Value, DocumentDate = request.IntermentDate, Currency = holder.Currency,
                NetAmount = request.IntermentFee, SalesAccountCode = request.FeeAccountCode!.Trim(), Narrative = $"Burial of {request.DeceasedName.Trim()} in plot {plot.Reference}",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }
        }

        plot.Inter(new CemeteryInterment(number.Value, request.DeceasedName.Trim(), request.DateOfDeath, request.IntermentDate));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlotEntry>> PlotsAsync(long companyId, string cemetery, CemeteryPlotStatus? status, CancellationToken ct = default)
    {
        string name = (cemetery ?? string.Empty).Trim();
        return await (
            from p in _db.Set<CemeteryPlot>().AsNoTracking()
            join c in _db.Customers.AsNoTracking() on p.HolderCustomerId equals (long?)c.Id into holders
            from h in holders.DefaultIfEmpty()
            where p.CompanyId == companyId && p.Cemetery == name && (status == null || p.Status == status)
            orderby p.Reference
            select new PlotEntry(p.Reference, p.Status, p.Price, p.Capacity, p.Interments.Count, h == null ? null : h.Code))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BurialEntry>> BurialRegisterAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default)
        => await (
            from i in _db.Set<CemeteryInterment>().AsNoTracking()
            join p in _db.Set<CemeteryPlot>().AsNoTracking() on i.CemeteryPlotId equals p.Id
            where p.CompanyId == companyId && i.IntermentDate >= start && i.IntermentDate <= end
            orderby i.IntermentDate, i.IntermentNumber
            select new BurialEntry(i.IntermentNumber, p.Reference, i.DeceasedName, i.DateOfDeath, i.IntermentDate))
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<Result<CemeteryPlot>> FindPlotAsync(long companyId, string plotReference, CancellationToken ct)
    {
        string reference = (plotReference ?? string.Empty).Trim().ToUpperInvariant();
        CemeteryPlot? plot = await _db.Set<CemeteryPlot>().Include(p => p.Interments)
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Reference == reference, ct).ConfigureAwait(false);
        if (plot is null)
        {
            return Error.NotFound("Mun.PlotNotFound", $"Plot {reference} not found.");
        }

        return plot;
    }
}
