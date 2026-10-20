using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.Common;
using One4All.Core.FixedAssets;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.FixedAssets;

/// <summary>
/// Fixed Assets (brief §4.14). Registers and capitalises assets, runs monthly depreciation posting to the
/// GL, and disposes assets recognising the profit or loss - all through the central posting engine.
/// </summary>
public sealed class FixedAssetService : IFixedAssetService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    private readonly Application.Abstractions.Trading.ISalesLedgerService? _sales;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="sales">The sales ledger, for an asset sold to a customer on an invoice.</param>
    public FixedAssetService(One4AllDbContext db, IPostingEngine posting, Application.Abstractions.Trading.ISalesLedgerService? sales = null)
    {
        _sales = sales;
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<long>> RegisterAsync(RegisterAssetRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RegisterCoreAsync(request, ct), ct);

    /// <inheritdoc />
    public Task<Result<long>> CapitaliseFromInvoiceAsync(CapitaliseInvoiceRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => CapitaliseCoreAsync(request, ct), ct);

    private async Task<Result<long>> CapitaliseCoreAsync(CapitaliseInvoiceRequest request, CancellationToken ct)
    {
        string number = (request.InvoiceNumber ?? string.Empty).Trim();
        IQueryable<Core.Trading.OpenItem> invoices = _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == request.CompanyId && i.Ledger == Core.Trading.TradingLedger.AccountsPayable
            && i.ItemType == Core.Trading.OpenItemType.Invoice && i.DocumentNumber == number && i.JournalEntryId != null);
        if (!string.IsNullOrWhiteSpace(request.SupplierCode))
        {
            string code = request.SupplierCode.Trim().ToUpperInvariant();
            invoices = invoices.Where(i => _db.Suppliers.Any(s => s.Id == i.TraderId && s.Code == code));
        }

        List<Core.Trading.OpenItem> found = await invoices.Take(2).ToListAsync(ct).ConfigureAwait(false);
        if (found.Count == 0)
        {
            return Error.NotFound("Fa.InvoiceNotFound", $"No posted supplier invoice {number}.");
        }

        if (found.Count > 1)
        {
            return Error.Validation("Fa.InvoiceAmbiguous", $"More than one supplier has an invoice {number}; name the supplier.");
        }

        Core.Trading.OpenItem invoice = found[0];
        string classCode = (request.AssetClass ?? string.Empty).Trim().ToUpperInvariant();
        if (await _db.Set<FixedAssetClass>().AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == request.CompanyId && c.Code == classCode, ct).ConfigureAwait(false) is not FixedAssetClass assetClass)
        {
            return Error.NotFound("Fa.ClassNotFound", $"Asset class {classCode} not found; capitalising from an invoice takes the class's accounts.");
        }

        string account = string.IsNullOrWhiteSpace(request.InvoiceAccountCode) ? assetClass.CostAccountCode : request.InvoiceAccountCode.Trim().ToUpperInvariant();
        decimal invoiced = await _db.Journals.AsNoTracking().Where(j => j.Id == invoice.JournalEntryId).SelectMany(j => j.Lines).Where(l => l.AccountCode == account)
            .SumAsync(l => l.BaseDebit - l.BaseCredit, ct).ConfigureAwait(false);
        decimal capitalised = await _db.FixedAssets.AsNoTracking().Where(a => a.CompanyId == request.CompanyId && a.SourceOpenItemId == invoice.Id && a.SourceAccountCode == account)
            .SumAsync(a => a.Cost, ct).ConfigureAwait(false);
        decimal left = invoiced - capitalised;
        decimal cost = request.Cost ?? left;
        if (cost <= 0m || cost > left + 0.005m)
        {
            return Error.Rule("Fa.InvoiceCapitalised", $"Only {left:#,0.00} of invoice {number} on account {account} is left to capitalise.");
        }

        return await RegisterCoreAsync(new RegisterAssetRequest
        {
            CompanyId = request.CompanyId, AssetNumber = request.AssetNumber, Description = request.Description, AssetClass = assetClass.Code, Cost = cost,
            AcquisitionDate = request.AcquisitionDate ?? invoice.DocumentDate, FundingAccountCode = account,
            DocumentNumber = string.IsNullOrWhiteSpace(request.DocumentNumber) ? $"CAP-{number}" : request.DocumentNumber.Trim(), CostCentre = request.CostCentre, Department = request.Department,
        }, ct, (invoice.Id, account)).ConfigureAwait(false);
    }

    // One unit of work: its postings and record changes commit together or not at all. An asset capitalised from a supplier invoice already coded
    // to its cost account needs no posting.
    private async Task<Result<long>> RegisterCoreAsync(RegisterAssetRequest request, CancellationToken ct, (long OpenItemId, string Account)? source = null)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Fa.CompanyNotFound", $"Company {request.CompanyId} not found.");
        }

        // The asset class fills in whatever the request leaves blank: method, life, residual, rate and accounts.
        string classCode = (request.AssetClass ?? string.Empty).Trim().ToUpperInvariant();
        if (await _db.Set<FixedAssetClass>().AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == request.CompanyId && c.Code == classCode, ct).ConfigureAwait(false) is FixedAssetClass assetClass)
        {
            static string Pick(string given, string fallback) => string.IsNullOrWhiteSpace(given) ? fallback : given;
            request = new RegisterAssetRequest
            {
                CompanyId = request.CompanyId, AssetNumber = request.AssetNumber, Description = request.Description, AssetClass = assetClass.Code, Cost = request.Cost,
                ResidualValue = request.ResidualValue != 0m ? request.ResidualValue : decimal.Round(request.Cost * assetClass.ResidualPercent / 100m, 2, MidpointRounding.AwayFromZero),
                AcquisitionDate = request.AcquisitionDate, Method = request.Method == default ? assetClass.Method : request.Method,
                UsefulLifeMonths = request.UsefulLifeMonths > 0 ? request.UsefulLifeMonths : assetClass.UsefulLifeMonths,
                ReducingBalanceRatePercent = request.ReducingBalanceRatePercent > 0m ? request.ReducingBalanceRatePercent : assetClass.ReducingBalanceRatePercent,
                CostAccountCode = Pick(request.CostAccountCode, assetClass.CostAccountCode),
                AccumulatedDepreciationAccountCode = Pick(request.AccumulatedDepreciationAccountCode, assetClass.AccumulatedDepreciationAccountCode),
                DepreciationExpenseAccountCode = Pick(request.DepreciationExpenseAccountCode, assetClass.DepreciationExpenseAccountCode),
                FundingAccountCode = request.FundingAccountCode, DocumentNumber = request.DocumentNumber, TotalEstimatedUnits = request.TotalEstimatedUnits,
                ParentAssetNumber = request.ParentAssetNumber, CostCentre = request.CostCentre, Department = request.Department, Convention = request.Convention ?? assetClass.Convention,
            };
        }

        if (string.IsNullOrWhiteSpace(request.CostAccountCode) || string.IsNullOrWhiteSpace(request.AccumulatedDepreciationAccountCode) || string.IsNullOrWhiteSpace(request.DepreciationExpenseAccountCode)
            || request.Method == default)
        {
            return Error.Validation("Fa.ClassDefaults", "An asset needs its method and cost, depreciation and expense accounts, given or from a known asset class.");
        }

        Result<(string? CostCentre, string? Department)> analysis = await DimensionsAsync(request.CompanyId, request.CostCentre, request.Department, ct).ConfigureAwait(false);
        if (analysis.IsFailure)
        {
            return analysis.Error;
        }

        long? parentId = null;
        if (!string.IsNullOrWhiteSpace(request.ParentAssetNumber))
        {
            parentId = await _db.Set<FixedAsset>().Where(a => a.CompanyId == request.CompanyId && a.AssetNumber == request.ParentAssetNumber.Trim().ToUpper() && a.Status != FixedAssetStatus.Disposed)
                .Select(a => (long?)a.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (parentId is null)
            {
                return Error.NotFound("Fa.ParentNotFound", $"No asset '{request.ParentAssetNumber}' to make this a component of.");
            }
        }

        if (request.Method == DepreciationMethod.UnitsOfProduction && request.TotalEstimatedUnits is not > 0m)
        {
            return Error.Validation("Fa.TotalUnits", "A units-of-production asset needs the total units it is expected to produce.");
        }

        FixedAsset asset = new(request.CompanyId, request.AssetNumber, request.Description, request.AssetClass, request.Cost, request.ResidualValue, request.AcquisitionDate, request.Method, request.UsefulLifeMonths, request.ReducingBalanceRatePercent, request.CostAccountCode, request.AccumulatedDepreciationAccountCode, request.DepreciationExpenseAccountCode)
        {
            TotalEstimatedUnits = request.TotalEstimatedUnits,
            ParentAssetId = parentId,
            CostCentre = analysis.Value.CostCentre,
            Department = analysis.Value.Department,
            Convention = request.Convention ?? DepreciationConvention.FullMonth,
            SourceOpenItemId = source?.OpenItemId,
            SourceAccountCode = source?.Account,
        };
        _db.Set<FixedAsset>().Add(asset);
        if (source is not null && string.Equals(request.FundingAccountCode, request.CostAccountCode, StringComparison.OrdinalIgnoreCase))
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return asset.Id;
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "GL",
            PostingDate = request.AcquisitionDate,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "FA-ACQ",
            DocumentNumber = request.DocumentNumber,
            Narrative = $"Capitalise asset {asset.AssetNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = request.CostAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Debit, TransactionAmount = request.Cost },
                new JournalLineRequest { AccountCode = request.FundingAccountCode, Side = JournalSide.Credit, TransactionAmount = request.Cost },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return asset.Id;
    }

    /// <inheritdoc />
    public Task<Result<string>> ReassignAsync(long companyId, long assetId, string? costCentre, string? department, DateOnly date, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReassignCoreAsync(companyId, assetId, costCentre, department, date, documentNumber, ct), ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string AssetNumber, string Description, decimal Cost, decimal NetBookValue)>> ComponentsAsync(long companyId, long assetId, CancellationToken ct = default)
        => [.. (await _db.Set<FixedAsset>().AsNoTracking().Where(a => a.CompanyId == companyId && a.ParentAssetId == assetId).OrderBy(a => a.AssetNumber).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => (a.AssetNumber, a.Description, a.Cost, a.NetBookValue))];

    private async Task<Result<string>> ReassignCoreAsync(long companyId, long assetId, string? costCentre, string? department, DateOnly date, string documentNumber, CancellationToken ct)
    {
        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found.");
        }

        if (asset.Status == FixedAssetStatus.Disposed)
        {
            return Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is disposed and cannot be transferred.");
        }

        Result<(string? CostCentre, string? Department)> to = await DimensionsAsync(companyId, costCentre, department, ct).ConfigureAwait(false);
        if (to.IsFailure)
        {
            return to.Error;
        }

        if (to.Value.CostCentre == asset.CostCentre && to.Value.Department == asset.Department)
        {
            return Error.Validation("Fa.NothingToTransfer", $"Asset {asset.AssetNumber} is already analysed there.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = asset.CostAccountCode, CostCentre = to.Value.CostCentre, Department = to.Value.Department, Side = JournalSide.Debit, TransactionAmount = asset.Cost },
            new JournalLineRequest { AccountCode = asset.CostAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Credit, TransactionAmount = asset.Cost },
        ];
        if (asset.AccumulatedDepreciation > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Debit, TransactionAmount = asset.AccumulatedDepreciation });
            lines.Add(new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, CostCentre = to.Value.CostCentre, Department = to.Value.Department, Side = JournalSide.Credit, TransactionAmount = asset.AccumulatedDepreciation });
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = company.BaseCurrency, DocumentType = "FA-TRANSFER", DocumentNumber = documentNumber,
            Narrative = $"Transfer asset {asset.AssetNumber} to {to.Value.CostCentre ?? "-"}/{to.Value.Department ?? "-"}", Lines = lines,
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        asset.CostCentre = to.Value.CostCentre;
        asset.Department = to.Value.Department;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    // Cost centre and department codes as the GL knows them (blank for none).
    private async Task<Result<(string? CostCentre, string? Department)>> DimensionsAsync(long companyId, string? costCentre, string? department, CancellationToken ct)
    {
        string? cc = string.IsNullOrWhiteSpace(costCentre) ? null : costCentre.Trim().ToUpperInvariant();
        string? dept = string.IsNullOrWhiteSpace(department) ? null : department.Trim().ToUpperInvariant();
        if (cc is not null && !await _db.CostCentres.AnyAsync(c => c.CompanyId == companyId && c.Code == cc, ct).ConfigureAwait(false))
        {
            return Error.Validation("Fa.UnknownDimension", $"No cost centre '{cc}'.");
        }

        if (dept is not null && !await _db.Departments.AnyAsync(d => d.CompanyId == companyId && d.Code == dept, ct).ConfigureAwait(false))
        {
            return Error.Validation("Fa.UnknownDimension", $"No department '{dept}'.");
        }

        return (cc, dept);
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordUsageAsync(long companyId, long assetId, DateOnly date, decimal units, string reference, CancellationToken ct = default)
    {
        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found.");
        }

        if (asset.Method != DepreciationMethod.UnitsOfProduction)
        {
            return Error.Rule("Fa.NotUnitsOfProduction", $"{asset.AssetNumber} is not depreciated on units of production.");
        }

        if (units <= 0m)
        {
            return Error.Validation("Fa.UsageUnits", "Usage must be a positive number of units.");
        }

        if (asset.LastDepreciatedPeriod is int last && last >= FixedAsset.PeriodOf(date))
        {
            return Error.Rule("Fa.PeriodDepreciated", $"{asset.AssetNumber} has already been depreciated for {date:yyyy-MM}.");
        }

        AssetUsageReading reading = new(companyId, assetId, date, units, (reference ?? string.Empty).Trim());
        _db.AssetUsageReadings.Add(reading);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return reading.Id;
    }

    /// <inheritdoc />
    public async Task<Result<DepreciationRunResult>> RunDepreciationAsync(long companyId, DateOnly date, string documentNumber, CancellationToken ct = default)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Fa.CompanyNotFound", $"Company {companyId} not found.");
        }

        List<FixedAsset> assets = await _db.Set<FixedAsset>()
            .Where(a => a.CompanyId == companyId && a.Status == FixedAssetStatus.Active)
            .ToListAsync(ct).ConfigureAwait(false);

        // Units-of-production assets are charged on the month's usage readings.
        DateOnly monthStart = new(date.Year, date.Month, 1), monthEnd = monthStart.AddMonths(1).AddDays(-1);
        List<long> unitAssets = [.. assets.Where(a => a.Method == DepreciationMethod.UnitsOfProduction).Select(a => a.Id)];
        Dictionary<long, decimal> usage = await _db.AssetUsageReadings.Where(r => unitAssets.Contains(r.AssetId) && r.Date >= monthStart && r.Date <= monthEnd)
            .GroupBy(r => r.AssetId).Select(g => new { g.Key, Units = g.Sum(r => r.Units) }).ToDictionaryAsync(x => x.Key, x => x.Units, ct).ConfigureAwait(false);

        List<JournalLineRequest> lines = [];
        List<(FixedAsset Asset, decimal Charge)> charges = [];
        decimal total = 0m;
        int count = 0;
        int notYetAcquired = 0;
        int alreadyDepreciated = 0;
        foreach (FixedAsset asset in assets)
        {
            // Never charge before acquisition, and never charge the same month twice.
            switch (asset.SkipReasonFor(date))
            {
                case DepreciationSkip.NotYetAcquired:
                    notYetAcquired++;
                    continue;
                case DepreciationSkip.AlreadyDepreciated:
                    alreadyDepreciated++;
                    continue;
                case DepreciationSkip.NotActive:
                    continue;
            }

            decimal charge = asset.MonthlyDepreciation(usage.GetValueOrDefault(asset.Id), date);
            if (charge <= 0m)
            {
                continue;
            }

            lines.Add(new JournalLineRequest { AccountCode = asset.DepreciationExpenseAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Debit, TransactionAmount = charge, Narrative = asset.AssetNumber });
            lines.Add(new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Credit, TransactionAmount = charge, Narrative = asset.AssetNumber });
            charges.Add((asset, charge));
            total += charge;
            count++;
        }

        if (count == 0)
        {
            return new DepreciationRunResult(0, 0m, null, notYetAcquired, alreadyDepreciated);
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "FA-DEPR",
            DocumentNumber = documentNumber,
            Narrative = $"Depreciation run {date:yyyy-MM}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            // Nothing applied yet: a refused posting (e.g. a closed period) must not leave the assets marked as
            // depreciated for the month, or the next run would skip a month that was never charged.
            return posted.Error;
        }

        foreach ((FixedAsset asset, decimal charge) in charges)
        {
            asset.ApplyDepreciation(charge, date);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DepreciationRunResult(count, total, posted.Value.JournalNumber, notYetAcquired, alreadyDepreciated);
    }

    /// <inheritdoc />
    public Task<Result<DisposalResult>> DisposeAsync(long companyId, long assetId, decimal proceeds, DateOnly date, string fundingAccountCode, string gainAccountCode, string lossAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => DisposeCoreAsync(companyId, assetId, proceeds, date, fundingAccountCode, gainAccountCode, lossAccountCode, documentNumber, ct), ct);

    /// <inheritdoc />
    public Task<Result<DisposalResult>> SellToCustomerAsync(AssetSaleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(async () =>
        {
            if (_sales is null || request.NetProceeds < 0m || string.IsNullOrWhiteSpace(request.InvoiceNumber) || string.IsNullOrWhiteSpace(request.ProceedsAccountCode))
            {
                return Error.Validation("Fa.SaleDetails", "An asset sale needs the invoice number, the proceeds account and a price of zero or more.");
            }

            Core.Trading.Customer? customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
            if (customer is null)
            {
                return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
            }

            // Output VAT at the tax code's rate on the day (a disposal of a business asset is a taxable supply).
            decimal tax = 0m;
            string? taxCode = string.IsNullOrWhiteSpace(request.TaxCode) ? null : request.TaxCode.Trim().ToUpperInvariant();
            string? taxAccount = string.IsNullOrWhiteSpace(request.TaxAccountCode) ? null : request.TaxAccountCode.Trim();
            if (taxCode is not null)
            {
                Core.Accounting.TaxCode? code = await _db.TaxCodes.AsNoTracking().Include(t => t.Rates).FirstOrDefaultAsync(t => t.CompanyId == request.CompanyId && t.Code == taxCode, ct).ConfigureAwait(false);
                if (code is null)
                {
                    return Error.NotFound("Tax.CodeNotFound", $"No tax code {taxCode}.");
                }

                tax = decimal.Round(request.NetProceeds * code.RateOn(request.Date) / 100m, 2, MidpointRounding.AwayFromZero);
                taxAccount ??= code.OutputAccountCode;
            }

            Result<Application.Abstractions.Trading.TradingPostingResult> invoiced = await _sales.PostInvoiceAsync(new Application.Abstractions.Trading.SalesInvoiceRequest
            {
                CompanyId = request.CompanyId, CustomerId = customer.Id, DocumentNumber = request.InvoiceNumber.Trim(), DocumentDate = request.Date, Currency = customer.Currency,
                NetAmount = request.NetProceeds, TaxAmount = tax, TaxCode = taxCode, TaxAccountCode = taxAccount, SalesAccountCode = request.ProceedsAccountCode.Trim(),
                Narrative = $"Sale of asset {request.AssetId}",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }

            return await DisposeCoreAsync(request.CompanyId, request.AssetId, request.NetProceeds, request.Date, request.ProceedsAccountCode.Trim(), request.GainAccountCode, request.LossAccountCode,
                request.InvoiceNumber.Trim(), ct).ConfigureAwait(false);
        }, ct);
    }

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<DisposalResult>> DisposeCoreAsync(long companyId, long assetId, decimal proceeds, DateOnly date, string fundingAccountCode, string gainAccountCode, string lossAccountCode, string documentNumber, CancellationToken ct)
    {
        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found.");
        }

        if (asset.Status == FixedAssetStatus.Disposed)
        {
            return Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is already disposed.");
        }

        // An asset goes with its significant parts: dispose of the components first.
        if (await _db.Set<FixedAsset>().AnyAsync(a => a.ParentAssetId == asset.Id && a.Status != FixedAssetStatus.Disposed, ct).ConfigureAwait(false))
        {
            return Error.Rule("Fa.HasComponents", $"Asset {asset.AssetNumber} still has components in use; dispose of them first.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Fa.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Depreciation is charged up to the disposal (brief §4.14): every month from the last one charged to the disposal month - that month by
        // the asset's convention - goes into the disposal journal before the asset is cleared, so the gain or loss is on the carrying amount at the date.
        decimal caughtUp = 0m;
        if (asset.Status == FixedAssetStatus.Active && asset.Method != DepreciationMethod.UnitsOfProduction)
        {
            DateOnly month = asset.LastDepreciatedPeriod is int last ? new DateOnly(last / 100, last % 100, 1).AddMonths(1) : new DateOnly(asset.AcquisitionDate.Year, asset.AcquisitionDate.Month, 1);
            for (; FixedAsset.PeriodOf(month) <= FixedAsset.PeriodOf(date) && asset.Status == FixedAssetStatus.Active; month = month.AddMonths(1))
            {
                if (FixedAsset.PeriodOf(month) < asset.FirstChargePeriod())
                {
                    continue;
                }

                decimal share = FixedAsset.PeriodOf(month) == FixedAsset.PeriodOf(date) ? asset.DisposalMonthShare(date) : 1m;
                decimal charge = decimal.Round(asset.MonthlyDepreciation(0m, month) * share, 2, MidpointRounding.AwayFromZero);
                if (charge <= 0m)
                {
                    break;
                }

                asset.ApplyDepreciation(charge, month);
                caughtUp += charge;
            }
        }

        decimal nbv = asset.NetBookValue;
        decimal gainOrLoss = proceeds - nbv;

        // Clear cost and accumulated depreciation, take the proceeds, and book the gain/loss balancing figure.
        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Debit, TransactionAmount = proceeds },
            new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Debit, TransactionAmount = asset.AccumulatedDepreciation },
            new JournalLineRequest { AccountCode = asset.CostAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Credit, TransactionAmount = asset.Cost },
        ];

        if (caughtUp > 0m)
        {
            lines.Insert(0, new JournalLineRequest { AccountCode = asset.DepreciationExpenseAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Debit, TransactionAmount = caughtUp, Narrative = $"Depreciation to {date:yyyy-MM-dd}" });
            lines.Insert(1, new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Credit, TransactionAmount = caughtUp, Narrative = $"Depreciation to {date:yyyy-MM-dd}" });
        }

        if (gainOrLoss > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = gainAccountCode, Side = JournalSide.Credit, TransactionAmount = gainOrLoss });
        }
        else if (gainOrLoss < 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = lossAccountCode, Side = JournalSide.Debit, TransactionAmount = -gainOrLoss });
        }

        // The asset's revaluation surplus is realised on disposal: moved straight to retained earnings, never through profit (IAS 16.41).
        // Without a retained earnings account it stays in the reserve (the transfer is permitted, not required).
        if (asset.RevaluationSurplus > 0m && asset.RevaluationReserveAccountCode is string reserve
            && await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.ControlAccountType == Core.Accounting.ControlAccountType.RetainedEarnings).OrderBy(a => a.AccountCode)
                .Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false) is string retained)
        {
            decimal surplus = asset.RealiseSurplus();
            lines.Add(new JournalLineRequest { AccountCode = reserve, Side = JournalSide.Debit, TransactionAmount = surplus, Narrative = "Revaluation surplus realised" });
            lines.Add(new JournalLineRequest { AccountCode = retained, Side = JournalSide.Credit, TransactionAmount = surplus, Narrative = "Revaluation surplus realised" });
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "FA-DISP",
            DocumentNumber = documentNumber,
            Narrative = $"Disposal of asset {asset.AssetNumber}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        asset.Dispose();
        _db.Set<AssetDisposal>().Add(new AssetDisposal(companyId, asset.Id, asset.AssetNumber, date, proceeds, nbv, gainOrLoss, posted.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DisposalResult(proceeds, nbv, gainOrLoss, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public Task<Result<RevaluationResult>> RevalueAsync(long companyId, long assetId, decimal newCarryingAmount, DateOnly date, string revaluationReserveAccountCode, string impairmentExpenseAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => RevalueCoreAsync(companyId, assetId, newCarryingAmount, date, revaluationReserveAccountCode, impairmentExpenseAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<RevaluationResult>> RevalueCoreAsync(long companyId, long assetId, decimal newCarryingAmount, DateOnly date, string revaluationReserveAccountCode, string impairmentExpenseAccountCode, string documentNumber, CancellationToken ct)
    {
        if (newCarryingAmount < 0m)
        {
            return Error.Rule("Fa.NegativeCarryingAmount", "The revalued carrying amount cannot be negative.");
        }

        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found.");
        }

        if (asset.Status == FixedAssetStatus.Disposed)
        {
            return Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is disposed and cannot be revalued.");
        }

        decimal delta = newCarryingAmount - asset.NetBookValue;
        if (delta == 0m)
        {
            return new RevaluationResult(0m, newCarryingAmount, string.Empty);
        }

        // The asset cost moves by the change; the other side is split per asset (IAS 16.39-40) between this asset's surplus in the reserve and profit.
        (decimal toReserve, decimal toProfit) = asset.SplitRevaluation(delta);
        if ((toReserve != 0m && string.IsNullOrWhiteSpace(revaluationReserveAccountCode)) || (toProfit != 0m && string.IsNullOrWhiteSpace(impairmentExpenseAccountCode)))
        {
            return Error.Validation("Fa.RevaluationAccounts", $"Revaluing {asset.AssetNumber} moves {(toReserve != 0m ? "the revaluation reserve" : "profit")}: give its account.");
        }

        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = asset.CostAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = delta > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(delta) },
        ];
        if (toReserve != 0m)
        {
            lines.Add(new JournalLineRequest
            {
                AccountCode = revaluationReserveAccountCode, Side = toReserve > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(toReserve),
                Narrative = toReserve > 0m ? "Revaluation surplus" : "Decrease against this asset's revaluation surplus",
            });
        }

        if (toProfit != 0m)
        {
            lines.Add(new JournalLineRequest
            {
                AccountCode = impairmentExpenseAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = toProfit > 0m ? JournalSide.Debit : JournalSide.Credit,
                TransactionAmount = Math.Abs(toProfit), Narrative = toProfit > 0m ? "Revaluation decrease charged to profit" : "Reversal of an earlier revaluation decrease",
            });
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "FA-REVAL",
            DocumentNumber = documentNumber,
            Narrative = $"Revaluation of asset {asset.AssetNumber}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        asset.ApplyRevaluation(delta, toReserve, toProfit, revaluationReserveAccountCode);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RevaluationResult(delta, asset.NetBookValue, posted.Value.JournalNumber, toReserve, toProfit);
    }

    /// <inheritdoc />
    public async Task<Result> SaveClassAsync(AssetClassRequest request, CancellationToken ct = default)
    {
        string code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0 || string.IsNullOrWhiteSpace(request.Name) || !Enum.IsDefined(request.Method) || request.UsefulLifeMonths < 0 || request.ResidualPercent is < 0m or >= 100m
            || request.ReducingBalanceRatePercent < 0m || (request.Method == DepreciationMethod.StraightLine && request.UsefulLifeMonths == 0)
            || (request.Method == DepreciationMethod.ReducingBalance && request.ReducingBalanceRatePercent == 0m))
        {
            return Result.Failure(Error.Validation("Fa.Class", "A class needs a code, a name, a method with its life (straight line) or rate (reducing balance), and a residual of 0–99%."));
        }

        string[] accounts = [.. new[] { request.CostAccountCode, request.AccumulatedDepreciationAccountCode, request.DepreciationExpenseAccountCode, request.ImpairmentAccountCode }
            .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim().ToUpperInvariant())];
        if (accounts.Length < 3)
        {
            return Result.Failure(Error.Validation("Fa.ClassAccounts", "A class needs its cost, accumulated depreciation and depreciation expense accounts."));
        }

        List<string> known = await _db.Accounts.Where(a => a.CompanyId == request.CompanyId && accounts.Contains(a.AccountCode)).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        if (accounts.FirstOrDefault(a => !known.Contains(a)) is string unknown)
        {
            return Result.Failure(Error.NotFound("Fa.AccountNotFound", $"Account {unknown} does not exist."));
        }

        FixedAssetClass? assetClass = await _db.Set<FixedAssetClass>().FirstOrDefaultAsync(c => c.CompanyId == request.CompanyId && c.Code == code, ct).ConfigureAwait(false);
        if (assetClass is null)
        {
            assetClass = new FixedAssetClass(request.CompanyId, code, request.Name.Trim());
            _db.Set<FixedAssetClass>().Add(assetClass);
        }

        assetClass.Name = request.Name.Trim();
        assetClass.Method = request.Method;
        assetClass.UsefulLifeMonths = request.UsefulLifeMonths;
        assetClass.ResidualPercent = request.ResidualPercent;
        assetClass.ReducingBalanceRatePercent = request.ReducingBalanceRatePercent;
        assetClass.CostAccountCode = request.CostAccountCode.Trim().ToUpperInvariant();
        assetClass.AccumulatedDepreciationAccountCode = request.AccumulatedDepreciationAccountCode.Trim().ToUpperInvariant();
        assetClass.DepreciationExpenseAccountCode = request.DepreciationExpenseAccountCode.Trim().ToUpperInvariant();
        assetClass.ImpairmentAccountCode = string.IsNullOrWhiteSpace(request.ImpairmentAccountCode) ? null : request.ImpairmentAccountCode.Trim().ToUpperInvariant();
        assetClass.Convention = request.Convention;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<ImpairmentResult>> ImpairAsync(long companyId, long assetId, decimal recoverableAmount, DateOnly date, string reason, string documentNumber, string? impairmentAccountCode = null,
        CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (recoverableAmount < 0m || string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(documentNumber))
            {
                return Error.Validation("Fa.Impairment", "An impairment test needs a recoverable amount of nil or more, the indicator that prompted it and a document number.");
            }

            FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
            if (asset is null)
            {
                return Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found.");
            }

            if (asset.Status == FixedAssetStatus.Disposed)
            {
                return Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is disposed.");
            }

            string? account = string.IsNullOrWhiteSpace(impairmentAccountCode)
                ? await _db.Set<FixedAssetClass>().Where(c => c.CompanyId == companyId && c.Code == asset.AssetClass).Select(c => c.ImpairmentAccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                : impairmentAccountCode.Trim().ToUpperInvariant();
            if (account is null)
            {
                return Error.Validation("Fa.ImpairmentAccount", $"Give the impairment account, or set one on class {asset.AssetClass}.");
            }

            // A loss down to the recoverable amount; or a reversal of losses held, capped at the carrying amount with no impairment.
            decimal before = asset.NetBookValue;
            decimal amount = recoverableAmount < before ? before - recoverableAmount
                : -Math.Max(0m, Math.Min(recoverableAmount - before, Math.Min(asset.AccumulatedImpairment, asset.CarryingAmountWithoutImpairment - before)));
            string journal = string.Empty;
            if (amount != 0m)
            {
                Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
                Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
                {
                    CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = company.BaseCurrency, DocumentType = "FA-IMPAIR", DocumentNumber = documentNumber.Trim(),
                    Narrative = $"{(amount > 0m ? "Impairment" : "Impairment reversal")} of asset {asset.AssetNumber}: {reason.Trim()}",
                    Lines =
                    [
                        new JournalLineRequest { AccountCode = account, CostCentre = asset.CostCentre, Department = asset.Department, Side = amount > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(amount) },
                        new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = amount > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(amount) },
                    ],
                }, ct).ConfigureAwait(false);
                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                journal = posted.Value.JournalNumber;
                asset.Impair(amount);
            }

            _db.Set<AssetImpairment>().Add(new AssetImpairment(companyId, asset.Id, date, before, recoverableAmount, amount, reason.Trim(), journal));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<ImpairmentResult>.Success(new ImpairmentResult(amount, asset.NetBookValue, journal));
        }, ct);

    /// <inheritdoc />
    public async Task<Result> TransferAsync(long companyId, long assetId, string? newLocation, string? newCustodian, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newLocation) && string.IsNullOrWhiteSpace(newCustodian))
        {
            return Result.Failure(Error.Validation("Fa.NothingToTransfer", "Provide a new location and/or custodian."));
        }

        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure(Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found."));
        }

        if (asset.Status == FixedAssetStatus.Disposed)
        {
            return Result.Failure(Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is disposed and cannot be transferred."));
        }

        if (!string.IsNullOrWhiteSpace(newLocation))
        {
            asset.Location = newLocation;
        }

        if (!string.IsNullOrWhiteSpace(newCustodian))
        {
            asset.Custodian = newCustodian;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordMaintenanceAsync(long companyId, long assetId, DateOnly date, string description, decimal cost, string? provider, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation("Fa.MaintenanceNoDescription", "A maintenance log needs a description.");
        }

        if (cost < 0m)
        {
            return Error.Rule("Fa.MaintenanceNegativeCost", "The maintenance cost cannot be negative.");
        }

        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found.");
        }

        if (asset.Status == FixedAssetStatus.Disposed)
        {
            return Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is disposed; no maintenance can be logged.");
        }

        AssetMaintenance log = new(companyId, assetId, date, description, cost, provider);
        _db.Set<AssetMaintenance>().Add(log);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return log.Id;
    }

    /// <inheritdoc />
    public async Task<Result<DepreciationForecastReport>> DepreciationForecastAsync(long companyId, DateOnly startMonth, int months, CancellationToken ct = default)
    {
        if (months <= 0)
        {
            return Error.Rule("Fa.ForecastMonths", "The number of months to forecast must be positive.");
        }

        List<FixedAsset> assets = await _db.Set<FixedAsset>()
            .Where(a => a.CompanyId == companyId && a.Status == FixedAssetStatus.Active)
            .ToListAsync(ct).ConfigureAwait(false);

        // Simulate each asset month by month on local balances, never touching the entities. The same rule as the
        // run applies: no charge before the acquisition month, nor for a month the run has already charged.
        (decimal Nbv, decimal Residual, decimal Straight, decimal RbRate, DepreciationMethod Method, bool ByLife, int AcquiredPeriod, int LastCharged, int ProRataPeriod, decimal ProRataShare)[] state = assets
            .Select(a => (a.NetBookValue, a.ResidualValue,
                a.UsefulLifeMonths <= 0 ? 0m : decimal.Round(a.DepreciableAmount / a.UsefulLifeMonths, 4, MidpointRounding.ToEven),
                a.ReducingBalanceRatePercent, a.Method, a.Method == DepreciationMethod.StraightLine,
                a.FirstChargePeriod(), a.LastDepreciatedPeriod ?? 0, FixedAsset.PeriodOf(a.AcquisitionDate), a.FirstMonthShare(a.AcquisitionDate)))
            .ToArray();

        DateOnly month = new(startMonth.Year, startMonth.Month, 1);
        List<DepreciationForecastPeriod> periods = [];
        for (int m = 0; m < months; m++)
        {
            int period = FixedAsset.PeriodOf(month);
            decimal periodCharge = 0m;
            decimal closingNbv = 0m;
            for (int i = 0; i < state.Length; i++)
            {
                decimal remaining = state[i].Nbv - state[i].Residual;
                decimal charge = 0m;
                bool due = period >= state[i].AcquiredPeriod && period > state[i].LastCharged;
                if (due && remaining > 0m)
                {
                    charge = state[i].ByLife
                        ? state[i].Straight
                        : decimal.Round(state[i].Nbv * state[i].RbRate / 100m, 4, MidpointRounding.ToEven);
                    if (period == state[i].ProRataPeriod && state[i].ProRataShare < 1m)
                    {
                        charge = decimal.Round(charge * state[i].ProRataShare, 4, MidpointRounding.ToEven);
                    }

                    if (charge > remaining) { charge = remaining; }
                    state[i].Nbv -= charge;
                }

                periodCharge += charge;
                closingNbv += state[i].Nbv;
            }

            periods.Add(new DepreciationForecastPeriod(month.ToString("yyyy-MM"), periodCharge, closingNbv));
            month = month.AddMonths(1);
        }

        return new DepreciationForecastReport(new DateOnly(startMonth.Year, startMonth.Month, 1), months, periods);
    }
}
