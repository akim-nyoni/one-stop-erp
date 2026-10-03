using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>
/// The built-in token generator: a deterministic 20-digit number from the sale, for testing and demonstration only.
/// It is not an STS token; a live installation registers its vendor's generator in its place.
/// </summary>
public sealed class PlaceholderPrepaidTokenGenerator : IPrepaidTokenGenerator
{
    /// <inheritdoc />
    public string Generate(string meterSerial, decimal units, string saleNumber)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{meterSerial}|{units:0.00}|{saleNumber}")));
        StringBuilder digits = new();
        foreach (byte b in hash)
        {
            digits.Append((b % 10).ToString(CultureInfo.InvariantCulture));
            if (digits.Length == 20)
            {
                break;
            }
        }

        string t = digits.ToString();
        return string.Join('-', Enumerable.Range(0, 5).Select(i => t.Substring(i * 4, 4)));
    }
}

/// <summary>Prepaid vending with the credit-off-arrears rule, posted through AR and the posting engine.</summary>
public sealed class PrepaidVendingService : IPrepaidVendingService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;
    private readonly IPostingEngine _engine;
    private readonly IPrepaidTokenGenerator _tokens;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    /// <param name="engine">The posting engine.</param>
    /// <param name="tokens">The token generator hook.</param>
    public PrepaidVendingService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar, IPostingEngine engine, IPrepaidTokenGenerator tokens)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
        _engine = engine;
        _tokens = tokens;
    }

    /// <inheritdoc />
    public Task<Result<VendResult>> VendAsync(PrepaidVendRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => VendCoreAsync(request, ct), ct);
    }

    private async Task<Result<VendResult>> VendCoreAsync(PrepaidVendRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0m || request.ArrearsPercent < 0m || request.ArrearsPercent > 100m || string.IsNullOrWhiteSpace(request.BankAccountCode))
        {
            return Error.Validation("Mun.VendRequest", "A sale needs a positive amount, an arrears percentage from 0 to 100 and a bank account.");
        }

        Property? property = await _db.Properties.Include(p => p.Services).AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PropertyId && p.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {request.PropertyId} not found.");
        }

        PropertyServiceLink? link = property.Services.FirstOrDefault(s => s.ServiceType == request.ServiceType && s.IsActive);
        if (link is null)
        {
            return Error.Rule("Mun.ServiceNotLinked", $"Property {property.StandNumber} has no active {request.ServiceType} service.");
        }

        Tariff? tariff = await _db.Tariffs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == link.TariffId, ct).ConfigureAwait(false);
        if (tariff is null || tariff.PerUnitRate <= 0m)
        {
            return Error.Rule("Mun.NotPrepaid", $"The {request.ServiceType} tariff on stand {property.StandNumber} has no unit price to vend at.");
        }

        string meterSerial = link.MeterId is long meterId
            ? await _db.Set<Meter>().Where(m => m.Id == meterId).Select(m => m.Serial).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty
            : string.Empty;

        decimal arrears = await BalanceAsync(request.CompanyId, property.RatepayerCustomerId, ct).ConfigureAwait(false);
        VendSplit split = PrepaidVending.Split(request.Amount, arrears, request.ArrearsPercent, tariff.PerUnitRate);
        if (split.ToUnits > 0m && split.Units <= 0m)
        {
            return Error.Rule("Mun.TooLittle", $"After arrears, {split.ToUnits:0.00} does not buy a hundredth of a unit.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "PP", request.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == request.CompanyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        string bank = request.BankAccountCode.Trim();
        if (split.ToArrears > 0m)
        {
            Result<TradingPostingResult> receipted = await _ar.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = request.CompanyId, CustomerId = property.RatepayerCustomerId, DocumentNumber = $"{number.Value}-A", ReceiptDate = request.Date,
                Currency = currency, Amount = split.ToArrears, BankAccountCode = bank, Narrative = $"Prepaid sale {number.Value}: credit off arrears, stand {property.StandNumber}",
            }, ct).ConfigureAwait(false);
            if (receipted.IsFailure)
            {
                return receipted.Error;
            }
        }

        if (split.ToUnits > 0m)
        {
            Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = request.CompanyId, SourceModule = "CB", PostingDate = request.Date, TransactionCurrency = currency, DocumentType = "PrepaidSale",
                DocumentNumber = number.Value, Narrative = $"Prepaid {request.ServiceType} {split.Units:0.00} units, stand {property.StandNumber}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = bank, Side = JournalSide.Debit, TransactionAmount = split.ToUnits },
                    new JournalLineRequest { AccountCode = tariff.RevenueAccountCode, Side = JournalSide.Credit, TransactionAmount = split.ToUnits },
                ],
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        string token = _tokens.Generate(meterSerial, split.Units, number.Value);
        _db.Set<PrepaidSale>().Add(new PrepaidSale(request.CompanyId, number.Value, property.Id, request.ServiceType, meterSerial, request.Date,
            request.Amount, split.ToArrears, split.Units, token, string.IsNullOrWhiteSpace(request.Channel) ? "Counter" : request.Channel.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new VendResult(number.Value, token, split.Units, split.ToArrears, split.ToUnits, Math.Max(0m, arrears - split.ToArrears));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrepaidSaleLine>> SalesAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default)
        => await (
            from s in _db.Set<PrepaidSale>().AsNoTracking()
            join p in _db.Properties.AsNoTracking() on s.PropertyId equals p.Id
            where s.CompanyId == companyId && s.SoldOn >= start && s.SoldOn <= end
            orderby s.SoldOn, s.SaleNumber
            select new PrepaidSaleLine(s.SaleNumber, p.StandNumber, s.SoldOn, s.Amount, s.ToArrears, s.Units, s.Token, s.Channel))
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<decimal> BalanceAsync(long companyId, long customerId, CancellationToken ct)
    {
        var items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.OutstandingBase != 0m)
            .Select(i => new { i.Sign, i.OutstandingBase }).ToListAsync(ct).ConfigureAwait(false);
        return items.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase);
    }
}
