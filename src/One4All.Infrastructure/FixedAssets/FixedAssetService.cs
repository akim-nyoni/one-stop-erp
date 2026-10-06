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
/// GL, and disposes assets recognising the profit or loss — all through the central posting engine.
/// </summary>
public sealed class FixedAssetService : IFixedAssetService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public FixedAssetService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<long>> RegisterAsync(RegisterAssetRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RegisterCoreAsync(request, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<long>> RegisterCoreAsync(RegisterAssetRequest request, CancellationToken ct)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Fa.CompanyNotFound", $"Company {request.CompanyId} not found.");
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
        };
        _db.Set<FixedAsset>().Add(asset);

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

            decimal charge = asset.MonthlyDepreciation(usage.GetValueOrDefault(asset.Id));
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

        decimal nbv = asset.NetBookValue;
        decimal gainOrLoss = proceeds - nbv;

        // Clear cost and accumulated depreciation, take the proceeds, and book the gain/loss balancing figure.
        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Debit, TransactionAmount = proceeds },
            new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Debit, TransactionAmount = asset.AccumulatedDepreciation },
            new JournalLineRequest { AccountCode = asset.CostAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Credit, TransactionAmount = asset.Cost },
        ];

        if (gainOrLoss > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = gainAccountCode, Side = JournalSide.Credit, TransactionAmount = gainOrLoss });
        }
        else if (gainOrLoss < 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = lossAccountCode, Side = JournalSide.Debit, TransactionAmount = -gainOrLoss });
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

        // Uplift → Dr asset cost / Cr revaluation reserve; write-down → Dr impairment expense / Cr asset cost.
        List<JournalLineRequest> lines = delta > 0m
            ?
            [
                new JournalLineRequest { AccountCode = asset.CostAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Debit, TransactionAmount = delta },
                new JournalLineRequest { AccountCode = revaluationReserveAccountCode, Side = JournalSide.Credit, TransactionAmount = delta },
            ]
            :
            [
                new JournalLineRequest { AccountCode = impairmentExpenseAccountCode, Side = JournalSide.Debit, TransactionAmount = -delta },
                new JournalLineRequest { AccountCode = asset.CostAccountCode, CostCentre = asset.CostCentre, Department = asset.Department, Side = JournalSide.Credit, TransactionAmount = -delta },
            ];

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

        asset.Revalue(delta);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RevaluationResult(delta, asset.NetBookValue, posted.Value.JournalNumber);
    }

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
        (decimal Nbv, decimal Residual, decimal Straight, decimal RbRate, DepreciationMethod Method, bool ByLife, int AcquiredPeriod, int LastCharged)[] state = assets
            .Select(a => (a.NetBookValue, a.ResidualValue,
                a.UsefulLifeMonths <= 0 ? 0m : decimal.Round(a.DepreciableAmount / a.UsefulLifeMonths, 4, MidpointRounding.ToEven),
                a.ReducingBalanceRatePercent, a.Method, a.Method == DepreciationMethod.StraightLine,
                FixedAsset.PeriodOf(a.AcquisitionDate), a.LastDepreciatedPeriod ?? 0))
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
