using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Configurable municipal services and their connections to properties.</summary>
public sealed class MunicipalServiceTypeService : IMunicipalServiceTypeService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The receivables ledger the deposits are invoiced through.</param>
    public MunicipalServiceTypeService(One4AllDbContext db, ISalesLedgerService ar)
    {
        _db = db;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result> SaveAsync(long companyId, string code, string name, ServiceType category, bool metered, decimal defaultDeposit, string? depositAccountCode, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        string? account = string.IsNullOrWhiteSpace(depositAccountCode) ? null : depositAccountCode.Trim().ToUpperInvariant();
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name) || !Enum.IsDefined(category) || defaultDeposit < 0m || (defaultDeposit > 0m && account is null))
        {
            return Result.Failure(Error.Validation("Mun.ServiceType", "A service needs a code, a name, a category, and a deposit account when it takes a deposit."));
        }

        if (account is not null && !await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == account, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Mun.AccountNotFound", $"Account {account} does not exist."));
        }

        MunicipalServiceType? type = await _db.MunicipalServiceTypes.FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == key, ct).ConfigureAwait(false);
        if (type is null)
        {
            type = new MunicipalServiceType(companyId, key, name, category);
            _db.MunicipalServiceTypes.Add(type);
        }

        type.Name = name.Trim();
        type.Category = category;
        type.IsMetered = metered;
        type.DefaultDeposit = defaultDeposit;
        type.DepositAccountCode = account;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<string?>> ConnectAsync(long companyId, string standNumber, string serviceCode, string tariffCode, DateOnly startDate, decimal? quantity, decimal? deposit, CancellationToken ct = default)
        => _db.AtomicAsync(() => ConnectCoreAsync(companyId, standNumber, serviceCode, tariffCode, startDate, quantity, deposit, ct), ct);

    private async Task<Result<string?>> ConnectCoreAsync(long companyId, string standNumber, string serviceCode, string tariffCode, DateOnly startDate, decimal? quantity, decimal? deposit,
        CancellationToken ct)
    {
        string code = (serviceCode ?? string.Empty).Trim().ToUpperInvariant(), tariffKey = (tariffCode ?? string.Empty).Trim().ToUpperInvariant();
        if (quantity is <= 0m || deposit is < 0m)
        {
            return Error.Validation("Mun.Connection", "A connection's quantity must be above zero and its deposit not negative.");
        }

        if (await _db.MunicipalServiceTypes.AsNoTracking().FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == code, ct).ConfigureAwait(false) is not { IsActive: true } type)
        {
            return Error.NotFound("Mun.ServiceTypeNotFound", $"No active service {code}.");
        }

        if (await PropertyAsync(companyId, standNumber, ct).ConfigureAwait(false) is not Property property)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Stand {standNumber} not found.");
        }

        Tariff? tariff = await _db.Tariffs.AsNoTracking().Where(t => t.CompanyId == companyId && t.Code == tariffKey && t.EffectiveFrom <= startDate).OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (tariff is null || tariff.ServiceType != type.Category)
        {
            return Error.Validation("Mun.TariffNotFound", $"No {type.Category} tariff {tariffKey} in force on {startDate:yyyy-MM-dd}.");
        }

        if (property.Services.Any(s => s.ServiceCode == code && s.Status != ServiceLinkStatus.Closed))
        {
            return Error.Conflict("Mun.AlreadyConnected", $"Stand {property.StandNumber} already has {code}.");
        }

        decimal held = deposit ?? type.DefaultDeposit;
        PropertyServiceLink link = property.AddService(type.Category, tariff.Id, startDate);
        link.ServiceCode = code;
        link.Quantity = quantity ?? 1m;
        link.DepositAmount = held;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (held <= 0m)
        {
            return Result<string?>.Success(null);
        }

        if (type.DepositAccountCode is null)
        {
            return Error.Validation("Mun.DepositAccount", $"{code} has no deposit account to hold the deposit in.");
        }

        // The deposit is billed to the ratepayer and held in the liability account until the service is closed.
        string number = $"DEP-{property.StandNumber}-{code}-{startDate:yyyyMMdd}";
        Result<TradingPostingResult> billed = await MunicipalCharges.PostAsync(_db, _ar, companyId, property.RatepayerCustomerId, number, startDate, held, 0m, type.DepositAccountCode, null,
            $"{type.Name} deposit, stand {property.StandNumber}", ct).ConfigureAwait(false);
        return billed.IsFailure ? billed.Error : number;
    }

    /// <inheritdoc />
    public async Task<Result> SetStatusAsync(long companyId, string standNumber, string serviceCode, ServiceLinkStatus status, DateOnly date, CancellationToken ct = default)
    {
        if (await LinkAsync(companyId, standNumber, serviceCode, ct).ConfigureAwait(false) is not PropertyServiceLink link)
        {
            return Result.Failure(Error.NotFound("Mun.NotConnected", $"Stand {standNumber} has no open {serviceCode} connection."));
        }

        link.SetStatus(status, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetQuantityAsync(long companyId, string standNumber, string serviceCode, decimal quantity, CancellationToken ct = default)
    {
        if (quantity <= 0m)
        {
            return Result.Failure(Error.Validation("Mun.Connection", "A connection's quantity must be above zero."));
        }

        if (await LinkAsync(companyId, standNumber, serviceCode, ct).ConfigureAwait(false) is not PropertyServiceLink link)
        {
            return Result.Failure(Error.NotFound("Mun.NotConnected", $"Stand {standNumber} has no open {serviceCode} connection."));
        }

        link.Quantity = quantity;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PropertyServiceRow>> ConnectionsAsync(long companyId, string? standNumber, CancellationToken ct = default)
    {
        string? stand = string.IsNullOrWhiteSpace(standNumber) ? null : standNumber.Trim().ToUpperInvariant();
        var rows = await (from p in _db.Properties.AsNoTracking()
                          from l in p.Services
                          join t in _db.Tariffs.AsNoTracking() on l.TariffId equals t.Id
                          where p.CompanyId == companyId && (stand == null || p.StandNumber == stand)
                          orderby p.StandNumber, l.StartDate
                          select new { p.StandNumber, l.ServiceCode, l.ServiceType, t.Code, l.Quantity, l.DepositAmount, l.Status, l.StartDate }).Take(2000).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(r => new PropertyServiceRow(r.StandNumber, r.ServiceCode ?? r.ServiceType.ToString(), r.Code, r.Quantity, r.DepositAmount, r.Status, r.StartDate))];
    }

    private Task<Property?> PropertyAsync(long companyId, string standNumber, CancellationToken ct)
    {
        string stand = (standNumber ?? string.Empty).Trim().ToUpperInvariant();
        return _db.Properties.Include(p => p.Services).FirstOrDefaultAsync(p => p.CompanyId == companyId && p.StandNumber == stand, ct);
    }

    private async Task<PropertyServiceLink?> LinkAsync(long companyId, string standNumber, string serviceCode, CancellationToken ct)
    {
        string code = (serviceCode ?? string.Empty).Trim().ToUpperInvariant();
        return (await PropertyAsync(companyId, standNumber, ct).ConfigureAwait(false))?.Services.Where(s => s.ServiceCode == code && s.Status != ServiceLinkStatus.Closed)
            .OrderByDescending(s => s.StartDate).FirstOrDefault();
    }
}
